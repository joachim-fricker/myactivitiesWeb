using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MyActivities.Data;
using MyActivities.Domain;

namespace MyActivities.Views;

public partial class StatistikPage : Page
{
    /// <summary>Eine Balken-Serie: Bezeichnung und zugehörige typeKeys (null = alle übrigen Typen).</summary>
    private sealed record Series(string Label, string[]? Types)
    {
        public bool Matches(string typeKey) =>
            Types is null ? !Stats.GroupedTypes.Contains(typeKey) : Types.Contains(typeKey);
    }

    public StatistikPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Render();
    }

    /// <summary>Zahlen rechtsbündig mit Tausendertrennzeichen, Text linksbündig.</summary>
    private void OnAutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.Column is not DataGridTextColumn column) return;
        bool numeric = e.PropertyType == typeof(double) || e.PropertyType == typeof(int);
        column.ElementStyle = (Style)FindResource(numeric ? "RightCell" : "LeftCell");
        if (numeric && column.Binding is Binding binding) binding.StringFormat = "N0";
        if (!numeric) column.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) Render();
    }

    private void Render()
    {
        int group = GroupBox.SelectedIndex;      // 0 Rad, 1 Wandern, 2 Skitouren, 3 Total
        bool km = MetricBox.SelectedIndex == 0;
        bool season = group == 2;                // Skitouren nach Saison (Sep–Aug)

        List<Series> series = group switch
        {
            0 => Stats.CyclingTypes.Select(t => new Series(Stats.TypeLabel(t), [t])).ToList(),
            1 => Stats.HikingTypes.Select(t => new Series(Stats.TypeLabel(t), [t])).ToList(),
            2 => [new Series("Skitour", Stats.SkiTypes)],
            _ =>
            [
                new Series("Rad", Stats.CyclingTypes),
                new Series("Wandern", Stats.HikingTypes),
                new Series("Skitour", Stats.SkiTypes),
                new Series("Sonstige", null),
            ],
        };

        var rows = (season ? AppServices.Db.SeasonTypeSums() : AppServices.Db.YearTypeSums())
            .Where(r => series.Any(s => s.Matches(r.TypeKey)))
            .ToList();

        var plot = Chart.Plot;
        plot.Clear();
        plot.Legend.ManualItems.Clear();

        if (rows.Count == 0)
        {
            EmptyHint.Visibility = Visibility.Visible;
            Table.ItemsSource = null;
            Chart.Refresh();
            return;
        }
        EmptyHint.Visibility = Visibility.Collapsed;

        // Lückenlose Jahresachse
        int first = rows.Min(r => r.Year);
        int last = rows.Max(r => r.Year);
        int[] years = Enumerable.Range(first, last - first + 1).ToArray();

        var values = new double[series.Count, years.Length];
        var counts = new int[years.Length];
        foreach (var r in rows)
        {
            int si = series.FindIndex(s => s.Matches(r.TypeKey));
            int yi = r.Year - first;
            values[si, yi] += km ? r.DistanceM / 1000.0 : r.ElevationGain;
            counts[yi] += r.Count;
        }

        // --- Diagramm: gestapelte Balken ---
        var palette = new ScottPlot.Palettes.Category10();
        var bars = new List<ScottPlot.Bar>();
        for (int yi = 0; yi < years.Length; yi++)
        {
            double bottom = 0;
            for (int si = 0; si < series.Count; si++)
            {
                if (values[si, yi] <= 0) continue;   // keine leeren Balken (sonst schwarze Striche)
                double top = bottom + values[si, yi];
                bars.Add(new ScottPlot.Bar
                {
                    Position = yi,
                    ValueBase = bottom,
                    Value = top,
                    FillColor = palette.GetColor(si),
                });
                bottom = top;
            }
        }
        plot.Add.Bars(bars);

        if (series.Count > 1)
        {
            for (int si = 0; si < series.Count; si++)
            {
                plot.Legend.ManualItems.Add(new ScottPlot.LegendItem
                {
                    LabelText = series[si].Label,
                    FillColor = palette.GetColor(si),
                });
            }
            plot.Legend.Alignment = ScottPlot.Alignment.UpperLeft;
            plot.ShowLegend();
        }
        else
        {
            plot.HideLegend();
        }

        double[] positions = years.Select((_, i) => (double)i).ToArray();
        string[] labels = years.Select(y => season ? Stats.SeasonLabelShort(y) : y.ToString()).ToArray();
        plot.Axes.Bottom.SetTicks(positions, labels);
        plot.YLabel(km ? "km" : "Höhenmeter");
        plot.Axes.Margins(bottom: 0);
        plot.Axes.AutoScale();
        Chart.Refresh();

        // --- Tabelle: neuestes Jahr oben ---
        var table = new DataTable();
        table.Columns.Add(season ? "Saison" : "Jahr", typeof(string));
        foreach (var s in series) table.Columns.Add(s.Label, typeof(double));
        if (series.Count > 1) table.Columns.Add("Total", typeof(double));
        table.Columns.Add("Anzahl", typeof(int));

        for (int yi = years.Length - 1; yi >= 0; yi--)
        {
            var row = table.NewRow();
            row[0] = season ? Stats.SeasonLabel(years[yi]) : years[yi].ToString();
            double total = 0;
            for (int si = 0; si < series.Count; si++)
            {
                row[si + 1] = Math.Round(values[si, yi]);
                total += values[si, yi];
            }
            if (series.Count > 1) row["Total"] = Math.Round(total);
            row["Anzahl"] = counts[yi];
            table.Rows.Add(row);
        }
        Table.ItemsSource = table.DefaultView;
    }
}
