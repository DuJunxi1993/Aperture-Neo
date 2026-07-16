using System;
using System.Windows.Input;

namespace ApertureNeo.ViewModels;

/// <summary>Minimal ICommand adapter for VM-side wiring.
/// CommunityToolkit.Mvvm's RelayCommand is also referenced
/// (and used elsewhere in the app via AsyncRelayCommand), but
/// defining a local copy here keeps the settings VM
/// self-contained and avoids dragging in MVVM-Toolkit
/// generics for a 5-line helper.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}