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

        // Beim ersten Start groß (85 % × 90 % des Bildschirms, 50 px schmaler), danach wie zuletzt verlassen
        WindowBounds.Apply(this, "mainwindow", widthFraction: 0.85, heightFraction: 0.9, widthAdjust: -50);

        // Hell/Dunkel automatisch an Windows anpassen
        SystemThemeWatcher.Watch(this);

        Loaded += (_, _) => RootNavigation.Navigate(typeof(AktivitaetenPage));
    }


}
