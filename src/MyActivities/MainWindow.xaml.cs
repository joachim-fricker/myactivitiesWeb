using System.Windows;
using MyActivities.Views;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace MyActivities;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();

        // Beim ersten Start groß (85 % × 90 % des Bildschirms), danach wie zuletzt verlassen
        WindowBounds.Apply(this, "main", widthFraction: 0.85, heightFraction: 0.9);

        // Hell/Dunkel automatisch an Windows anpassen
        SystemThemeWatcher.Watch(this);

        Loaded += (_, _) => RootNavigation.Navigate(typeof(StatistikPage));
    }

    /// <summary>Zurück zur Karte (Zustand der Karte bleibt erhalten).</summary>
    public void ShowMap() => RootNavigation.Navigate(typeof(KartePage));

    /// <summary>Zur Aktivitäten-Seite wechseln und dort die Tour auswählen.</summary>
    public void ShowActivity(long id)
    {
        AktivitaetenPage.PendingSelection = id;
        RootNavigation.Navigate(typeof(AktivitaetenPage));
    }
}
