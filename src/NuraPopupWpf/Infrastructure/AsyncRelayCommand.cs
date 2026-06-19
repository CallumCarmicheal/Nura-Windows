using System.Windows.Input;

namespace NuraPopupWpf.Infrastructure;

public sealed class AsyncRelayCommand : ICommand {
    private readonly Func<object?, CancellationToken, Task> _execute;
    private readonly Predicate<object?>? _canExecute;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<object?, CancellationToken, Task> execute, Predicate<object?>? canExecute = null) {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool IsExecuting {
        get => _isExecuting;
        private set {
            if (_isExecuting == value) {
                return;
            }

            _isExecuting = value;
            RaiseCanExecuteChanged();
        }
    }

    public bool CanExecute(object? parameter) => !IsExecuting && (_canExecute?.Invoke(parameter) ?? true);

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
