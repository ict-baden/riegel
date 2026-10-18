// SPDX-License-Identifier: MIT
using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using Riegel.ViewModels;

namespace Riegel.Tests.Windows.App;

/// <summary>
/// Tests der Oberfläche laufen nacheinander: <see cref="UI.AsyncCommand"/> hat statische Fehlerbehandlung, und die
/// Demo-Anlage räumt alte Demo-Ordner im Temp-Ordner auf.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AppCollection
{
    public const string Name = "App";
}

/// <summary>
/// Führt einen asynchronen Test auf einem eigenen STA-Thread mit Dispatcher aus – wie in der App kommen Fortsetzungen
/// nach <c>await</c> auf denselben Thread zurück (WPF-Befehle, CommandManager, Sammlungen der ViewModels).
/// </summary>
internal static class Sta
{
    public static void Run(Func<Task> body, int timeoutSeconds = 180)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var frame = new DispatcherFrame();
            async void Start()
            {
                try { await body(); }
                catch (Exception e) { error = e; }
                finally { frame.Continue = false; }
            }
            dispatcher.BeginInvoke(new Action(Start));
            Dispatcher.PushFrame(frame);
            dispatcher.InvokeShutdown();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(timeoutSeconds))) throw new TimeoutException("Der Oberflächentest lief zu lange.");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    /// <summary>Warten, bis eine Bedingung gilt (async-void-Befehle nach einem Tastenkürzel).</summary>
    public static async Task Until(Func<bool> condition, string what, int timeoutSeconds = 60)
    {
        var end = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (!condition())
        {
            if (DateTime.UtcNow > end) throw new TimeoutException("Nicht eingetreten: " + what);
            await Task.Delay(25);
        }
    }
}

/// <summary>Dialoge für Tests: antworten ohne Fenster; Bearbeitungsdialoge füllt <see cref="OnShow"/> aus.</summary>
internal sealed class TestDialogs : IDialogs
{
    public Func<IDialogViewModel, bool>? OnShow { get; set; }
    public Func<string, string?>? OnPickFolder { get; set; }
    public List<string> Shown { get; } = [];
    public List<string> Revealed { get; } = [];

    public string? PickFolder(string title, string? initial = null) => OnPickFolder?.Invoke(title);
    public IReadOnlyList<string> PickFiles(string title, string filter, bool multiple) => [];
    public string? SaveFile(string title, string filter, string fileName) => null;
    public bool Confirm(string title, string text) => true;

    public bool Show(IDialogViewModel dialog)
    {
        Shown.Add(dialog.GetType().Name);
        return OnShow is not null && OnShow(dialog) && dialog.CanAccept && dialog.Accept();
    }

    public void ShowText(string title, string text) { }

    /// <summary>Nie den Explorer öffnen – nur merken.</summary>
    public string? Reveal(string path)
    {
        Revealed.Add(path);
        return null;
    }
}
