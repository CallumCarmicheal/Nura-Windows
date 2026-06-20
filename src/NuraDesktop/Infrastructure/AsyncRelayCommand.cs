using System.Windows.Input;

namespace NuraDesktop.Infrastructure;

public sealed class AsyncRelayCommand : ICommand {
    private readonly Func<object?, CancellationToken, Task> _execute;
    private readonly Predicate<object?>? _canExecute;
    private readonly bool _allowConcurrentExecutions;
    private int _executionCount;

    public AsyncRelayCommand(
        Func<object?, CancellationToken, Task> execute,
        Predicate<object?>? canExecute = null,
        bool allowConcurrentExecutions = false
    ) {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _allowConcurrentExecutions = allowConcurrentExecutions;
    }

    public event EventHandler? CanExecuteChanged;

    public bool IsExecuting {
        get => _executionCount > 0;
        private set {
            var nextCount = value ? _executionCount + 1 : Math.Max(0, _executionCount - 1);
            if (_executionCount == nextCount) {
                return;
            }

            _executionCount = nextCount;
            RaiseCanExecuteChanged();
        }
    }

    public bool CanExecute(object? parameter) =>
        (_allowConcurrentExecutions || !IsExecuting) &&
        (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter) {
        if (!CanExecute(parameter)) {
            return;
        }

        IsExecuting = true;
        try {
            await _execute(parameter, CancellationToken.None);
        } finally {
            IsExecuting = false;
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
