using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MyActivities.Data;
using MyActivities.Domain;

namespace MyActivities.Views;

/// <summary>
/// Kalender-Heatmap: ein Jahr (bei Skitouren eine Saison Sep–Aug) als Monatskalender,
/// Färbung nach km bzw. Hm pro Tag. Klick auf einen Tag zeigt dessen Touren.
/// </summary>
public partial class KalenderPage : Page
{
    private static readonly string[] Weekdays = ["Mo", "Di", "Mi", "Do", "Fr", "Sa", "So"];

    /// <summary>Deckkraft je Stufe (0 = wenig ... 3 = viel).</summary>
    private static readonly byte[] LevelAlpha = [77, 128, 191, 255];

    /// <summary>Gruppen wie in der Tabelle; Farben wie in der Android-App.</summary>
    private static readonly (string[]? Types, Color Color)[] Groups =
    [
        (null, Color.FromRgb(0x43, 0xA0, 0x47)),
        (Stats.CyclingTypes, Color.FromRgb(0x1E, 0x88, 0xE5)),
        (Stats.HikingTypes, Color.FromRgb(0x6D, 0x4C, 0x41)),
        (Stats.SkiTypes, Color.FromRgb(0x00, 0xAC, 0xC1)),
    ];

    private const double CellSize = 26;

    private List<Activity> _all = [];
    private int _dbCount = -1;
    private int? _period;            // gewähltes Jahr bzw. Startjahr der Saison; null = aktuell
    private DateTime? _selectedDay;

