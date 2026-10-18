// SPDX-License-Identifier: MIT
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Riegel.UI;

/// <summary>Basis der ViewModels: Eigenschaften mit Änderungsmeldung (MVVM ohne fremde Bibliothek).</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Alle Bindungen neu lesen (nach dem Laden einer Anlage).</summary>
    protected void RaiseAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

/// <summary>Befehl mit Bedingung; WPF fragt die Bedingung bei jeder Eingabe neu ab (CommandManager).</summary>
public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter)) execute(parameter);
    }
}

/// <summary>
/// Asynchroner Befehl: läuft höchstens einmal gleichzeitig; Fehler meldet <see cref="Failed"/> (die Sitzung zeigt sie
/// in der Meldungsleiste), statt die App abstürzen zu lassen.
/// </summary>
public sealed class AsyncCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    private bool _running;

    public AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    /// <summary>Wird bei einem Fehler aufgerufen (von der Sitzung gesetzt).</summary>
    public static Action<Exception>? Failed { get; set; }

    /// <summary>Im Screenshot- und Testmodus Fehler nicht schlucken.</summary>
    public static bool Strict { get; set; }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _running = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await ExecuteAsync(parameter);
        }
        catch (Exception e) when (!Strict && e is not OutOfMemoryException)
        {
            Failed?.Invoke(e);
        }
        finally
        {
            _running = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Direkt ausführen (Tests, Screenshots, Tastenkürzel).</summary>
    public Task ExecuteAsync(object? parameter = null) => execute(parameter);
}
