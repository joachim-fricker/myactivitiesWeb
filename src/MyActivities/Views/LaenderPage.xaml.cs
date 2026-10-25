using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MyActivities.Data;

namespace MyActivities.Views;

/// <summary>Weltkarte: alle Länder, in denen Fotos (mit GPS) aufgenommen wurden, sind eingefärbt.</summary>
public partial class LaenderPage : Page
{
    /// <summary>Zeile der Länderliste.</summary>
    public sealed record CountryRow(string Code, string Name, int Count, DateTime? First, DateTime? Last)
    {
        public string Period => Count == 0 ? "manuell eingetragen" : $"{First:MM.yyyy} – {Last:MM.yyyy}";
    }

    private Task? _mapInit;
    private bool _scanned;
    private List<CountryRow> _rows = [];
    private Dictionary<string, string> _names = [];
    private List<TourPhoto> _photos = [];          // alle Fotos des Landes (neueste zuerst)
    private List<TourPhoto> _visiblePhotos = [];   // davon im Kartenausschnitt (Fotostreifen, Bildbetrachter)
    private string _headerBase = "";
    private string? _selected;
    private bool _selectingFromMap;

    public LaenderPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            _mapInit ??= InitMapAsync();
            await _mapInit;
            if (_scanned) return;   // zurück von einer anderen Seite: alles so lassen
            _scanned = true;

