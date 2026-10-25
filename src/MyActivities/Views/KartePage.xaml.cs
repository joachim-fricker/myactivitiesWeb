using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MyActivities.Data;
using MyActivities.Domain;

namespace MyActivities.Views;

/// <summary>Weltkarte mit den Startpunkten aller Touren; Klick zeigt Track und Fotos.</summary>
public partial class KartePage : Page
{
    private List<Activity> _all = [];
    private Task? _mapInit;
    private bool _updatingFilters;
    private bool _firstShow = true;
    private int _dbCount = -1;
    private int _request;
    private Activity? _selected;
    private List<TourPhoto> _photos = [];

    /// <summary>Geöffnete Tracks (lila), bleiben auch bei Filterwechsel stehen.</summary>
    private readonly Dictionary<long, List<GeoPoint>> _opened = [];
    private bool _loadingTracks;

    /// <summary>Mehr Tracks auf einmal würden die Karte zäh machen.</summary>
    private const int MaxTracksAtOnce = 300;

    /// <summary>"Tracks im Bereich öffnen" gibt es bei weniger Touren im Ausschnitt ...</summary>
    private const int MaxToursForTracks = 20;

    /// <summary>... oder ab dieser Zoomstufe (ca. 50 km Ausschnitt).</summary>
    private const double MinZoomForTracks = 10;

    private List<TourPhoto> _areaPhotos = [];
    private string _stripTitle = "Fotos im Bereich";
    private int _photoRequest;

    public KartePage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            // Zurück von einer anderen Seite: Ansicht, Tracks und Fotos so lassen, wie sie waren
            int count = AppServices.Db.Count();
            if (count == _dbCount) return;
            _dbCount = count;

