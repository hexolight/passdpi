using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace PassDpi.App.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Func<object?, Task> _run;
    private readonly Func<bool>? _can;
    private bool _busy;

    public RelayCommand(Func<Task> run, Func<bool>? can = null) : this(_ => run(), can) { }
    public RelayCommand(Action run, Func<bool>? can = null) : this(_ => { run(); return Task.CompletedTask; }, can) { }
    public RelayCommand(Func<object?, Task> run, Func<bool>? can = null) { _run = run; _can = can; }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? p) => !_busy && (_can?.Invoke() ?? true);

    public async void Execute(object? p)
    {
        if (!CanExecute(p)) return;
        _busy = true;
        CommandManager.InvalidateRequerySuggested();
        try { await _run(p); }
        finally { _busy = false; CommandManager.InvalidateRequerySuggested(); }
    }
}