    public KalenderPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            int count = AppServices.Db.Count();
            if (count != _dbCount)
            {
                _dbCount = count;
                _all = AppServices.Db.AllActivities();
            }
            Render();
        };
    }

    private int GroupIndex => Math.Max(GroupBox.SelectedIndex, 0);
    private bool IsSki => GroupIndex == 3;
    private int StartMonth => IsSki ? Stats.SkiSeasonStartMonth : 1;
    private bool ByKm => MetricBox.SelectedIndex != 1;

    private int PeriodOf(DateTime d) => d.Month >= StartMonth ? d.Year : d.Year - 1;

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _selectedDay = null;
        Render();
    }

    private void OnPrev(object sender, RoutedEventArgs e) => ChangePeriod(-1);

    private void OnNext(object sender, RoutedEventArgs e) => ChangePeriod(+1);

    private void ChangePeriod(int delta)
    {
        _period = (_period ?? PeriodOf(DateTime.Today)) + delta;
        _selectedDay = null;
        Render();
    }

    private void Render()
    {
        var types = Groups[GroupIndex].Types;
        var byDay = _all.Where(a => types is null || types.Contains(a.TypeKey))
                        .GroupBy(a => a.Start.Date)
                        .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Start).ToList());

        // Zeitraum begrenzen: vom ersten Jahr mit Daten bis heute
        int current = PeriodOf(DateTime.Today);
        int first = byDay.Count == 0 ? current : Math.Min(byDay.Keys.Min(PeriodOf), current);
        int period = Math.Clamp(_period ?? current, first, current);
        _period = period;
        var from = new DateTime(period, StartMonth, 1);
        var to = from.AddYears(1);   // exklusiv

        PeriodText.Text = IsSki ? Stats.SeasonLabel(period) : period.ToString();
        PrevButton.IsEnabled = period > first;
        NextButton.IsEnabled = period < current;

        double ValueOf(List<Activity> list) => ByKm ? list.Sum(a => a.Km) : list.Sum(a => a.ElevationGain);
        var inPeriod = byDay.Where(kv => kv.Key >= from && kv.Key < to).ToDictionary(kv => kv.Key, kv => kv.Value);
        var dayValues = inPeriod.ToDictionary(kv => kv.Key, kv => ValueOf(kv.Value));

        // Stufen nach Quartilen der aktiven Tage, damit Ausreißer die Karte nicht "blass" machen
        var sorted = dayValues.Values.Where(v => v > 0).Order().ToList();
        List<double> thresholds = sorted.Count == 0
            ? []
            : new[] { 0.25, 0.5, 0.75 }.Select(p => sorted[(int)Math.Round((sorted.Count - 1) * p)]).ToList();
        int LevelOf(DateTime d) => dayValues.TryGetValue(d, out var v)
            ? Math.Min(thresholds.Count(t => v > t), LevelAlpha.Length - 1)
            : -1;

        var baseColor = Groups[GroupIndex].Color;
        string unit = ByKm ? "km" : "Hm";
        SummaryText.Text = $"{inPeriod.Count} Tage · {inPeriod.Values.Sum(l => l.Count)} Aktivitäten · " +
                           $"{dayValues.Values.Sum():N0} {unit}";

        // Legende
        Legend.Children.Clear();
        Legend.Children.Add(Swatch(EmptyBrush()));
        foreach (var alpha in LevelAlpha) Legend.Children.Add(Swatch(new SolidColorBrush(WithAlpha(baseColor, alpha))));

        // Monate
        Months.Children.Clear();
        for (int i = 0; i < 12; i++)
        {
            var month = from.AddMonths(i);
            Months.Children.Add(BuildMonth(month, d =>
            {
                int level = LevelOf(d);
                return level < 0 ? EmptyBrush() : new SolidColorBrush(WithAlpha(baseColor, LevelAlpha[level]));
            }, d => LevelOf(d) >= 2, byDay, unit, showYear: IsSki));
        }

        ShowDay(byDay);
    }

    private FrameworkElement BuildMonth(DateTime month, Func<DateTime, Brush> colorOf, Func<DateTime, bool> strong,
                                        Dictionary<DateTime, List<Activity>> byDay, string unit, bool showYear)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 28, 20) };
        var name = month.ToString(showYear ? "MMMM yyyy" : "MMMM", CultureInfo.CurrentCulture);
        panel.Children.Add(new TextBlock
        {
            Text = name,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });

        var grid = new UniformGrid { Columns = 7 };
        foreach (var wd in Weekdays)
        {
            grid.Children.Add(new TextBlock
            {
                Text = wd,
                FontSize = 10,
                Opacity = 0.7,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2),
            });
        }

        int offset = ((int)month.DayOfWeek + 6) % 7;   // Montag = 0
        for (int k = 0; k < offset; k++) grid.Children.Add(new Border());

        var today = DateTime.Today;
        int days = DateTime.DaysInMonth(month.Year, month.Month);
        for (int dayNo = 1; dayNo <= days; dayNo++)
        {
            var d = new DateTime(month.Year, month.Month, dayNo);
            var cell = new Border
            {
                Width = CellSize,
                Height = CellSize,
                Margin = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Background = colorOf(d),
                BorderThickness = new Thickness(d == _selectedDay ? 2 : d == today ? 1 : 0),
                Child = new TextBlock
                {
                    Text = dayNo.ToString(),
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = strong(d) ? Brushes.White : null,
                    Opacity = d > today ? 0.35 : 1,
                },
            };
            if (d == _selectedDay) cell.SetResourceReference(Border.BorderBrushProperty, "AccentFillColorDefaultBrush");
            else if (d == today) cell.SetResourceReference(Border.BorderBrushProperty, "TextFillColorPrimaryBrush");
            if (cell.Child is TextBlock text && !strong(d)) text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

            if (byDay.TryGetValue(d, out var list))
            {
                cell.Cursor = Cursors.Hand;
                cell.ToolTip = string.Join("\n", list.Select(a =>
                    $"{a.Name} · {(unit == "km" ? $"{a.Km:N1} km" : $"{a.ElevationGain:N0} Hm")}"));
            }
            if (d <= today)
            {
                cell.MouseLeftButtonUp += (_, _) =>
                {
                    _selectedDay = d;
                    Render();
                };
            }
            grid.Children.Add(cell);
        }
        panel.Children.Add(grid);
        return panel;
    }

    private void ShowDay(Dictionary<DateTime, List<Activity>> byDay)
    {
        if (_selectedDay is not DateTime day)
        {
            DayPanel.Visibility = Visibility.Collapsed;
            return;
        }
        DayPanel.Visibility = Visibility.Visible;
        List<Activity> list = byDay.TryGetValue(day, out var l) ? l : [];
        DayHeader.Text = $"{day:dddd, d. MMMM yyyy}" + (list.Count == 0 ? " – keine Aktivitäten" : "");
        DayTable.Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        DayTable.ItemsSource = list;
        DayTable.SelectedItem = null;

        if (list.Count == 1) TourDetails.FollowIfOpen(list[0]);
    }

    private void OnDayTourSelected(object sender, SelectionChangedEventArgs e)
    {
        if (DayTable.SelectedItem is not Activity a) return;
        TourDetails.Open(a, Window.GetWindow(this));
        DayTable.SelectedItem = null;   // damit ein erneuter Klick wieder öffnet
    }

    private static Border Swatch(Brush brush) => new()
    {
        Width = 12,
        Height = 12,
        Margin = new Thickness(2, 0, 2, 0),
        CornerRadius = new CornerRadius(2),
        Background = brush,
    };

    private static Brush EmptyBrush() => new SolidColorBrush(Color.FromArgb(0x26, 0x80, 0x80, 0x80));

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);
}
