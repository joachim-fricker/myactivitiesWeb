using System.Windows;
using MyActivities.Domain;

namespace MyActivities.Views;

/// <summary>Ein gemeinsames Detailfenster für alle Seiten (Tabelle, Karte, Kalender).</summary>
public static class TourDetails
{
    private static TourDetailWindow? _window;

    public static bool IsOpen => _window is not null;

    /// <summary>Detailfenster öffnen bzw. nach vorne holen und auf die Tour umstellen.</summary>
    public static void Open(Activity a, Window? owner)
    {
        if (_window is null)
        {
            _window = new TourDetailWindow { Owner = owner };
            _window.Closed += (_, _) => _window = null;
            _window.Show();
        }
        else
        {
            _window.Activate();
        }
        if (_window.CurrentActivity != a) _ = _window.ShowAsync(a);
    }

    /// <summary>Ist das Fenster offen, folgt es der Auswahl; sonst passiert nichts.</summary>
    public static void FollowIfOpen(Activity a)
    {
        if (_window is not null && _window.CurrentActivity != a) _ = _window.ShowAsync(a);
    }
}
