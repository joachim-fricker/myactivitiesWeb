using System.Windows.Input;
using MyActivities.Data;
using Wpf.Ui.Controls;

namespace MyActivities.Views;

/// <summary>Große Karte in einem eigenen Fenster (Esc schließt).</summary>
public partial class MapWindow : FluentWindow
{
    private readonly string _script;

    /// <param name="title">Fenstertitel, z. B. Name der Aktivität</param>
    /// <param name="script">JavaScript-Aufruf für map.html, z. B. showTrack([...])</param>
    /// <param name="photos">Fotos der Tour (Klick auf ein Bildchen öffnet den Betrachter)</param>
    public MapWindow(string title, string script, IReadOnlyList<TourPhoto> photos)
    {
        InitializeComponent();
        WindowBounds.Apply(this, "map", widthFraction: 0.8, heightFraction: 0.85);
        Title = title;
        AppTitleBar.Title = title;
        _script = script;

        Loaded += async (_, _) =>
        {
            await MapHost.InitAsync(Map);
            Map.WebMessageReceived += (_, e) =>
            {
                if (MapHost.PhotoIndex(e) is int index && index < photos.Count)
                {
                    new PhotoWindow(photos, index, title) { Owner = this }.Show();
                }
            };
            await Map.ExecuteScriptAsync(_script);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }
}
