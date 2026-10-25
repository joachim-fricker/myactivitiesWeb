using System.Windows.Input;
using Wpf.Ui.Controls;

namespace MyActivities.Views;

/// <summary>Große Karte in einem eigenen Fenster (Esc schließt).</summary>
public partial class MapWindow : FluentWindow
{
    private readonly string _script;

    /// <param name="title">Fenstertitel, z. B. Name der Aktivität</param>
    /// <param name="script">JavaScript-Aufruf für map.html, z. B. showTrack([...])</param>
    public MapWindow(string title, string script)
    {
        InitializeComponent();
        Title = title;
        AppTitleBar.Title = title;
        _script = script;

        Loaded += async (_, _) =>
        {
            await MapHost.InitAsync(Map);
            await Map.ExecuteScriptAsync(_script);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }
}
