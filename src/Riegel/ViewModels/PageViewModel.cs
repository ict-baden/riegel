// SPDX-License-Identifier: MIT
using Riegel.Session;
using Riegel.UI;

namespace Riegel.ViewModels;

/// <summary>Eine Seite der linken Navigation (spec 7.8).</summary>
internal abstract class PageViewModel : ObservableObject
{
    private bool _selected;
    private string? _badge;
    private string _badgeTone = "";

    protected PageViewModel(AppSession session, IDialogs dialogs, string slug, string glyph)
    {
        Session = session;
        Dialogs = dialogs;
        Slug = slug;
        Glyph = glyph;
    }

    public AppSession Session { get; }

    protected IDialogs Dialogs { get; }

    /// <summary>Kürzel der Seite; auch Name der Screenshots (&lt;slug&gt;-light.png).</summary>
    public string Slug { get; }

    /// <summary>Symbol aus Segoe Fluent Icons.</summary>
    public string Glyph { get; }

    public string Title => Loc.T("Page_" + Slug);

    public string Subtitle => Loc.T("Page_" + Slug + "_Subtitle");

    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }

    public string? Badge { get => _badge; protected set => Set(ref _badge, value); }

    public string BadgeTone { get => _badgeTone; protected set => Set(ref _badgeTone, value); }

    /// <summary>Seite braucht eine geöffnete Anlage (sonst Hinweis „Anlage öffnen“).</summary>
    public virtual bool NeedsPlant => true;

    public bool ShowNoPlant => NeedsPlant && !Session.HasPlant;

    public PlantContext? Plant => Session.Plant;

    /// <summary>Seite wird sichtbar.</summary>
    public virtual void OnShown() { }

    /// <summary>Anlage wurde geöffnet, geschlossen oder neu geladen.</summary>
    public virtual void OnPlantChanged()
    {
        Raise(nameof(ShowNoPlant));
        Raise(nameof(Plant));
    }

    /// <summary>Festen Zustand für <c>--screenshots</c> herstellen (Auswahl, Filter).</summary>
    public virtual Task PrepareScreenshotAsync() => Task.CompletedTask;

    /// <summary>Strg+F: Suchfeld fokussieren (Seiten mit Suche geben true zurück).</summary>
    public virtual bool HasSearch => false;
}
