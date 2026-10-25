using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MyActivities.Data;
using MyActivities.Domain;

namespace MyActivities.Views;

/// <summary>
/// Heatmap aller Tracks: weit draußen eine klassische Heatmap, beim Hineinzoomen einzelne
/// halbtransparente Linien – oft gefahrene Wege werden dunkler. Klick zeigt die Touren dort.
/// </summary>
public partial class HeatmapPage : Page
{
    private List<Activity> _all = [];
    private Dictionary<long, Activity> _byId = [];
    private Dictionary<long, List<GeoPoint>> _tracks = [];
    private Task? _mapInit;
    private int _dbCount = -1;
    private bool _updatingFilters;
    private bool _firstShow = true;
    private int _request;

    public HeatmapPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            int count = AppServices.Db.Count();
            if (count == _dbCount) return;   // zurück von einer anderen Seite: Karte so lassen
            _dbCount = count;

            LoadData();
            _mapInit ??= InitMapAsync();
            await _mapInit;
            await LoadTracksAndShowAsync();
        };
    }

    private async Task InitMapAsync()
    {
        await MapHost.InitAsync(Map);
        Map.WebMessageReceived += (_, e) =>
        {
            if (MapHost.NearIds(e) is { } near) ShowNear(near.Ids, near.TooFar);
        };
    }

    private void LoadData()
    {
        // nur Touren mit GPS, ohne Indoor-Fahrten (virtuelle Strecken)
        _all = AppServices.Db.AllActivities()
            .Where(a => a.StartLat is not null && !Stats.IsIndoor(a.TypeKey, a.Name))
            .ToList();
        _byId = _all.ToDictionary(a => a.Id);

        _updatingFilters = true;
        YearBox.Items.Clear();
        YearBox.Items.Add(new ComboBoxItem { Content = "Alle Jahre" });
        foreach (var year in _all.Select(a => a.Year).Distinct().OrderDescending())
            YearBox.Items.Add(new ComboBoxItem { Content = year.ToString(), Tag = year });
        YearBox.SelectedIndex = 0;
        _updatingFilters = false;
    }

    private async Task LoadTracksAndShowAsync()
    {
        var progress = new Progress<(int Done, int Total)>(p =>
            StatusText.Text = $"Lade Tracks: {p.Done} / {p.Total} … (beim ersten Mal von GitHub, danach aus dem Cache)");
        StatusText.Text = "Lade Tracks …";
        _tracks = await AppServices.Heat.LoadAsync(_all, progress);
        await ShowAsync();
    }

    private IEnumerable<Activity> Filtered()
    {
        var group = (GroupBox.SelectedItem as ComboBoxItem)?.Content as string ?? "Alle";
        var year = (YearBox.SelectedItem as ComboBoxItem)?.Tag as int?;
        return _all.Where(a => _tracks.ContainsKey(a.Id) &&
                               (group == "Alle" || Stats.GroupOf(a.TypeKey) == group) &&
                               (year is null || a.Year == year));
    }

    private async void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingFilters || _mapInit is not { IsCompleted: true } || _tracks.Count == 0) return;
        await ShowAsync(fit: true);
    }

    private async Task ShowAsync(bool fit = false)
    {
        int request = ++_request;
        await Task.Yield();
        var list = Filtered().ToList();
        var payload = new
        {
            heat = list.Select(a => new
            {
                id = a.Id.ToString(),
                p = _tracks[a.Id].Select(pt => new[] { Math.Round(pt.Lat, 5), Math.Round(pt.Lon, 5) }),
            }),
            bounds = fit || _firstShow ? CoreBounds(list) : null,
        };
        _firstShow = false;
        var json = await Task.Run(() => JsonSerializer.Serialize(payload));
        if (request != _request) return;

        Map.CoreWebView2.PostWebMessageAsJson(json);
        NearPanel.Visibility = Visibility.Collapsed;
        StatusText.Text = $"{list.Count} Tracks · weit draußen Heatmap, ab Zoomstufe 12 einzelne Linien · " +
                          "reinzoomen und auf eine Linie klicken = Touren dort";
    }

    /// <summary>Bereich, in dem die meisten Touren starten (5 %- bis 95 %-Quantil).</summary>
    private static double[][]? CoreBounds(List<Activity> list)
    {
        if (list.Count == 0) return null;
        var lats = list.Select(a => a.StartLat!.Value).Order().ToList();
        var lons = list.Select(a => a.StartLon!.Value).Order().ToList();
        int lo = list.Count < 20 ? 0 : (int)(list.Count * 0.05);
        int hi = list.Count < 20 ? list.Count - 1 : Math.Min((int)(list.Count * 0.95), list.Count - 1);
        return [[lats[lo], lons[lo]], [lats[hi], lons[hi]]];
    }

    private async void OnShowAllClick(object sender, RoutedEventArgs e)
    {
        if (_mapInit is not { IsCompleted: true }) return;
        if (CoreBounds(Filtered().ToList()) is { } b) await Map.ExecuteScriptAsync($"fitArea({JsonSerializer.Serialize(b)})");
    }

    // ------------------------------------------------------------------ Touren an einem Punkt

    private void ShowNear(List<long> ids, bool tooFar)
    {
        if (tooFar)
        {
            NearPanel.Visibility = Visibility.Collapsed;
            StatusText.Text = "Zum Anzeigen der Touren an einer Stelle bitte weiter hineinzoomen (ab Zoomstufe 11).";
            return;
        }
        var visible = Filtered().Select(a => a.Id).ToHashSet();
        var tours = ids.Where(visible.Contains).Select(id => _byId[id]).OrderByDescending(a => a.Start).ToList();
        if (tours.Count == 0)
        {
            NearPanel.Visibility = Visibility.Collapsed;
            return;
        }
        NearHeader.Text = tours.Count == 1 ? "1 Tour führt hier durch" : $"{tours.Count} Touren führen hier durch";
        NearTable.ItemsSource = tours;
        NearTable.SelectedItem = null;
        NearPanel.Visibility = Visibility.Visible;
    }

    private void OnNearSelected(object sender, SelectionChangedEventArgs e)
    {
        if (NearTable.SelectedItem is not Activity a) return;
        TourDetails.Open(a, Window.GetWindow(this));
        NearTable.SelectedItem = null;   // damit ein erneuter Klick wieder öffnet
    }

    private void OnCloseNear(object sender, RoutedEventArgs e) => NearPanel.Visibility = Visibility.Collapsed;
}