            LoadData();
            _mapInit ??= InitMapAsync();
            await _mapInit;
            await ShowStartsAsync();
        };
    }

    private async Task InitMapAsync()
    {
        await MapHost.InitAsync(Map);
        Map.WebMessageReceived += async (_, e) =>
        {
            if (MapHost.ViewInfo(e) is { } view)
            {
                UpdateViewInfo(view.Count, view.Closed, view.Zoom);
            }
            else if (MapHost.DetailsId(e) is long detailsId)
            {
                (Window.GetWindow(this) as MainWindow)?.ShowActivity(detailsId);
            }
            else if (MapHost.SelectedId(e) is long id && _all.FirstOrDefault(a => a.Id == id) is Activity a)
            {
                await ShowTourAsync(a);
            }
            else if (MapHost.PhotoIndex(e) is int index && index >= 0 && index < _photos.Count)
            {
                new PhotoWindow(_photos, index, _selected?.Name ?? "Foto") { Owner = Window.GetWindow(this) }.Show();
            }
        };
    }

    private void LoadData()
    {
        // Indoor-Fahrten haben virtuelle Startpunkte und gehören nicht auf die Karte
        _all = AppServices.Db.AllActivities()
            .Where(a => a.StartLat is not null && a.StartLon is not null && !Stats.IsIndoor(a.TypeKey, a.Name))
            .ToList();

        _updatingFilters = true;
        var selectedYear = (YearBox.SelectedItem as ComboBoxItem)?.Tag;
        YearBox.Items.Clear();
        YearBox.Items.Add(new ComboBoxItem { Content = "Alle Jahre" });
        foreach (var year in _all.Select(a => a.Year).Distinct().OrderDescending())
        {
            YearBox.Items.Add(new ComboBoxItem { Content = year.ToString(), Tag = year });
        }
        YearBox.SelectedItem = YearBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, selectedYear))
                               ?? YearBox.Items[0];
        _updatingFilters = false;
    }

    private IEnumerable<Activity> Filtered()
    {
        var group = (GroupBox.SelectedItem as ComboBoxItem)?.Content as string ?? "Alle";
        var year = (YearBox.SelectedItem as ComboBoxItem)?.Tag as int?;
        return _all.Where(a => (group == "Alle" || Stats.GroupOf(a.TypeKey) == group) &&
                               (year is null || a.Year == year));
    }

    private async void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingFilters || _mapInit is null || !_mapInit.IsCompleted) return;
        await ShowStartsAsync(fit: true);
    }

    private async Task ShowStartsAsync(bool fit = false)
    {
        var colors = Stats.GroupColors.ToDictionary(g => g.Group, g => g.Color);
        var list = Filtered().ToList();
        var points = list.Select(a => new
        {
            id = a.Id,
            lat = a.StartLat!.Value,
            lon = a.StartLon!.Value,
            c = colors[Stats.GroupOf(a.TypeKey)],
            n = a.Name,
            d = a.Start.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture),
            t = a.TypeLabel,
            info = $"{a.Km:N1} km · {a.ElevationGain:N0} Hm · {a.DurationText}",
        });

        // Beim ersten Anzeigen und bei Filterwechsel auf den Kernbereich zoomen
        bool zoom = fit || _firstShow;
        _firstShow = false;
        _request++;
        _selected = null;
        _photos = [];
        if (_opened.Count == 0)
        {
            // keine geöffneten Tracks mehr: Fotostreifen der vorher gewählten Tour ausblenden
            _photoRequest++;
            AreaPhotoPanel.Visibility = Visibility.Collapsed;
        }
        var bounds = zoom ? BoundsJson(list) : "null";
        await Map.ExecuteScriptAsync($"clearTrack(); showStarts({JsonSerializer.Serialize(points)}, {bounds})");
        StatusText.Text = $"{list.Count} Touren · Klick auf einen Punkt zeigt Track und Fotos";
    }

    /// <summary>
    /// Bereich, in dem die meisten Touren liegen (5 %- bis 95 %-Quantil), damit einzelne
    /// Ferientouren weit weg die Ansicht nicht auf ganz Europa aufziehen. "null" = keine Touren.
    /// </summary>
    private static string BoundsJson(List<Activity> list)
    {
        if (list.Count == 0) return "null";
        var lats = list.Select(a => a.StartLat!.Value).Order().ToList();
        var lons = list.Select(a => a.StartLon!.Value).Order().ToList();
        int lo = list.Count < 20 ? 0 : (int)(list.Count * 0.05);
        int hi = list.Count < 20 ? list.Count - 1 : Math.Min((int)(list.Count * 0.95), list.Count - 1);
        return JsonSerializer.Serialize(new[] { new[] { lats[lo], lons[lo] }, new[] { lats[hi], lons[hi] } });
    }

    private async void OnShowAllClick(object sender, RoutedEventArgs e)
    {
        if (_mapInit is not { IsCompleted: true }) return;
        var bounds = BoundsJson(Filtered().ToList());
        if (bounds != "null") await Map.ExecuteScriptAsync($"fitArea({bounds})");
    }

    // ------------------------------------------------------------------ Tracks im Bereich

    private async void OnOpenInViewClick(object sender, RoutedEventArgs e)
    {
        if (_loadingTracks || _mapInit is not { IsCompleted: true }) return;

        var json = await Map.ExecuteScriptAsync("startsInView()");
        var inView = (JsonSerializer.Deserialize<string[]>(json) ?? [])
            .Select(text => long.TryParse(text, out var id) ? id : 0)
            .Where(id => id != 0)
            .ToHashSet();
        if (inView.Count > MaxTracksAtOnce)
        {
            StatusText.Text = $"{inView.Count} Touren im Bereich – bitte näher heranzoomen (höchstens {MaxTracksAtOnce} Tracks gleichzeitig).";
            return;
        }

        // Neuer Bereich: Tracks außerhalb schließen, bereits geladene im Bereich behalten
        foreach (var stale in _opened.Keys.Where(k => !inView.Contains(k)).ToList()) _opened.Remove(stale);
        if (_selected is not null && !inView.Contains(_selected.Id))
        {
            _request++;
            _selected = null;
            _photos = [];
            await Map.ExecuteScriptAsync("clearTrack()");
        }
        var ids = inView.Where(id => !_opened.ContainsKey(id)).ToList();

        _loadingTracks = true;
        OpenInViewButton.IsEnabled = false;
        int done = 0, failed = 0;
        var loaded = new System.Collections.Concurrent.ConcurrentDictionary<long, List<GeoPoint>>();
        try
        {
            await Parallel.ForEachAsync(ids, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (id, ct) =>
            {
                try
                {
                    var track = await AppServices.Tracks.LoadAsync(id, ct);
                    if (track.Points.Count >= 2) loaded[id] = Thin(track.Points, 600);
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref failed);
                }
                int n = Interlocked.Increment(ref done);
                await Dispatcher.InvokeAsync(() => StatusText.Text = $"Lade Tracks: {n} / {ids.Count} …");
            });
        }
        finally
        {
            foreach (var (id, points) in loaded) _opened[id] = points;
            _loadingTracks = false;
        }

        await DrawOpenedAsync();
        StatusText.Text = $"{_opened.Count} Tracks im Bereich geöffnet" +
                          (failed > 0 ? $", {failed} konnten nicht geladen werden" : "") +
                          " · Klick auf einen Track zeigt Details und Fotos";
        await LoadAreaPhotosAsync();
    }

    // ------------------------------------------------------------------ Fotos im Bereich

    /// <summary>Fotos aller geöffneten Touren unten als Streifen anzeigen (neueste Tour zuerst).</summary>
    private async Task LoadAreaPhotosAsync()
    {
        int request = ++_photoRequest;
        var tours = _all.Where(a => _opened.ContainsKey(a.Id)).OrderByDescending(a => a.Start).ToList();
        if (tours.Count == 0)
        {
            _areaPhotos = [];
            AreaPhotoPanel.Visibility = Visibility.Collapsed;
            return;
        }

        AreaPhotoPanel.Visibility = Visibility.Visible;
        AreaPhotoList.ItemsSource = null;
        AreaPhotoHeader.Text = "Suche Fotos im Bereich …";

        var all = new List<TourPhoto>();
        int toursWithPhotos = 0;
        foreach (var tour in tours)
        {
            List<TourPhoto> photos;
            try
            {
                photos = await AppServices.Photos.PhotosForAsync(tour);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                continue;
            }
            if (request != _photoRequest) return;
            if (photos.Count > 0) toursWithPhotos++;
            foreach (var photo in photos) photo.TourName = $"{tour.Name} · {tour.Start:dd.MM.yyyy}";
            all.AddRange(photos);
        }

        var header = all.Count == 0
            ? "Keine Fotos zu den geöffneten Touren"
            : $"Fotos im Bereich ({all.Count}) aus {toursWithPhotos} {(toursWithPhotos == 1 ? "Tour" : "Touren")}";
        await ShowPhotoStripAsync(all, header, "Fotos im Bereich", request);
    }

    /// <summary>Fotostreifen unter der Karte füllen; Vorschaubilder 4 gleichzeitig im Hintergrund.</summary>
    private async Task ShowPhotoStripAsync(List<TourPhoto> photos, string header, string title, int request)
    {
        _areaPhotos = photos;
        _stripTitle = title;
        AreaPhotoPanel.Visibility = Visibility.Visible;
        AreaPhotoHeader.Text = header;
        AreaPhotoList.Visibility = photos.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        AreaPhotoList.ItemsSource = photos;

        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(photos.Select(async photo =>
        {
            await gate.WaitAsync();
            try
            {
                if (request == _photoRequest)
                    photo.Thumb ??= await Task.Run(() => PhotoService.LoadImage(photo.Path, 180, photo.Orientation));
            }
            finally
            {
                gate.Release();
            }
        }));
    }

    private void OnAreaPhotoClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(AreaPhotoList, (DependencyObject)e.OriginalSource) is ListBoxItem item)
        {
            int index = AreaPhotoList.ItemContainerGenerator.IndexFromContainer(item);
            if (index >= 0 && index < _areaPhotos.Count)
                new PhotoWindow(_areaPhotos, index, _stripTitle) { Owner = Window.GetWindow(this) }.Show();
        }
    }

    /// <summary>Anzahl Touren im Ausschnitt anzeigen; Öffnen-Button erst ab genügend Zoom.</summary>
    private void UpdateViewInfo(int count, int closed, double zoom)
    {
        ViewText.Text = count switch
        {
            0 => "Keine Touren im Kartenausschnitt",
            1 => "1 Tour im Kartenausschnitt",
            _ => $"{count} Touren im Kartenausschnitt",
        };

        bool zoomedIn = count < MaxToursForTracks || zoom >= MinZoomForTracks;
        OpenInViewButton.Visibility = zoomedIn && closed > 0 ? Visibility.Visible : Visibility.Collapsed;
        OpenInViewButton.Content = $"{closed} Tracks im Bereich öffnen";
        OpenInViewButton.IsEnabled = !_loadingTracks && count <= MaxTracksAtOnce;
        if (!zoomedIn && count > 0) ViewText.Text += " · zum Öffnen der Tracks näher heranzoomen";
    }

    private async void OnHideTracksClick(object sender, RoutedEventArgs e)
    {
        _opened.Clear();
        _photoRequest++;
        _areaPhotos = [];
        AreaPhotoPanel.Visibility = Visibility.Collapsed;
        _request++;
        _selected = null;
        _photos = [];
        await Map.ExecuteScriptAsync("clearTrack()");
        await DrawOpenedAsync();
        StatusText.Text = $"{Filtered().Count()} Touren · Klick auf einen Punkt zeigt Track und Fotos";
    }

    private async Task DrawOpenedAsync()
    {
        var list = _opened.Select(kv => new
        {
            id = kv.Key,
            p = kv.Value.Select(pt => new[] { Math.Round(pt.Lat, 5), Math.Round(pt.Lon, 5) }),
        });
        await Map.ExecuteScriptAsync($"showTracks({JsonSerializer.Serialize(list)})");
        HideTracksButton.Content = $"Tracks ausblenden ({_opened.Count})";
        HideTracksButton.Visibility = _opened.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Für die Übersicht genügen wenige Punkte pro Track.</summary>
    private static List<GeoPoint> Thin(List<GeoPoint> points, int max)
    {
        if (points.Count <= max) return points;
        int step = (int)Math.Ceiling(points.Count / (double)max);
        var result = new List<GeoPoint>(max + 1);
        for (int i = 0; i < points.Count; i += step) result.Add(points[i]);
        result.Add(points[^1]);
        return result;
    }

    private async Task ShowTourAsync(Activity a)
    {
        int request = ++_request;
        _selected = a;
        StatusText.Text = $"{a.Name} · lade Track und Fotos …";

        var result = await TourMap.BuildAsync(a);
        if (request != _request) return;

        _photos = result.Photos;
        await Map.ExecuteScriptAsync(result.Script);
        var photos = result.Photos.Count switch
        {
            0 => "keine Fotos",
            1 => "1 Foto",
            var n => $"{n} Fotos",
        };
        StatusText.Text = $"{a.Name} · {a.Start:dd.MM.yyyy} · {photos}" +
                          (string.IsNullOrEmpty(result.Status) ? "" : $" · {result.Status}");

        var header = result.Photos.Count == 0
            ? $"Keine Fotos zu {a.Name} · {a.Start:dd.MM.yyyy}"
            : $"Fotos – {a.Name} · {a.Start:dd.MM.yyyy} ({result.Photos.Count})";
        await ShowPhotoStripAsync(result.Photos, header, a.Name, ++_photoRequest);
    }
}
