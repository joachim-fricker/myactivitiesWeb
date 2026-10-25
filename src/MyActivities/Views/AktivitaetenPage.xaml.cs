using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Xml;
using MyActivities.Data;
using MyActivities.Domain;

namespace MyActivities.Views;

public partial class AktivitaetenPage : Page
{
    /// <summary>Schnellwahl; bei Radfahren und Wandern gibt es zusätzlich die Unterwahl nach typeKey.</summary>
    private static readonly (string[]? Types, bool HasSubFilter)[] QuickFilters =
    [
        (null, false),
        (Stats.CyclingTypes, true),
        (Stats.HikingTypes, true),
        (Stats.SkiTypes, false),
    ];

    /// <summary>Beim nächsten Anzeigen auszuwählende Tour (z. B. "Details anzeigen" auf der Karte).</summary>
    public static long? PendingSelection { get; set; }

    private List<Activity> _all = [];
    private ICollectionView? _view;
    private bool _updatingFilters;
    private Task? _mapInit;
    private int _trackRequest;
    private Activity? _selected;
    private string? _mapScript;   // zuletzt angezeigter Inhalt der Karte (für die große Karte)
    private List<TourPhoto> _photos = [];
    private List<KeyValuePair<string, string>> _detailLines = [];

    public AktivitaetenPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            LoadData();
            _mapInit ??= InitMapAsync();
            SelectPending();
        };
    }

    // ------------------------------------------------------------------ Daten & Filter

    private void LoadData()
    {
        _all = AppServices.Db.AllActivities();
        _view = CollectionViewSource.GetDefaultView(_all);
        _view.Filter = Matches;
        _view.SortDescriptions.Clear();
        _view.SortDescriptions.Add(new SortDescription(nameof(Activity.Start), ListSortDirection.Descending));
        Table.ItemsSource = _view;
        DateColumn.SortDirection = ListSortDirection.Descending;

        _updatingFilters = true;
        YearBox.Items.Clear();
        YearBox.Items.Add(new ComboBoxItem { Content = "Alle Jahre" });
        foreach (var year in _all.Select(a => a.Year).Distinct().OrderDescending())
        {
            YearBox.Items.Add(new ComboBoxItem { Content = year.ToString(), Tag = year });
        }
        YearBox.SelectedIndex = 0;
        UpdateSubBox();
        _updatingFilters = false;

        UpdateSummary();
    }

    private void SelectPending()
    {
        BackToMapButton.Visibility = PendingSelection is null ? Visibility.Collapsed : Visibility.Visible;
        if (PendingSelection is not long id) return;
        PendingSelection = null;
        if (_all.FirstOrDefault(a => a.Id == id) is not Activity a) return;
        Table.SelectedItem = a;   // löst Details, Karte und Fotos aus
        Table.ScrollIntoView(a);
        Table.Focus();
    }

    private void OnBackToMapClick(object sender, RoutedEventArgs e)
    {
        BackToMapButton.Visibility = Visibility.Collapsed;
        (Window.GetWindow(this) as MainWindow)?.ShowMap();
    }

    private bool Matches(object item)
    {
        var a = (Activity)item;

        var types = QuickFilters[Math.Max(QuickBox.SelectedIndex, 0)].Types;
        if (types is not null && !types.Contains(a.TypeKey)) return false;

        if (SubBox.SelectedItem is ComboBoxItem { Tag: string subType } && a.TypeKey != subType) return false;

        if (YearBox.SelectedItem is ComboBoxItem { Tag: int year } && a.Year != year) return false;

        var query = SearchBox.Text?.Trim();
        if (!string.IsNullOrEmpty(query) &&
            !a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
            !(a.LocationName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return false;
        }
        return true;
    }

    private void UpdateSubBox()
    {
        var (types, hasSub) = QuickFilters[Math.Max(QuickBox.SelectedIndex, 0)];
        SubBox.Items.Clear();
        if (hasSub && types is not null)
        {
            SubBox.Items.Add(new ComboBoxItem { Content = "Alle Typen" });
            foreach (var t in types) SubBox.Items.Add(new ComboBoxItem { Content = Stats.TypeLabel(t), Tag = t });
            SubBox.SelectedIndex = 0;
            SubBox.Visibility = Visibility.Visible;
        }
        else
        {
            SubBox.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyFilter()
    {
        if (_view is null || _updatingFilters) return;
        _view.Refresh();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        if (_view is null) return;
        var visible = _view.Cast<Activity>().ToList();
        SummaryText.Text = visible.Count == 0
            ? (_all.Count == 0 ? "Noch keine Daten – bitte unter Einstellungen synchronisieren."
                               : "Keine Aktivitäten für diese Auswahl.")
            : $"{visible.Count} Aktivitäten · {visible.Sum(a => a.Km):N0} km · {visible.Sum(a => a.ElevationGain):N0} Hm";
    }

    private void OnQuickChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_view is null) return;
        _updatingFilters = true;
        UpdateSubBox();
        _updatingFilters = false;
        ApplyFilter();
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    // ------------------------------------------------------------------ Details

    private async void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Table.SelectedItem is not Activity a) return;
        _selected = a;
        ShowDetails(a);
        await ShowMapAndPhotosAsync(a);
    }

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
        if (Stats.ShowsSpeed(a.TypeKey))
        {
            if (a.AvgSpeed is double avg && avg > 0)
            {
                lines.Add(new("Ø Geschwindigkeit", $"{avg * 3.6:N1} km/h"));
                if (a.TypeKey == "running") lines.Add(new("Ø Pace", $"{Format.Pace(1000.0 / avg)} /km"));
            }
            if (Stats.TrustedMaxSpeed(a.TypeKey, a.MaxSpeed, a.DeviceId) is double max)
            {
                lines.Add(new("Max. Geschwindigkeit", $"{max * 3.6:N1} km/h"));
            }
        }
        if (a.MinElev is double minElev) lines.Add(new("Tiefster Punkt", $"{minElev:N0} m"));
        if (a.MaxElev is double maxElev) lines.Add(new("Höchster Punkt", $"{maxElev:N0} m"));
        if (a.AvgHr is double hr) lines.Add(new("Ø Puls", $"{hr:N0} bpm"));
        if (a.Calories is double kcal) lines.Add(new("Kalorien", $"{kcal:N0} kcal"));
        lines.Add(new("Aktivitäts-ID", a.Id.ToString()));
        _detailLines = lines;
        DetailList.ItemsSource = lines;

        GarminButton.Visibility = Visibility.Visible;
    }

    private void OnGarminClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"https://connect.garmin.com/modern/activity/{_selected.Id}")
        {
            UseShellExecute = true,
        });
    }

    // ------------------------------------------------------------------ Karte & Fotos

    private async Task InitMapAsync()
    {
        await MapHost.InitAsync(Map);
        Map.WebMessageReceived += (_, e) =>
        {
            if (MapHost.PhotoIndex(e) is int index) OpenPhoto(index);
        };
    }

    private void OnEnlargeClick(object sender, RoutedEventArgs e)
    {
        if (_mapScript is null || _selected is null) return;
        new MapWindow($"{_selected.Name} · {_selected.Start:dd.MM.yyyy}", _mapScript, _photos)
        {
            Owner = Window.GetWindow(this),
        }.Show();
    }

    private async Task ShowMapAndPhotosAsync(Activity a)
    {
        int request = ++_trackRequest;
        TrackStatus.Text = "Lade Track und Fotos …";
        _mapScript = null;
        EnlargeButton.IsEnabled = false;
        _photos = [];
        PhotoList.ItemsSource = null;
        PhotoPanel.Visibility = Visibility.Collapsed;

        var result = await TourMap.BuildAsync(a);
        if (_mapInit is not null) await _mapInit;
        if (request != _trackRequest) return;   // inzwischen andere Aktivität gewählt

        await Map.ExecuteScriptAsync(result.Script);
        _mapScript = result.Script;
        EnlargeButton.IsEnabled = result.HasContent;
        TrackStatus.Text = result.Status;

        _photos = result.Photos;
        var stripTask = ShowPhotoStripAsync(result.Photos, request);
        await ShowPowerAsync(a, result.Track, request);
        await stripTask;
    }

    /// <summary>Radfahren: geschätzte beste Leistung am Berg (5/10/20 min) aus dem GPX unter den Details.</summary>
    private async Task ShowPowerAsync(Activity a, TrackData track, int request)
    {
        if (!Stats.CyclingTypes.Contains(a.TypeKey) || Stats.IsIndoor(a.TypeKey, a.Name) || track.Timeline.Count < 2) return;

        var powers = await Task.Run(() => Power.BestPowers(track.Timeline, a.TypeKey));
        if (request != _trackRequest) return;

        var lines = new List<KeyValuePair<string, string>>(_detailLines);
        var found = Power.WindowsS.Zip(powers).Where(x => x.Second is not null).ToList();
        lines.Add(new("Leistung am Berg (geschätzt)", found.Count == 0 ? "kein Anstieg" : ""));
        foreach (var (window, watt) in found)
        {
            lines.Add(new($"   Beste {window / 60} min", $"{watt:N0} W"));
        }
        _detailLines = lines;
        DetailList.ItemsSource = lines;
    }

    /// <summary>Vorschaustreifen füllen; Vorschaubilder werden nacheinander im Hintergrund geladen.</summary>
    private async Task ShowPhotoStripAsync(List<TourPhoto> photos, int request)
    {
        PhotoPanel.Visibility = Visibility.Visible;
        PhotoHeader.Text = photos.Count == 0 ? "Keine Fotos zu dieser Tour" : $"Fotos ({photos.Count})";
        PhotoList.Visibility = photos.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        PhotoList.ItemsSource = photos;

        foreach (var photo in photos)
        {
            if (request != _trackRequest) return;
            photo.Thumb ??= await Task.Run(() => PhotoService.LoadImage(photo.Path, 240, photo.Orientation));
        }
    }

    private void OnPhotoClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Angeklicktes Vorschaubild bestimmen (nicht die Scrollleiste)
        if (ItemsControl.ContainerFromElement(PhotoList, (DependencyObject)e.OriginalSource) is ListBoxItem item)
        {
            OpenPhoto(PhotoList.ItemContainerGenerator.IndexFromContainer(item));
        }
    }

    private void OnPhotoKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && PhotoList.SelectedIndex >= 0) OpenPhoto(PhotoList.SelectedIndex);
    }

    private void OpenPhoto(int index)
    {
        if (index < 0 || index >= _photos.Count) return;
        new PhotoWindow(_photos, index, _selected?.Name ?? "Foto")
        {
            Owner = Window.GetWindow(this),
        }.Show();
    }
}
