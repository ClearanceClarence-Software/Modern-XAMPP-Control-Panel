using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace XamppControl.ViewModels;

/// <summary>Base for view models: raises <see cref="PropertyChanged"/> so bindings update.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    /// <summary>Raised on the thread that changed the property.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Tells bindings that a property changed.</summary>
    /// <param name="propertyName">The property's name; filled in with the caller's name when left out.</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Stores a new value in a backing field and raises the change, unless the value is equal.</summary>
    /// <typeparam name="T">The property's type; compared with its default equality comparer.</typeparam>
    /// <param name="field">The backing field, written only when the value differs.</param>
    /// <param name="value">The new value.</param>
    /// <param name="propertyName">The property's name; filled in with the calling setter's name when left out.</param>
    /// <returns>True when the value changed and the event was raised; false when it was already equal.</returns>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

/// <summary>Minimal ICommand for sync and async actions. Disables itself while an async action runs.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isRunning;

    /// <summary>Wraps a synchronous action.</summary>
    /// <param name="execute">Runs on each execution; an exception goes to <see cref="UnhandledError"/>.</param>
    /// <param name="canExecute">Checked on every <see cref="CanExecute"/>; null means always enabled.</param>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(
            () =>
            {
                execute();
                return Task.CompletedTask;
            },
            canExecute)
    {
    }

    /// <summary>Wraps an asynchronous action; the command stays disabled until its task completes.</summary>
    /// <param name="execute">Started on each execution; an exception goes to <see cref="UnhandledError"/>.</param>
    /// <param name="canExecute">Checked on every <see cref="CanExecute"/>; null means always enabled.</param>
    public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <summary>Receives any exception an action throws, since an async void command has no caller.</summary>
    public static event Action<Exception>? UnhandledError;

    /// <summary>Raised when the action starts and ends, and on <see cref="RaiseCanExecuteChanged"/>.</summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>False while the action runs, otherwise the result of the condition given to the constructor.</summary>
    /// <param name="parameter">Ignored; the commands take no parameter.</param>
    /// <returns>True when the command can run now.</returns>
    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke() ?? true);

    /// <summary>Runs the action when the command can run, disabling the command until it completes.</summary>
    /// <param name="parameter">Ignored; the commands take no parameter.</param>
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isRunning = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute();
        }
        catch (Exception ex)
        {
            // Any action may fail, and an exception escaping an async void method would crash the app;
            // it is reported to UnhandledError instead (the main window logs it) and the command re-enables.
            UnhandledError?.Invoke(ex);
        }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    /// <summary>Makes controls query <see cref="CanExecute"/> again when its condition may have changed.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