            await RefreshAsync();   // sofort mit dem bisherigen Stand
            await ScanAsync();      // dann neue Fotos einlesen
        };
    }

    private async Task InitMapAsync()
    {
        await MapHost.InitAsync(Map);
        Map.WebMessageReceived += (_, e) =>
        {
            if (MapHost.CountryCode(e) is string code) SelectFromMap(code);
            else if (MapHost.ViewPhotos(e) is { } inView) ShowVisiblePhotos(inView);
            else if (MapHost.PhotoIndex(e) is int index && index >= 0 && index < _photos.Count) OpenPhoto(_photos[index]);
            else if (MapHost.LogText(e) is string log) CountryHeader.Text += $" · {log}";   // nur Fehlermeldungen
        };
    }

    private async Task ScanAsync()
    {
        ScanProgress.Visibility = Visibility.Visible;
        ScanProgress.IsIndeterminate = true;
        ScanText.Text = "Suche Fotos …";
        var progress = new Progress<(int Done, int Total)>(p =>
        {
            if (p.Total == 0) return;
            ScanProgress.IsIndeterminate = false;
            ScanProgress.Maximum = p.Total;
            ScanProgress.Value = p.Done;
            ScanText.Text = $"Lese Aufnahmeorte aus den Fotos: {p.Done:N0} / {p.Total:N0} … " +
                            "(nur beim ersten Mal alle, danach nur neue Fotos)";
        });
        int read = await AppServices.PhotoCountries.ScanAsync(progress);
        ScanProgress.Visibility = Visibility.Collapsed;
        ScanText.Text = read == 0
            ? "Klick auf ein Land oder in die Liste zeigt die Fotos von dort."
            : $"{read:N0} Fotos neu eingelesen · Klick auf ein Land oder in die Liste zeigt die Fotos von dort.";
        if (read > 0) await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var service = AppServices.PhotoCountries;
        var (stats, names, gps) = await Task.Run(() => (service.VisitedCountries(), service.Lookup.Names,
                                                        AppServices.Db.PhotosWithGpsCount()));
        _names = names;
        _rows = stats.Select(s => new CountryRow(s.Country, names.GetValueOrDefault(s.Country, s.Country),
                                                 s.Count, s.First, s.Last)).ToList();
        CountryTable.ItemsSource = _rows;
        int fromPhotos = _rows.Count(r => r.Count > 0);
        SummaryText.Text = $"{_rows.Count} von {names.Count} Ländern besucht ({100.0 * _rows.Count / Math.Max(1, names.Count):N0} %) · " +
                           $"{gps:N0} Fotos mit GPS";
        ListHeader.Text = $"{_rows.Count} besuchte Länder ({fromPhotos} mit Fotos, {_rows.Count - fromPhotos} manuell)";
        await Map.ExecuteScriptAsync($"showCountries({JsonSerializer.Serialize(_rows.Select(r => r.Code))})");
        if (_selected is not null) await Map.ExecuteScriptAsync($"selectCountry({JsonSerializer.Serialize(_selected)}, false)");
    }

    // ------------------------------------------------------------------ Auswahl

    private void SelectFromMap(string code)
    {
        var row = _rows.FirstOrDefault(r => r.Code == code);
        _selectingFromMap = true;
        CountryTable.SelectedItem = row;   // nicht besuchte Länder sind nicht in der Liste
        if (row is not null) CountryTable.ScrollIntoView(row);
        _selectingFromMap = false;
        _ = ShowCountryAsync(code, zoom: false);
    }

    private void OnCountrySelected(object sender, SelectionChangedEventArgs e)
    {
        if (_selectingFromMap || CountryTable.SelectedItem is not CountryRow row) return;
        _ = ShowCountryAsync(row.Code, zoom: true);
    }

    private async Task ShowCountryAsync(string code, bool zoom)
    {
        try
        {
            await ShowCountryCoreAsync(code, zoom);
        }
        catch (Exception ex)
        {
            CountryPanel.Visibility = Visibility.Visible;
            CountryHeader.Text = $"Fehler: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private async Task ShowCountryCoreAsync(string code, bool zoom)
    {
        _selected = code;
        await Map.ExecuteScriptAsync($"selectCountry({JsonSerializer.Serialize(code)}, {(zoom ? "true" : "false")})");

        var row = _rows.FirstOrDefault(r => r.Code == code);
        var name = row?.Name ?? _names.GetValueOrDefault(code, code);
        List<TourPhoto> photos = row is { Count: > 0 } ? await Task.Run(() => AppServices.PhotoCountries.PhotosIn(code)) : [];
        if (_selected != code) return;

        _photos = photos;
        _visiblePhotos = photos;
        CountryPanel.Visibility = Visibility.Visible;
        CountryHeader.Text = row switch
        {
            null => $"{name} – nicht besucht",
            { Count: 0 } => $"{name} – manuell eingetragen, keine Fotos mit GPS",
            _ => $"{name} – {row.Count:N0} Fotos · erstes {row.First:dd.MM.yyyy} · letztes {row.Last:dd.MM.yyyy}",
        };
        _headerBase = CountryHeader.Text;
        PhotoList.ItemsSource = photos;
        PhotoList.Visibility = photos.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (photos.Count > 0) PhotoList.ScrollIntoView(photos[0]);

        // Aufnahmeorte auf der Karte (wie bei den Tracks), Klick öffnet das Foto
        var markers = photos
            .Select((photo, index) => (photo, index))
            .Where(x => x.photo.Gps is not null)
            .Select(x => new
            {
                i = x.index,
                lat = Math.Round(x.photo.Gps!.Value.Lat, 6),
                lon = Math.Round(x.photo.Gps!.Value.Lon, 6),
                url = AppServices.Photos.MapUrl(x.photo),
            });
        var json = await Task.Run(() => JsonSerializer.Serialize(new { countryPhotos = markers }));
        if (_selected == code) Map.CoreWebView2.PostWebMessageAsJson(json);
    }

    // ------------------------------------------------------------------ Suche

    /// <summary>Während der Eingabe zum ersten passenden Land springen (Anfang des Namens zuerst).</summary>
    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        var q = SearchBox.Text.Trim();
        if (q.Length == 0) return;

        var row = _rows.Where(r => r.Name.StartsWith(q, StringComparison.CurrentCultureIgnoreCase))
                       .OrderBy(r => r.Name, StringComparer.CurrentCulture).FirstOrDefault()
                  ?? _rows.FirstOrDefault(r => r.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase));
        if (row is not null)
        {
            CountryTable.SelectedItem = row;   // zoomt die Karte und zeigt die Fotos
            CountryTable.ScrollIntoView(row);
            return;
        }

        // nicht besucht: trotzdem auf der Karte zeigen
        var code = _names.Where(kv => kv.Value.StartsWith(q, StringComparison.CurrentCultureIgnoreCase))
                         .OrderBy(kv => kv.Value, StringComparer.CurrentCulture)
                         .Select(kv => kv.Key).FirstOrDefault();
        if (code is not null)
        {
            CountryTable.SelectedItem = null;
            _ = ShowCountryAsync(code, zoom: true);
        }
    }

    // ------------------------------------------------------------------ Fotos

    /// <summary>Vorschaubild erst laden, wenn es im Streifen sichtbar wird (Länder können tausende Fotos haben).</summary>
    private async void OnThumbLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TourPhoto photo } || photo.Thumb is not null) return;
        photo.Thumb = await Task.Run(() => PhotoService.LoadThumb(photo, 180));
    }

    /// <summary>Nicht mehr sichtbar: Speicher wieder freigeben.</summary>
    private void OnThumbUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TourPhoto photo }) photo.Thumb = null;
    }

    private void OnPhotoClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(PhotoList, (DependencyObject)e.OriginalSource) is ListBoxItem item)
        {
            int index = PhotoList.ItemContainerGenerator.IndexFromContainer(item);
            if (index >= 0 && index < _visiblePhotos.Count) OpenPhoto(_visiblePhotos[index]);
        }
    }

    /// <summary>Bildbetrachter mit den Fotos im Kartenausschnitt öffnen (bzw. allen, falls das Foto außerhalb liegt).</summary>
    private void OpenPhoto(TourPhoto photo)
    {
        var list = _visiblePhotos.Contains(photo) ? _visiblePhotos : _photos;
        var title = _rows.FirstOrDefault(r => r.Code == _selected)?.Name ?? "Fotos";
        new PhotoWindow(list, list.IndexOf(photo), title) { Owner = Window.GetWindow(this) }.Show();
    }

    /// <summary>Fotostreifen auf die Fotos im Kartenausschnitt beschränken.</summary>
    private void ShowVisiblePhotos(List<int> indices)
    {
        var visible = indices.Where(i => i >= 0 && i < _photos.Count).Select(i => _photos[i]).ToList();
        if (visible.SequenceEqual(_visiblePhotos)) return;
        _visiblePhotos = visible;
        PhotoList.ItemsSource = visible;
        PhotoList.Visibility = visible.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        CountryHeader.Text = visible.Count < _photos.Count
            ? $"{_headerBase} · {visible.Count:N0} davon im Kartenausschnitt"
            : _headerBase;
    }
}
