
namespace NuraApp;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

public sealed class AsyncConsoleLogger : IAsyncDisposable {
    private readonly Channel<ConsoleCommand> _queue;
    private readonly Task _pump;
    private readonly TextWriter _writer;
    private readonly bool _ansiEnabled;
    private readonly bool _canPinHoistedSections;
    private readonly Timer? _resizeTimer;
    private readonly SemaphoreSlim _inputGate = new(1, 1);
    private readonly TimeSpan _keyPollInterval;
    private readonly bool _keyIntercept;

    private CancellationTokenSource? _keyListenerCts;
    private Task? _keyListenerPump;
    private int _keyListenerStarted;
    private int _promptDepth;

    private readonly Dictionary<string, HoistedSection> _hoistedSections = new(StringComparer.Ordinal);
    private readonly List<string> _hoistedOrder = new();

    private int _completed;
    private int _lastWindowWidth;
    private int _lastWindowHeight;
    private int _lastHoistedHeight;
    private bool _hoistedDirty = true;
    private bool _suppressHoistedRendering;


    public AsyncConsoleLogger(
        TextWriter? writer = null,
        bool? enableAnsi = null,
        bool enableAnsiWhenOutputRedirected = false,
        bool tryEnableWindowsVirtualTerminal = true,
        int resizePollMilliseconds = 250,
        bool keyListenerIntercept = true,
        int keyListenerPollMilliseconds = 25) {
        _writer = writer ?? Console.Out;
        _keyIntercept = keyListenerIntercept;
        _keyPollInterval = TimeSpan.FromMilliseconds(Math.Max(1, keyListenerPollMilliseconds));

        if (tryEnableWindowsVirtualTerminal)
            AnsiConsoleSupport.TryEnableVirtualTerminalProcessing();

        _ansiEnabled =
            enableAnsi ??
            (!Console.IsOutputRedirected || enableAnsiWhenOutputRedirected);

        _canPinHoistedSections =
            _ansiEnabled &&
            !Console.IsOutputRedirected;

        _queue = Channel.CreateUnbounded<ConsoleCommand>(
            new UnboundedChannelOptions {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

        _pump = Task.Run(ProcessQueueAsync);

        if (_canPinHoistedSections && resizePollMilliseconds > 0) {
            _resizeTimer = new Timer(
                _ => {
                    if (!IsCompleted)
                        _queue.Writer.TryWrite(RefreshLayoutCommand.Instance);
                },
                null,
                resizePollMilliseconds,
                resizePollMilliseconds);
        }
    }

    public bool EnableHoistedBorder { get; set; } = false;

    public int MaxHoistedHeight { get; set; } = 8;

    public bool IsPromptActive => Volatile.Read(ref _promptDepth) > 0;

    public bool IsKeyListenerRunning => Volatile.Read(ref _keyListenerStarted) != 0;

    public event Action<ConsoleKeyInfo>? KeyPressed;

    public event Func<ConsoleKeyInfo, CancellationToken, ValueTask>? KeyPressedAsync;

    public Task Completion => _pump;

    public bool IsCompleted => Volatile.Read(ref _completed) != 0;

    public TaskAwaiter GetAwaiter() => _pump.GetAwaiter();

    public bool WriteLine(string text) {
        return WriteLine(new AnsiPart(text));
    }

    public bool WriteLine(params object?[] items) => WriteLine(AnsiLine.From(items).Parts);
    public bool WriteLine(params AnsiPart[] parts) {
        if (IsCompleted)
            return false;

        return _queue.Writer.TryWrite(
            new WriteLogCommand(CopyParts(parts), Completion: null));
    }

    public Task WriteLineAndWaitAsync(
        string text,
        CancellationToken cancellationToken = default) {
        return WriteLineAndWaitAsync(
            new[] { new AnsiPart(text) },
            cancellationToken);
    }

    public Task WriteLineAndWaitAsync(params AnsiPart[] parts) {
        return WriteLineAndWaitAsync(parts.AsEnumerable(), CancellationToken.None);
    }

    public Task WriteLineAndWaitAsync(
        IEnumerable<AnsiPart> parts,
        CancellationToken cancellationToken = default) {
        if (parts == null)
            throw new ArgumentNullException(nameof(parts));

        if (IsCompleted) {
            return Task.FromException(
                new InvalidOperationException("The console logger has already been completed."));
        }

        TaskCompletionSource<object?> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        bool queued = _queue.Writer.TryWrite(
            new WriteLogCommand(CopyParts(parts), completion));

        if (!queued) {
            return Task.FromException(
                new InvalidOperationException("The console logger has already been completed."));
        }

        if (!cancellationToken.CanBeCanceled)
            return completion.Task;

        return WaitWithCancellationAsync(completion.Task, cancellationToken);
    }

    private enum HoistedSectionMove {
        Top,
        Bottom,
        Before,
        After
    }

    private sealed record MoveHoistedSectionCommand(
        string Key,
        HoistedSectionMove Move,
        string? RelativeKey = null) : ConsoleCommand;

    public bool SetHoistedSection(string key, string text) {
        return SetHoistedSection(
            key,
            new AnsiLine(text));
    }

    public bool SetHoistedSection(string key, params AnsiPart[] parts) => SetHoistedSection(key, AnsiLine.From(parts));
    public bool SetHoistedSection(string key, params AnsiLine[] lines) {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        if (IsCompleted)
            return false;

        return _queue.Writer.TryWrite(
            new SetHoistedSectionCommand(
                key,
                CopyLines(lines),
                Completion: null));
    }

    public Task SetHoistedSectionAndWaitAsync(
        string key,
        string text,
        CancellationToken cancellationToken = default) {
        return SetHoistedSectionAndWaitAsync(
            key,
            new[]
            {
            new AnsiLine(text)
            },
            cancellationToken);
    }

    public Task SetHoistedSectionAndWaitAsync(
        string key,
        AnsiLine line,
        CancellationToken cancellationToken = default) {
        return SetHoistedSectionAndWaitAsync(
            key,
            new[]
            {
            line
            },
            cancellationToken);
    }

    public Task SetHoistedSectionAndWaitAsync(
        string key,
        IEnumerable<AnsiLine> lines,
        CancellationToken cancellationToken = default) {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        if (lines == null)
            throw new ArgumentNullException(nameof(lines));

        if (IsCompleted) {
            return Task.FromException(
                new InvalidOperationException("The console logger has already been completed."));
        }

        TaskCompletionSource<object?> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        bool queued = _queue.Writer.TryWrite(
            new SetHoistedSectionCommand(
                key,
                CopyLines(lines),
                completion));

        if (!queued) {
            return Task.FromException(
                new InvalidOperationException("The console logger has already been completed."));
        }

        if (!cancellationToken.CanBeCanceled)
            return completion.Task;

        return WaitWithCancellationAsync(completion.Task, cancellationToken);
    }

    public bool MoveHoistedSectionToBottom(string key) {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        if (IsCompleted)
            return false;

        return _queue.Writer.TryWrite(new MoveHoistedSectionCommand(key, HoistedSectionMove.Bottom));
    }

    public bool MoveHoistedSectionToTop(string key) {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        if (IsCompleted)
            return false;

        return _queue.Writer.TryWrite(new MoveHoistedSectionCommand(key, HoistedSectionMove.Top));
    }

    public bool MoveHoistedSectionBefore(string key, string beforeKey) {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        if (beforeKey == null)
            throw new ArgumentNullException(nameof(beforeKey));

        if (IsCompleted)
            return false;

        return _queue.Writer.TryWrite(new MoveHoistedSectionCommand(key, HoistedSectionMove.Before, beforeKey));
    }

    public bool MoveHoistedSectionAfter(string key, string afterKey) {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        if (afterKey == null)
            throw new ArgumentNullException(nameof(afterKey));

        if (IsCompleted)
            return false;

        return _queue.Writer.TryWrite(new MoveHoistedSectionCommand(key, HoistedSectionMove.After, afterKey));
    }

    public bool RemoveHoistedSection(string key) {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        if (IsCompleted)
            return false;

        return _queue.Writer.TryWrite(new RemoveHoistedSectionCommand(key));
    }

    public bool ClearHoistedSections() {
        if (IsCompleted)
            return false;

        return _queue.Writer.TryWrite(ClearHoistedSectionsCommand.Instance);
    }

    public Task<string?> PromptLineAsync(string prompt) {
        return PromptLineAsync(new[] { new AnsiPart(prompt) });
    }

    public Task<string?> PromptLineAsync(params AnsiPart[] promptParts) {
        return PromptLineAsync(promptParts.AsEnumerable());
    }

    public Task<string?> PromptLineAsync(IEnumerable<AnsiPart> promptParts) {
        if (promptParts == null)
            throw new ArgumentNullException(nameof(promptParts));

        if (IsCompleted) {
            return Task.FromException<string?>(
                new InvalidOperationException("The console logger has already been completed."));
        }

        TaskCompletionSource<string?> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        bool queued = _queue.Writer.TryWrite(
            new PromptLineCommand(CopyParts(promptParts), completion));

        if (!queued) {
            return Task.FromException<string?>(
                new InvalidOperationException("The console logger has already been completed."));
        }

        return completion.Task;
    }

    public Task<bool> PromptYesNoAsync(string prompt, bool defaultYes) {
        return PromptYesNoAsync(new[] { new AnsiPart(prompt) }, defaultYes);
    }

    public Task<bool> PromptYesNoAsync(
        IEnumerable<AnsiPart> promptParts,
        bool defaultYes) {
        if (promptParts == null)
            throw new ArgumentNullException(nameof(promptParts));

        if (IsCompleted) {
            return Task.FromException<bool>(
                new InvalidOperationException("The console logger has already been completed."));
        }

        TaskCompletionSource<bool> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        bool queued = _queue.Writer.TryWrite(
            new PromptYesNoCommand(
                CopyParts(promptParts),
                defaultYes,
                completion));

        if (!queued) {
            return Task.FromException<bool>(
                new InvalidOperationException("The console logger has already been completed."));
        }

        return completion.Task;
    }

    public bool StartKeyListener() {
        if (Console.IsInputRedirected)
            return false;

        if (IsCompleted)
            return false;

        if (Interlocked.CompareExchange(ref _keyListenerStarted, 1, 0) != 0)
            return true;

        CancellationTokenSource cts = new();
        _keyListenerCts = cts;
        _keyListenerPump = Task.Run(() => KeyListenerLoopAsync(cts.Token));

        return true;
    }

    public async Task StopKeyListenerAsync() {
        if (Interlocked.Exchange(ref _keyListenerStarted, 0) == 0)
            return;

        CancellationTokenSource? cts = _keyListenerCts;
        Task? pump = _keyListenerPump;

        if (cts != null)
            await cts.CancelAsync().ConfigureAwait(false);

        if (pump != null) {
            try {
                await pump.ConfigureAwait(false);
            } catch (OperationCanceledException) {
            }
        }

        cts?.Dispose();

        _keyListenerCts = null;
        _keyListenerPump = null;
    }

    public void Complete() {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
            _queue.Writer.TryComplete();
    }

    public async Task StopAsync() {
        await StopKeyListenerAsync().ConfigureAwait(false);

        Complete();

        _resizeTimer?.Dispose();

        await _pump.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task ProcessQueueAsync() {
        await foreach (ConsoleCommand command in _queue.Reader.ReadAllAsync().ConfigureAwait(false)) {
            switch (command) {
            case WriteLogCommand write:
                await HandleWriteLogAsync(write).ConfigureAwait(false);
                break;

            case SetHoistedSectionCommand setSection:
                await HandleSetHoistedSectionAsync(setSection).ConfigureAwait(false);
                break;

            case RemoveHoistedSectionCommand removeSection:
                await HandleRemoveHoistedSectionAsync(removeSection).ConfigureAwait(false);
                break;

            case ClearHoistedSectionsCommand:
                await HandleClearHoistedSectionsAsync().ConfigureAwait(false);
                break;

            case MoveHoistedSectionCommand moveSection:
                await HandleMoveHoistedSectionAsync(moveSection).ConfigureAwait(false);
                break;

            case PromptLineCommand promptLine:
                await HandlePromptLineAsync(promptLine).ConfigureAwait(false);
                break;

            case PromptYesNoCommand promptYesNo:
                await HandlePromptYesNoAsync(promptYesNo).ConfigureAwait(false);
                break;

            case RefreshLayoutCommand:
                await HandleRefreshLayoutAsync().ConfigureAwait(false);
                break;
            }
        }

        if (_canPinHoistedSections) {
            await ClearHoistedAreaAsync(_lastHoistedHeight).ConfigureAwait(false);
            await _writer.WriteAsync(Ansi.ResetScrollRegion).ConfigureAwait(false);

            if (TryGetConsoleSize(out _, out int height))
                await _writer.WriteAsync(Ansi.MoveCursor(Math.Max(1, height), 1)).ConfigureAwait(false);
        }

        await _writer.FlushAsync().ConfigureAwait(false);
    }

    private async Task HandleWriteLogAsync(WriteLogCommand command) {
        try {
            await WriteLogCoreAsync(command.Parts).ConfigureAwait(false);

            if (command.Completion != null)
                await _writer.FlushAsync().ConfigureAwait(false);

            command.Completion?.TrySetResult(null);
        } catch (Exception ex) {
            command.Completion?.TrySetException(ex);
        }
    }

    private async Task HandleSetHoistedSectionAsync(SetHoistedSectionCommand command) {
        try {
            if (!_hoistedSections.ContainsKey(command.Key))
                _hoistedOrder.Add(command.Key);

            _hoistedSections[command.Key] = new HoistedSection(command.Lines);
            _hoistedDirty = true;

            await RenderHoistedSectionsAsync().ConfigureAwait(false);

            command.Completion?.TrySetResult(null);
        } catch (Exception ex) {
            command.Completion?.TrySetException(ex);
        }
    }

    private async Task HandleRemoveHoistedSectionAsync(RemoveHoistedSectionCommand command) {
        if (_hoistedSections.Remove(command.Key)) {
            _hoistedOrder.Remove(command.Key);
            _hoistedDirty = true;

            await RenderHoistedSectionsAsync().ConfigureAwait(false);
        }
    }

    private async Task HandleClearHoistedSectionsAsync() {
        _hoistedSections.Clear();
        _hoistedOrder.Clear();
        _hoistedDirty = true;

        await RenderHoistedSectionsAsync().ConfigureAwait(false);
    }

    private async Task HandleMoveHoistedSectionAsync(MoveHoistedSectionCommand command) {
        if (!_hoistedSections.ContainsKey(command.Key))
            return;

        _hoistedOrder.Remove(command.Key);

        switch (command.Move) {
        case HoistedSectionMove.Top:
            _hoistedOrder.Insert(0, command.Key);
            break;

        case HoistedSectionMove.Bottom:
            _hoistedOrder.Add(command.Key);
            break;

        case HoistedSectionMove.Before:
            if (command.RelativeKey == null || !_hoistedOrder.Contains(command.RelativeKey)) {
                _hoistedOrder.Add(command.Key);
                break;
            }

            _hoistedOrder.Insert(_hoistedOrder.IndexOf(command.RelativeKey), command.Key);
            break;

        case HoistedSectionMove.After:
            if (command.RelativeKey == null || !_hoistedOrder.Contains(command.RelativeKey)) {
                _hoistedOrder.Add(command.Key);
                break;
            }

            _hoistedOrder.Insert(_hoistedOrder.IndexOf(command.RelativeKey) + 1, command.Key);
            break;
        }

        _hoistedDirty = true;

        await RenderHoistedSectionsAsync().ConfigureAwait(false);
    }

    private async Task HandlePromptLineAsync(PromptLineCommand command) {
        try {
            using IDisposable inputLease =
                await EnterPromptInputAsync().ConfigureAwait(false);

            await BeginPromptAsync().ConfigureAwait(false);

            try {
                await WritePromptCoreAsync(command.PromptParts).ConfigureAwait(false);

                string? result = Console.ReadLine();

                command.Completion.TrySetResult(result);
            } finally {
                await EndPromptAsync().ConfigureAwait(false);
            }
        } catch (Exception ex) {
            command.Completion.TrySetException(ex);
        }
    }

    private async Task HandlePromptYesNoAsync(PromptYesNoCommand command) {
        try {
            using IDisposable inputLease =
                await EnterPromptInputAsync().ConfigureAwait(false);

            await BeginPromptAsync().ConfigureAwait(false);

            try {
                while (true) {
                    await WritePromptCoreAsync(command.PromptParts).ConfigureAwait(false);

                    ConsoleKeyInfo response = Console.ReadKey(intercept: true);

                    if (response.Key == ConsoleKey.Enter) {
                        await _writer.WriteLineAsync("").ConfigureAwait(false);

                        command.Completion.TrySetResult(command.DefaultYes);
                        return;
                    }

                    await _writer.WriteLineAsync(response.KeyChar.ToString()).ConfigureAwait(false);

                    char lower = char.ToLowerInvariant(response.KeyChar);

                    if (lower == 'y' || lower == '1') {
                        command.Completion.TrySetResult(true);
                        return;
                    }

                    if (lower == 'n' || lower == '0') {
                        command.Completion.TrySetResult(false);
                        return;
                    }

                    await WriteLogCoreAsync(
                        new[]
                        {
                            AnsiPart.Warning("Please enter y or n.")
                        }).ConfigureAwait(false);
                }
            } finally {
                await EndPromptAsync().ConfigureAwait(false);
            }
        } catch (Exception ex) {
            command.Completion.TrySetException(ex);
        }
    }

    private async Task<IDisposable> EnterPromptInputAsync() {
        Interlocked.Increment(ref _promptDepth);

        try {
            await _inputGate.WaitAsync().ConfigureAwait(false);
            return new PromptInputLease(this);
        } catch {
            Interlocked.Decrement(ref _promptDepth);
            throw;
        }
    }

    private void ReleasePromptInput() {
        Interlocked.Decrement(ref _promptDepth);
        _inputGate.Release();
    }

    private async Task KeyListenerLoopAsync(CancellationToken cancellationToken) {
        try {
            while (!cancellationToken.IsCancellationRequested && !IsCompleted) {
                if (Console.IsInputRedirected || IsPromptActive) {
                    await Task.Delay(_keyPollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!TryGetKeyAvailable(out bool available) || !available) {
                    await Task.Delay(_keyPollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!_inputGate.Wait(0)) {
                    await Task.Delay(_keyPollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                ConsoleKeyInfo? key = null;

                try {
                    if (!IsPromptActive &&
                        TryGetKeyAvailable(out available) &&
                        available) {
                        key = Console.ReadKey(intercept: _keyIntercept);
                    }
                } finally {
                    _inputGate.Release();
                }

                if (key == null || IsPromptActive)
                    continue;

                DispatchKeyPressed(key.Value);

                await DispatchKeyPressedAsync(key.Value, cancellationToken).ConfigureAwait(false);
            }
        } catch (OperationCanceledException) {
        } finally {
            Interlocked.Exchange(ref _keyListenerStarted, 0);
        }
    }

    private void DispatchKeyPressed(ConsoleKeyInfo key) {
        Action<ConsoleKeyInfo>? handlers = KeyPressed;

        if (handlers == null)
            return;

        foreach (Delegate handlerDelegate in handlers.GetInvocationList()) {
            Action<ConsoleKeyInfo> handler =
                (Action<ConsoleKeyInfo>)handlerDelegate;

            try {
                handler(key);
            } catch (Exception ex) {
                WriteLine(
                    AnsiPart.Error("Key handler failed: "),
                    ex.Message);
            }
        }
    }

    private async ValueTask DispatchKeyPressedAsync(
        ConsoleKeyInfo key,
        CancellationToken cancellationToken) {
        Func<ConsoleKeyInfo, CancellationToken, ValueTask>? handlers = KeyPressedAsync;

        if (handlers == null)
            return;

        foreach (Delegate handlerDelegate in handlers.GetInvocationList()) {
            Func<ConsoleKeyInfo, CancellationToken, ValueTask> handler =
                (Func<ConsoleKeyInfo, CancellationToken, ValueTask>)handlerDelegate;

            try {
                await handler(key, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) {
                WriteLine(
                    AnsiPart.Error("Async key handler failed: "),
                    ex.Message);
            }
        }
    }

    private async Task HandleRefreshLayoutAsync() {
        if (!_canPinHoistedSections || _suppressHoistedRendering)
            return;

        if (!TryGetConsoleSize(out int width, out int height))
            return;

        if (width == _lastWindowWidth && height == _lastWindowHeight)
            return;

        await RedrawHoistedSectionsAfterResizeAsync(width, height).ConfigureAwait(false);
    }

    private async Task RedrawHoistedSectionsAfterResizeAsync(int width, int height) {
        await _writer.WriteAsync(Ansi.SaveCursor).ConfigureAwait(false);

        try {
            await _writer.WriteAsync(Ansi.ResetScrollRegion).ConfigureAwait(false);

            int maxPanelHeight = Math.Min(
                Math.Max(0, MaxHoistedHeight),
                Math.Max(0, height - 1));

            List<AnsiPart[]> physicalLines =
                maxPanelHeight <= 0
                    ? new List<AnsiPart[]>()
                    : BuildHoistedPhysicalLines(width, maxPanelHeight);

            int oldHoistedHeight = _lastHoistedHeight;
            int newHoistedHeight = physicalLines.Count;

            await MakeRoomForHoistedGrowthAsync(
                oldHoistedHeight,
                newHoistedHeight,
                height).ConfigureAwait(false);

            int clearHeight = Math.Max(oldHoistedHeight, newHoistedHeight);

            await ClearBottomRowsAsync(clearHeight, height).ConfigureAwait(false);

            if (newHoistedHeight > 0) {
                int startRow = height - newHoistedHeight + 1;

                for (int i = 0; i < physicalLines.Count; i++) {
                    int row = startRow + i;

                    await _writer.WriteAsync(Ansi.MoveCursor(row, 1)).ConfigureAwait(false);
                    await _writer.WriteAsync(Ansi.ClearLine).ConfigureAwait(false);
                    await _writer.WriteAsync(Render(physicalLines[i])).ConfigureAwait(false);
                }

                int logBottom = Math.Max(1, height - newHoistedHeight);

                await _writer.WriteAsync(Ansi.SetScrollRegion(1, logBottom)).ConfigureAwait(false);
            } else {
                await _writer.WriteAsync(Ansi.ResetScrollRegion).ConfigureAwait(false);
            }

            _lastWindowWidth = width;
            _lastWindowHeight = height;
            _lastHoistedHeight = newHoistedHeight;
            _hoistedDirty = false;
        } finally {
            await _writer.WriteAsync(Ansi.RestoreCursor).ConfigureAwait(false);
            await _writer.FlushAsync().ConfigureAwait(false);
        }
    }

    private async Task BeginPromptAsync() {
        if (!_canPinHoistedSections)
            return;

        // Keep the hoisted/status area visible while prompting.
        _suppressHoistedRendering = false;

        await EnsureHoistedLayoutAsync().ConfigureAwait(false);

        if (_lastHoistedHeight <= 0)
            return;

        if (!TryGetConsoleSize(out int width, out int height))
            return;

        int logBottom = Math.Max(1, height - _lastHoistedHeight);

        bool hasSavedCursor = TryGetCursorPosition(out int savedLeft, out int savedTop);

        int restoreRow = hasSavedCursor ? savedTop + 1 : logBottom;
        int restoreColumn = hasSavedCursor ? savedLeft + 1 : 1;

        // If the cursor somehow ended up inside the hoisted/status area,
        // move it back into the log/input region.
        restoreRow = Math.Min(restoreRow, logBottom);

        restoreRow = Math.Clamp(restoreRow, 1, Math.Max(1, height));
        restoreColumn = Math.Clamp(restoreColumn, 1, Math.Max(1, width));

        // Important:
        // Setting the scroll region can move the cursor to 1,1 on some terminals.
        // So we set the region first, then restore the cursor.
        await _writer.WriteAsync(Ansi.SetScrollRegion(1, logBottom)).ConfigureAwait(false);
        await _writer.WriteAsync(Ansi.MoveCursor(restoreRow, restoreColumn)).ConfigureAwait(false);

        await _writer.FlushAsync().ConfigureAwait(false);
    }

    private async Task EndPromptAsync() {
        if (!_canPinHoistedSections)
            return;

        _suppressHoistedRendering = false;

        // The hoisted area was never hidden, so normally there is nothing to restore.
        // Still re-render if a resize/layout change was queued while the prompt was active.
        if (_hoistedDirty)
            await RenderHoistedSectionsAsync().ConfigureAwait(false);
    }

    private async Task WriteLogCoreAsync(IReadOnlyList<AnsiPart> parts) {
        if (_canPinHoistedSections && !_suppressHoistedRendering) {
            await EnsureHoistedLayoutAsync().ConfigureAwait(false);

            if (_lastHoistedHeight > 0 && TryGetConsoleSize(out _, out int height)) {
                int logBottom = Math.Max(1, height - _lastHoistedHeight);

                if (TryGetCursorPosition(out int left, out int top)) {
                    int currentRow = top + 1;

                    if (currentRow > logBottom)
                        await _writer.WriteAsync(Ansi.MoveCursor(logBottom, 1)).ConfigureAwait(false);
                    else if (left != 0)
                        await _writer.WriteLineAsync("").ConfigureAwait(false);
                }

                await _writer.WriteLineAsync(Render(parts)).ConfigureAwait(false);
                return;
            }
        }

        await _writer.WriteLineAsync(Render(parts)).ConfigureAwait(false);
    }

    private async Task WritePromptCoreAsync(IReadOnlyList<AnsiPart> parts) {
        await _writer.WriteAsync(Render(parts)).ConfigureAwait(false);
        await _writer.FlushAsync().ConfigureAwait(false);
    }

    private async Task EnsureHoistedLayoutAsync() {
        if (!_canPinHoistedSections || _suppressHoistedRendering)
            return;

        if (!TryGetConsoleSize(out int width, out int height))
            return;

        if (width != _lastWindowWidth || height != _lastWindowHeight)
            _hoistedDirty = true;

        if (_hoistedDirty)
            await RenderHoistedSectionsAsync().ConfigureAwait(false);
    }

    private async Task RenderHoistedSectionsAsync() {
        if (!_canPinHoistedSections || _suppressHoistedRendering)
            return;

        if (!TryGetConsoleSize(out int width, out int height))
            return;

        bool hasSavedCursor = TryGetCursorPosition(out int savedLeft, out int savedTop);

        int maxPanelHeight = Math.Min(
            Math.Max(0, MaxHoistedHeight),
            Math.Max(0, height - 1));

        List<AnsiPart[]> physicalLines =
            maxPanelHeight <= 0
                ? new List<AnsiPart[]>()
                : BuildHoistedPhysicalLines(width, maxPanelHeight);

        int oldHoistedHeight = _lastHoistedHeight;
        int newHoistedHeight = physicalLines.Count;

        await MakeRoomForHoistedGrowthAsync(
            oldHoistedHeight,
            newHoistedHeight,
            height).ConfigureAwait(false);

        int clearHeight = Math.Max(oldHoistedHeight, newHoistedHeight);

        await ClearHoistedAreaAsync(clearHeight).ConfigureAwait(false);

        int logBottom = height;

        if (newHoistedHeight > 0) {
            int startRow = height - newHoistedHeight + 1;

            for (int i = 0; i < physicalLines.Count; i++) {
                int row = startRow + i;

                await _writer.WriteAsync(Ansi.MoveCursor(row, 1)).ConfigureAwait(false);
                await _writer.WriteAsync(Ansi.ClearLine).ConfigureAwait(false);
                await _writer.WriteAsync(Render(physicalLines[i])).ConfigureAwait(false);
            }

            logBottom = Math.Max(1, height - newHoistedHeight);

            await _writer.WriteAsync(Ansi.SetScrollRegion(1, logBottom)).ConfigureAwait(false);
        } else {
            await _writer.WriteAsync(Ansi.ResetScrollRegion).ConfigureAwait(false);
        }

        if (hasSavedCursor) {
            int restoreRow = savedTop + 1;
            int restoreColumn = savedLeft + 1;

            int restoreLogBottom = newHoistedHeight > 0
                ? Math.Max(1, height - newHoistedHeight)
                : height;

            restoreRow = Math.Min(restoreRow, restoreLogBottom);

            restoreRow = Math.Clamp(restoreRow, 1, Math.Max(1, height));
            restoreColumn = Math.Clamp(restoreColumn, 1, Math.Max(1, width));

            await _writer.WriteAsync(Ansi.MoveCursor(restoreRow, restoreColumn)).ConfigureAwait(false);
        } else if (newHoistedHeight > 0) {
            await _writer.WriteAsync(Ansi.MoveCursor(logBottom, 1)).ConfigureAwait(false);
        }

        _lastWindowWidth = width;
        _lastWindowHeight = height;
        _lastHoistedHeight = newHoistedHeight;
        _hoistedDirty = false;

        await _writer.FlushAsync().ConfigureAwait(false);
    }

    private async Task ClearHoistedAreaAsync(int heightToClear) {
        if (!_canPinHoistedSections || heightToClear <= 0)
            return;

        if (!TryGetConsoleSize(out _, out int height))
            return;

        await ClearBottomRowsAsync(heightToClear, height).ConfigureAwait(false);
    }

    private async Task ClearBottomRowsAsync(int rowsToClear, int height) {
        if (!_canPinHoistedSections || rowsToClear <= 0)
            return;

        int startRow = Math.Max(1, height - rowsToClear + 1);

        for (int row = startRow; row <= height; row++) {
            await _writer.WriteAsync(Ansi.MoveCursor(row, 1)).ConfigureAwait(false);
            await _writer.WriteAsync(Ansi.ClearLine).ConfigureAwait(false);
        }
    }

    private async Task MakeRoomForHoistedGrowthAsync(
            int oldHoistedHeight,
            int newHoistedHeight,
            int windowHeight
    ) {
        if (!_canPinHoistedSections)
            return;

        int growth = newHoistedHeight - oldHoistedHeight;

        if (growth <= 0)
            return;

        // The old log region included everything above the old hoisted area.
        // If there was no old hoisted area, the whole visible window was the log region.
        int oldLogBottom = oldHoistedHeight > 0
            ? Math.Max(1, windowHeight - oldHoistedHeight)
            : Math.Max(1, windowHeight);

        await _writer.WriteAsync(Ansi.SaveCursor).ConfigureAwait(false);

        try {
            // Make the old log region scroll, then emit enough newlines to push
            // existing log content up out of the rows that are about to become hoisted.
            await _writer.WriteAsync(Ansi.SetScrollRegion(1, oldLogBottom)).ConfigureAwait(false);
            await _writer.WriteAsync(Ansi.MoveCursor(oldLogBottom, 1)).ConfigureAwait(false);

            for (int i = 0; i < growth; i++)
                await _writer.WriteLineAsync("").ConfigureAwait(false);
        } finally {
            await _writer.WriteAsync(Ansi.RestoreCursor).ConfigureAwait(false);
        }
    }

    private List<AnsiPart[]> BuildHoistedPhysicalLines(int width, int maxPanelHeight) {
        List<AnsiPart[]> lines = new();

        foreach (string key in _hoistedOrder) {
            if (!_hoistedSections.TryGetValue(key, out HoistedSection? section))
                continue;

            foreach (AnsiPart[] logicalLine in section.Lines)
                lines.AddRange(WrapAnsiParts(logicalLine, Math.Max(1, width)));
        }

        if (lines.Count == 0)
            return lines;

        int contentHeight = EnableHoistedBorder
            ? Math.Max(0, maxPanelHeight - 1)
            : maxPanelHeight;

        if (contentHeight <= 0)
            return new List<AnsiPart[]>();

        if (lines.Count > contentHeight) {
            int hidden = lines.Count - contentHeight + 1;

            lines = lines
                .Take(Math.Max(0, contentHeight - 1))
                .ToList();

            lines.Add(
                new[]
                {
                    AnsiPart.FgHex($"… {hidden} more line(s)", 0x9CA3AF)
                });
        }

        if (EnableHoistedBorder) {
            lines.Insert(
                0,
                new[]
                {
                    AnsiPart.FgHex(new string('─', Math.Max(1, width)), 0x4B5563)
                });
        }

        return lines;
    }

    private static List<AnsiPart[]> WrapAnsiParts(AnsiPart[] parts, int width) {
        List<List<AnsiPart>> result = new();
        List<AnsiPart> current = new();
        int column = 0;

        void FinishLine() {
            result.Add(current);
            current = new List<AnsiPart>();
            column = 0;
        }

        void AddSegment(string text, AnsiStyle style) {
            if (text.Length == 0)
                return;

            if (current.Count > 0 && current[^1].Style.Equals(style)) {
                AnsiPart previous = current[^1];
                current[^1] = new AnsiPart(previous.Text + text, style);
            } else {
                current.Add(new AnsiPart(text, style));
            }

            column += text.Length;
        }

        foreach (AnsiPart part in parts) {
            foreach ((string token, bool isWhitespace) in Tokenize(part.Text)) {
                if (isWhitespace) {
                    if (column > 0 && column < width)
                        AddSegment(" ", part.Style);

                    continue;
                }

                string remaining = token;

                while (remaining.Length > 0) {
                    if (column >= width)
                        FinishLine();

                    if (column > 0 && column + remaining.Length > width) {
                        FinishLine();
                        continue;
                    }

                    int take = Math.Min(width - column, remaining.Length);

                    AddSegment(remaining[..take], part.Style);

                    remaining = remaining[take..];

                    if (remaining.Length > 0)
                        FinishLine();
                }
            }
        }

        if (current.Count > 0 || result.Count == 0)
            result.Add(current);

        return result
            .Select(x => x.ToArray())
            .ToList();
    }

    private static IEnumerable<(string Token, bool IsWhitespace)> Tokenize(string text) {
        if (string.IsNullOrEmpty(text))
            yield break;

        int start = 0;
        bool whitespace = char.IsWhiteSpace(text[0]);

        for (int i = 1; i < text.Length; i++) {
            bool currentWhitespace = char.IsWhiteSpace(text[i]);

            if (currentWhitespace == whitespace)
                continue;

            yield return (text[start..i], whitespace);

            start = i;
            whitespace = currentWhitespace;
        }

        yield return (text[start..], whitespace);
    }

    private string Render(IReadOnlyList<AnsiPart> parts) {
        if (!_ansiEnabled)
            return string.Concat(parts.Select(x => x.Text));

        StringBuilder sb = new();

        foreach (AnsiPart part in parts) {
            AppendStyle(sb, part.Style);

            sb.Append(part.Text);

            if (part.Style.HasAnyStyle)
                sb.Append(Ansi.Reset);
        }

        return sb.ToString();
    }

    private static void AppendStyle(StringBuilder sb, AnsiStyle style) {
        if (style.Foreground != null)
            sb.Append(Ansi.Foreground(style.Foreground.Value));

        if (style.Background != null)
            sb.Append(Ansi.Background(style.Background.Value));

        if (style.Bold)
            sb.Append(Ansi.Bold);

        if (style.Dim)
            sb.Append(Ansi.Dim);

        if (style.Underline)
            sb.Append(Ansi.Underline);
    }

    private static bool TryGetConsoleSize(out int width, out int height) {
        try {
            width = Console.WindowWidth;
            height = Console.WindowHeight;

            return width > 0 && height > 0;
        } catch {
            width = 0;
            height = 0;

            return false;
        }
    }

    private static bool TryGetCursorPosition(out int left, out int top) {
        try {
            (left, top) = Console.GetCursorPosition();
            return true;
        } catch {
            left = 0;
            top = 0;
            return false;
        }
    }

    private static bool TryGetKeyAvailable(out bool available) {
        try {
            available = Console.KeyAvailable;
            return true;
        } catch (IOException) {
            available = false;
            return false;
        } catch (InvalidOperationException) {
            available = false;
            return false;
        }
    }

    private static AnsiPart[] CopyParts(IEnumerable<AnsiPart> parts) {
        return parts.ToArray();
    }

    private static List<AnsiPart[]> CopyLines(IEnumerable<AnsiLine> lines) {
        return lines
            .Select(x => CopyParts(x.Parts))
            .ToList();
    }

    private static async Task WaitWithCancellationAsync(
        Task task,
        CancellationToken cancellationToken) {
        Task cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        Task completed = await Task.WhenAny(task, cancellationTask).ConfigureAwait(false);

        if (completed == cancellationTask)
            throw new OperationCanceledException(cancellationToken);

        await task.ConfigureAwait(false);
    }

    private sealed class PromptInputLease : IDisposable {
        private readonly AsyncConsoleLogger _owner;
        private int _disposed;

        public PromptInputLease(AsyncConsoleLogger owner) {
            _owner = owner;
        }

        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _owner.ReleasePromptInput();
        }
    }

    private sealed class HoistedSection {
        public HoistedSection(List<AnsiPart[]> lines) {
            Lines = lines;
        }

        public List<AnsiPart[]> Lines { get; }
    }

    private abstract record ConsoleCommand;

    private sealed record WriteLogCommand(
        AnsiPart[] Parts,
        TaskCompletionSource<object?>? Completion) : ConsoleCommand;

    private sealed record SetHoistedSectionCommand(
        string Key,
        List<AnsiPart[]> Lines,
        TaskCompletionSource<object?>? Completion) : ConsoleCommand;

    private sealed record RemoveHoistedSectionCommand(
        string Key) : ConsoleCommand;

    private sealed record ClearHoistedSectionsCommand : ConsoleCommand {
        public static readonly ClearHoistedSectionsCommand Instance = new();
    }

    private sealed record PromptLineCommand(
        AnsiPart[] PromptParts,
        TaskCompletionSource<string?> Completion) : ConsoleCommand;

    private sealed record PromptYesNoCommand(
        AnsiPart[] PromptParts,
        bool DefaultYes,
        TaskCompletionSource<bool> Completion) : ConsoleCommand;

    private sealed record RefreshLayoutCommand : ConsoleCommand {
        public static readonly RefreshLayoutCommand Instance = new();
    }
}
public readonly record struct Rgb(byte R, byte G, byte B) {
    public static Rgb FromHex(int hex) {
        return new Rgb(
            (byte)((hex >> 16) & 0xFF),
            (byte)((hex >> 8) & 0xFF),
            (byte)(hex & 0xFF));
    }
}

public readonly record struct AnsiStyle(
    Rgb? Foreground = null,
    Rgb? Background = null,
    bool Bold = false,
    bool Dim = false,
    bool Underline = false) {
    public bool HasAnyStyle =>
        Foreground != null ||
        Background != null ||
        Bold ||
        Dim ||
        Underline;

    public static AnsiStyle Fg(byte r, byte g, byte b) {
        return new AnsiStyle(Foreground: new Rgb(r, g, b));
    }

    public static AnsiStyle Bg(byte r, byte g, byte b) {
        return new AnsiStyle(Background: new Rgb(r, g, b));
    }

    public static AnsiStyle FgHex(int hex) {
        return new AnsiStyle(Foreground: Rgb.FromHex(hex));
    }

    public static AnsiStyle BgHex(int hex) {
        return new AnsiStyle(Background: Rgb.FromHex(hex));
    }
}

public readonly record struct AnsiPart(string Text, AnsiStyle Style = default) {
    public static implicit operator AnsiPart(string text) {
        return new AnsiPart(text);
    }

    public static AnsiPart Fg(string text, byte r, byte g, byte b) {
        return new AnsiPart(text, AnsiStyle.Fg(r, g, b));
    }

    public static AnsiPart Bg(string text, byte r, byte g, byte b) {
        return new AnsiPart(text, AnsiStyle.Bg(r, g, b));
    }

    public static AnsiPart FgHex(string text, int hex) {
        return new AnsiPart(text, AnsiStyle.FgHex(hex));
    }

    public static AnsiPart BgHex(string text, int hex) {
        return new AnsiPart(text, AnsiStyle.BgHex(hex));
    }

    public static AnsiPart Dim(string text) {
        return new AnsiPart(
            text,
            new AnsiStyle(
                Foreground: Rgb.FromHex(0x9CA3AF),
                Dim: true));
    }

    public static AnsiPart Error(string text) {
        return FgHex(text, 0xEF4444);
    }

    public static AnsiPart Warning(string text) {
        return FgHex(text, 0xF59E0B);
    }

    public static AnsiPart Success(string text) {
        return FgHex(text, 0x22C55E);
    }

    public static AnsiPart Info(string text) {
        return FgHex(text, 0x3B82F6);
    }
}

public static class Ansi {
    public const string Escape = "\u001b[";

    public const string Reset = Escape + "0m";
    public const string Bold = Escape + "1m";
    public const string Dim = Escape + "2m";
    public const string Underline = Escape + "4m";

    public const string ClearLine = Escape + "2K";
    public const string ResetScrollRegion = Escape + "r";

    public const string SaveCursor = "\u001b7";
    public const string RestoreCursor = "\u001b8";

    public static string Foreground(Rgb rgb) {
        return $"{Escape}38;2;{rgb.R};{rgb.G};{rgb.B}m";
    }

    public static string Background(Rgb rgb) {
        return $"{Escape}48;2;{rgb.R};{rgb.G};{rgb.B}m";
    }

    public static string MoveCursor(int row, int column) {
        return $"{Escape}{row};{column}H";
    }

    public static string SetScrollRegion(int top, int bottom) {
        return $"{Escape}{top};{bottom}r";
    }
}

public readonly record struct AnsiLine {
    public AnsiLine(params AnsiPart[] parts) {
        Parts = parts ?? Array.Empty<AnsiPart>();
    }

    public AnsiPart[] Parts { get; }

    public static implicit operator AnsiLine(string text) {
        return new AnsiLine(text);
    }

    public static AnsiLine From(params object?[] items) {
        List<AnsiPart> parts = new();

        foreach (object? item in items) {
            switch (item) {
            case null:
                break;

            case string text:
                parts.Add(new AnsiPart(text));
                break;

            case AnsiPart part:
                parts.Add(part);
                break;

            case AnsiPart[] partArray:
                parts.AddRange(partArray);
                break;

            case IEnumerable<AnsiPart> partEnumerable:
                parts.AddRange(partEnumerable);
                break;

            default:
                parts.Add(new AnsiPart(item.ToString() ?? string.Empty));
                break;
            }
        }

        return new AnsiLine(parts.ToArray());
    }
}

internal static class AnsiConsoleSupport {
    private const int StdOutputHandle = -11;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    public static void TryEnableVirtualTerminalProcessing() {
        if (!OperatingSystem.IsWindows())
            return;

        nint handle = GetStdHandle(StdOutputHandle);

        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            return;

        if (!GetConsoleMode(handle, out uint mode))
            return;

        mode |= EnableVirtualTerminalProcessing;

        SetConsoleMode(handle, mode);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);
}
