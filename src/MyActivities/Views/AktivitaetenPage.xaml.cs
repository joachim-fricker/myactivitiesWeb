using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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

    private List<Activity> _all = [];
    private ICollectionView? _view;
    private bool _updatingFilters;
    private int _dbCount = -1;
    private Task? _mapInit;
    private int _trackRequest;
    private Activity? _selected;
    private string? _mapScript;   // zuletzt angezeigter Inhalt der Karte (für die große Karte)
    private List<TourPhoto> _photos = [];
    private TourDetailWindow? _detailWindow;
    private string _powerProgress = "";

    public AktivitaetenPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            // Nur neu laden, wenn neue Aktivitäten dazugekommen sind (Seite bleibt im Speicher)
            int count = AppServices.Db.Count();
            if (count != _dbCount)
            {
                _dbCount = count;
                LoadData();
            }
            _mapInit ??= InitMapAsync();
            _ = BackfillPowerAsync();
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

    private bool Matches(object item)
    {
        var a = (Activity)item;

        // Indoor-Fahrten (Rouvy, Rollentrainer) nur auf Wunsch
        if (IndoorSwitch.IsChecked != true && Stats.IsIndoor(a.TypeKey, a.Name)) return false;

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
        SummaryText.Text = (visible.Count == 0
            ? (_all.Count == 0 ? "Noch keine Daten – bitte unter Einstellungen synchronisieren."
                               : "Keine Aktivitäten für diese Auswahl.")
            : $"{visible.Count} Aktivitäten · {visible.Sum(a => a.Km):N0} km · {visible.Sum(a => a.ElevationGain):N0} Hm")
            + _powerProgress;
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

    private void OnIndoorClick(object sender, RoutedEventArgs e) => ApplyFilter();

    // ------------------------------------------------------------------ Watt-Werte im Hintergrund

    /// <summary>Leistung am Berg für alle Radtouren nachberechnen (lädt dafür die GPX-Dateien).</summary>
    private async Task BackfillPowerAsync()
    {
        var byId = _all.ToDictionary(a => a.Id);
        var progress = new Progress<(int Done, int Total)>(p =>
        {
            _powerProgress = p.Done < p.Total ? $" · berechne Watt-Werte: {p.Done} / {p.Total} …" : "";
            UpdateSummary();
        });
        await AppServices.Power.BackfillAsync(progress, (id, watts) =>
            Dispatcher.InvokeAsync(() =>
            {
                if (byId.TryGetValue(id, out var a)) a.SetPower(watts);
            }));
        _powerProgress = "";
        UpdateSummary();
    }

    // ------------------------------------------------------------------ Auswahl, Karte, Details

    private async void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Table.SelectedItem is not Activity a) return;
        _selected = a;
        DetailsButton.IsEnabled = true;
        if (_detailWindow is not null) _ = _detailWindow.ShowAsync(a);   // offenes Detailfenster folgt der Auswahl
        await ShowMapAsync(a);
    }

    private void OnTableDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // nur Doppelklick auf eine Zeile, nicht auf den Spaltenkopf
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(Table, d) is DataGridRow)
            OpenDetails();
    }

    private void OnTableKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _selected is null) return;
        OpenDetails();
        e.Handled = true;   // sonst springt die Auswahl eine Zeile weiter
    }

    private void OnDetailsClick(object sender, RoutedEventArgs e) => OpenDetails();

    private void OpenDetails()
    {
        if (_selected is null) return;
        if (_detailWindow is null)
        {
            _detailWindow = new TourDetailWindow { Owner = Window.GetWindow(this) };
            _detailWindow.Closed += (_, _) => _detailWindow = null;
            _detailWindow.Show();
        }
        else
        {
            _detailWindow.Activate();
        }
        if (_detailWindow.CurrentActivity != _selected) _ = _detailWindow.ShowAsync(_selected);
    }

    private async Task InitMapAsync()
    {
        await MapHost.InitAsync(Map);
        Map.WebMessageReceived += (_, e) =>
        {
            if (MapHost.PhotoIndex(e) is int index && index >= 0 && index < _photos.Count)
                new PhotoWindow(_photos, index, _selected?.Name ?? "Foto") { Owner = Window.GetWindow(this) }.Show();
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

    private async Task ShowMapAsync(Activity a)
    {
        int request = ++_trackRequest;
        TrackStatus.Text = $"{a.Name} · {a.Start:dd.MM.yyyy} · lade Track und Fotos …";
        _mapScript = null;
        _photos = [];
        EnlargeButton.IsEnabled = false;

        var result = await TourMap.BuildAsync(a);
        if (_mapInit is not null) await _mapInit;
        if (request != _trackRequest) return;   // inzwischen andere Tour gewählt

        await Map.ExecuteScriptAsync(result.Script);
        _mapScript = result.Script;
        _photos = result.Photos;
        EnlargeButton.IsEnabled = result.HasContent;
        var photos = result.Photos.Count switch { 0 => "keine Fotos", 1 => "1 Foto", var n => $"{n} Fotos" };
        TrackStatus.Text = $"{a.Name} · {a.Start:dd.MM.yyyy} · {photos}" +
                           (string.IsNullOrEmpty(result.Status) ? "" : $" · {result.Status}");
    }
}
