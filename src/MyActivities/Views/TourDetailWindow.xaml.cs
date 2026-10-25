using System.Windows;
using System.Windows.Input;
using MyActivities.Data;
using MyActivities.Domain;
using Wpf.Ui.Controls;

namespace MyActivities.Views;

/// <summary>Alle Details einer Tour: Kennzahlen, Karte, Fotos, Leistung, Link zu Garmin Connect.</summary>
public partial class TourDetailWindow : FluentWindow
{
    private readonly Task _mapInit;
    private int _request;
    private Activity? _activity;
    private string? _mapScript;
    private List<TourPhoto> _photos = [];
    private List<KeyValuePair<string, string>> _detailLines = [];

    public TourDetailWindow()
    {
        InitializeComponent();
        WindowBounds.Apply(this, "tourdetail", widthFraction: 0.28, heightFraction: 0.9);
        _mapInit = InitMapAsync();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    public Activity? CurrentActivity => _activity;

    private async Task InitMapAsync()
    {
        // WebView2 braucht ein sichtbares Fenster
        if (!IsLoaded)
        {
            var loaded = new TaskCompletionSource();
            Loaded += (_, _) => loaded.TrySetResult();
            await loaded.Task;
        }
        await MapHost.InitAsync(Map);
        Map.WebMessageReceived += (_, e) =>
        {
            if (MapHost.PhotoIndex(e) is int index) OpenPhoto(index);
        };
    }

    /// <summary>Tour anzeigen (auch für ein bereits offenes Fenster).</summary>
    public async Task ShowAsync(Activity a)
    {
        _activity = a;
        Title = $"{a.Name} · {a.Start:dd.MM.yyyy}";
        AppTitleBar.Title = Title;
        ShowDetails(a);
        await ShowMapAndPhotosAsync(a);
    }

    // ------------------------------------------------------------------ Kennzahlen & Details

    private void ShowDetails(Activity a)
    {
        DetailName.Text = a.Name;
        DetailSub.Text = $"{a.TypeLabel} · {a.Start:dddd, d. MMMM yyyy, HH:mm}";
        DetailLocation.Text = a.LocationName ?? "";
        DetailLocation.Visibility = string.IsNullOrWhiteSpace(a.LocationName) ? Visibility.Collapsed : Visibility.Visible;

        TileKm.Text = $"{a.Km:N1} km";
        TileHm.Text = $"{a.ElevationGain:N0} m";
        TileTime.Text = a.DurationText;
        Tiles.Visibility = Visibility.Visible;

        var lines = new List<KeyValuePair<string, string>>
        {
            new("Dauer", Format.Duration(a.DurationS, withSeconds: true)),
        };
        if (a.AvgKmh is double avg)
        {
            lines.Add(new("Ø Geschwindigkeit", $"{avg:N1} km/h"));
            if (a.TypeKey == "running" && a.AvgSpeed is double mps) lines.Add(new("Ø Pace", $"{Format.Pace(1000.0 / mps)} /km"));
        }
        if (a.MaxKmh is double max) lines.Add(new("Max. Geschwindigkeit", $"{max:N1} km/h"));
        if (a.MinElev is double minElev) lines.Add(new("Tiefster Punkt", $"{minElev:N0} m"));
        if (a.MaxElev is double maxElev) lines.Add(new("Höchster Punkt", $"{maxElev:N0} m"));
        if (a.AvgHr is double hr) lines.Add(new("Ø Puls", $"{hr:N0} bpm"));
        if (a.Calories is double kcal) lines.Add(new("Kalorien", $"{kcal:N0} kcal"));
        lines.Add(new("Aktivitäts-ID", a.Id.ToString()));
        _detailLines = lines;
        DetailList.ItemsSource = lines;

        GarminButton.Visibility = Visibility.Visible;
    }

    /// <summary>Radfahren: geschätzte beste Leistung am Berg (5/10/20 min) unter den Details.</summary>
    private async Task ShowPowerAsync(Activity a, TrackData track, int request)
    {
        if (!Stats.CyclingTypes.Contains(a.TypeKey) || Stats.IsIndoor(a.TypeKey, a.Name) || track.Timeline.Count < 2) return;

        var powers = await Task.Run(() => Power.BestPowers(track.Timeline, a.TypeKey));
        AppServices.Db.UpdatePower(a.Id, powers);   // gleich für die Tabelle merken
        a.SetPower(powers);
        if (request != _request) return;

        var lines = new List<KeyValuePair<string, string>>(_detailLines);
        var found = Power.WindowsS.Zip(powers).Where(x => x.Second is not null).ToList();
        lines.Add(new("Leistung am Berg (geschätzt)", found.Count == 0 ? "kein Anstieg" : ""));
        foreach (var (window, watt) in found) lines.Add(new($"   Beste {window / 60} min", $"{watt:N0} W"));
        _detailLines = lines;
        DetailList.ItemsSource = lines;
    }

    private void OnGarminClick(object sender, RoutedEventArgs e)
    {
        if (_activity is null) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            $"https://connect.garmin.com/modern/activity/{_activity.Id}") { UseShellExecute = true });
    }

    // ------------------------------------------------------------------ Karte & Fotos

    private void OnEnlargeClick(object sender, RoutedEventArgs e)
    {
        if (_mapScript is null || _activity is null) return;
        new MapWindow($"{_activity.Name} · {_activity.Start:dd.MM.yyyy}", _mapScript, _photos) { Owner = this }.Show();
    }

    private async Task ShowMapAndPhotosAsync(Activity a)
    {
        int request = ++_request;
        TrackStatus.Text = "Lade Track und Fotos …";
        _mapScript = null;
        EnlargeButton.IsEnabled = false;
        _photos = [];
        PhotoList.ItemsSource = null;
        PhotoPanel.Visibility = Visibility.Collapsed;

        var result = await TourMap.BuildAsync(a);
        await _mapInit;
        if (request != _request) return;   // inzwischen andere Tour gewählt

        await Map.ExecuteScriptAsync(result.Script);
        _mapScript = result.Script;
        EnlargeButton.IsEnabled = result.HasContent;
        TrackStatus.Text = result.Status;

        _photos = result.Photos;
        var stripTask = ShowPhotoStripAsync(result.Photos, request);
        await ShowPowerAsync(a, result.Track, request);
        await stripTask;
    }

    private async Task ShowPhotoStripAsync(List<TourPhoto> photos, int request)
    {
        PhotoPanel.Visibility = Visibility.Visible;
        PhotoHeader.Text = photos.Count == 0 ? "Keine Fotos zu dieser Tour" : $"Fotos ({photos.Count})";
        PhotoList.Visibility = photos.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        PhotoList.ItemsSource = photos;
        foreach (var photo in photos)
        {
            if (request != _request) return;
            photo.Thumb ??= await Task.Run(() => PhotoService.LoadImage(photo.Path, 240, photo.Orientation));
        }
    }

    private void OnPhotoClick(object sender, MouseButtonEventArgs e)
    {
        if (System.Windows.Controls.ItemsControl.ContainerFromElement(PhotoList, (DependencyObject)e.OriginalSource)
            is System.Windows.Controls.ListBoxItem item)
        {
            OpenPhoto(PhotoList.ItemContainerGenerator.IndexFromContainer(item));
        }
    }

    private void OnPhotoKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && PhotoList.SelectedIndex >= 0) OpenPhoto(PhotoList.SelectedIndex);
    }

    private void OpenPhoto(int index)
    {
        if (index < 0 || index >= _photos.Count) return;
        new PhotoWindow(_photos, index, _activity?.Name ?? "Foto") { Owner = this }.Show();
    }
}
