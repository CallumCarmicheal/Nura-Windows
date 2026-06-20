using System;
using System.Collections.Generic;
using System.Text;

namespace NuraTerm;

public sealed class AsyncDebouncer : IDisposable {
    private readonly object syncRoot = new();
    private readonly TimeSpan delay;

    private PendingDelay? pendingDelay;
    private bool disposed;

    public AsyncDebouncer(TimeSpan delay) {
        this.delay = delay;
    }

    public Task DebounceAsync(Action action) {
        return DebounceAsync(_ => {
            action();
            return Task.CompletedTask;
        });
    }

    public async Task DebounceAsync(Func<CancellationToken, Task> action) {
        PendingDelay currentDelay;
        PendingDelay? previousDelay;

        lock (syncRoot) {
            if (disposed)
                throw new ObjectDisposedException(nameof(AsyncDebouncer));

            previousDelay = pendingDelay;
            pendingDelay = currentDelay = new PendingDelay();
        }

        previousDelay?.Cancel();

        try {
            await Task.Delay(delay, currentDelay.Token);

            lock (syncRoot) {
                if (!ReferenceEquals(pendingDelay, currentDelay))
                    return;

                pendingDelay = null;
            }

            currentDelay.Token.ThrowIfCancellationRequested();

            await action(currentDelay.Token);
        } catch (OperationCanceledException) when (currentDelay.IsCancellationRequested) {
            // Expected when the debounce delay is reset or cancelled.
        } finally {
            // Only the owner of this CTS disposes it.
            currentDelay.Dispose();
        }
    }

    public void Cancel() {
        PendingDelay? delayToCancel;

        lock (syncRoot) {
            delayToCancel = pendingDelay;
            pendingDelay = null;
        }

        delayToCancel?.Cancel();
    }

    public void Dispose() {
        PendingDelay? delayToCancel;

        lock (syncRoot) {
            if (disposed)
                return;

            disposed = true;

            delayToCancel = pendingDelay;
            pendingDelay = null;
        }

        // Do not dispose this here. The owning DebounceAsync call will dispose it.
        delayToCancel?.Cancel();
    }

    private sealed class PendingDelay : IDisposable {
        private readonly object syncRoot = new();
        private readonly CancellationTokenSource cts = new();

        private bool disposed;

        public CancellationToken Token => cts.Token;

        public bool IsCancellationRequested => cts.IsCancellationRequested;

        public void Cancel() {
            lock (syncRoot) {
                if (disposed)
                    return;

                cts.Cancel();
            }
        }

        public void Dispose() {
            lock (syncRoot) {
                if (disposed)
                    return;

                disposed = true;
                cts.Dispose();
            }
        }
    }
}