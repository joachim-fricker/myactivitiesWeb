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

        // Hell/Dunkel automatisch an Windows anpassen
        SystemThemeWatcher.Watch(this);

        Loaded += (_, _) => RootNavigation.Navigate(typeof(StatistikPage));
    }
}
