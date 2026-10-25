namespace MyActivities.Domain;

/// <summary>Regeln für die Länderkarte (wie in der Android-App). Codes = ADM0_A3 aus countries.geojson.</summary>
public static class Countries
{
    /// <summary>Länder, die ohne (GPS-)Fotos als besucht gelten. GBR = ganzes Vereinigtes Königreich.</summary>
    public static readonly HashSet<string> ManualVisited =
    [
        "CAN", "USA", "MEX", "TUN", "MAR", "GBR", "IRL", "NLD", "BEL", "AND",
        "HUN", "DNK", "SWE", "CUB", "ECU", "BOL", "PER", "FIN", "DMA",
        "MCO", "VAT",   // Monaco, Vatikanstadt
    ];

    /// <summary>Länder, die trotz Fotos nicht als besucht gelten (Fotos nicht von einer eigenen Reise).</summary>
    public static readonly HashSet<string> Excluded = ["IND"];

    /// <summary>Gebiet, in dem das Land fest vorgegeben ist (Grenzdaten dort zu ungenau).</summary>
    public sealed record Override(double MinLat, double MaxLat, double MinLon, double MaxLon, string Country)
    {
        public bool Contains(double lat, double lon) => lat >= MinLat && lat <= MaxLat && lon >= MinLon && lon <= MaxLon;
    }

    public static readonly Override[] Overrides =
    [
        // Khumbu-Tal bis Everest Base Camp: Die Grenze in den Natural-Earth-Daten verläuft
        // zu weit südlich, das Basislager läge sonst in China.
        new(27.80, 28.02, 86.60, 86.88, "NPL"),
    ];

    /// <summary>Erhöhen, wenn sich die Regeln oder Grenzdaten ändern: dann wird das Land aller Fotos neu bestimmt.</summary>
    public const int RulesVersion = 1;
}
