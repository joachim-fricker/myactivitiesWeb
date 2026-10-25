namespace MyActivities.Domain;

/// <summary>Ergebnis der Gruppierung nach Jahr (bzw. Saison) und typeKey.</summary>
public sealed record YearTypeSum(int Year, string TypeKey, double DistanceM, double ElevationGain, int Count);

public static class Stats
{
    public static readonly string[] CyclingTypes = ["cycling", "road_biking", "mountain_biking", "gravel_cycling"];
    public static readonly string[] HikingTypes = ["walking", "running", "mountaineering", "hiking"];
    /// <summary>"other" wird bewusst als Skitour gezählt (alle bisherigen "other" sind Skitouren).</summary>
    public static readonly string[] SkiTypes = ["backcountry_skiing", "other"];

    public static readonly HashSet<string> GroupedTypes = [.. CyclingTypes, .. HikingTypes, .. SkiTypes];

    /// <summary>Skisaison beginnt am 1. September.</summary>
    public const int SkiSeasonStartMonth = 9;

    private static readonly Dictionary<string, string> Labels = new()
    {
        ["cycling"] = "Rad",
        ["road_biking"] = "Rennrad",
        ["mountain_biking"] = "MTB",
        ["gravel_cycling"] = "Gravel",
        ["walking"] = "Gehen",
        ["running"] = "Laufen",
        ["mountaineering"] = "Bergtour",
        ["hiking"] = "Wandern",
        ["backcountry_skiing"] = "Skitour",
        ["other"] = "Skitour",
        ["virtual_ride"] = "Indoor-Rad",
        ["indoor_cycling"] = "Indoor-Rad",
    };

    public static string TypeLabel(string typeKey) => Labels.TryGetValue(typeKey, out var l) ? l : typeKey;

    /// <summary>Wandern (ohne Laufen): hier interessiert keine Geschwindigkeit.</summary>
    private static readonly HashSet<string> NoSpeedTypes = ["walking", "hiking", "mountaineering"];

    public static bool ShowsSpeed(string typeKey) => !NoSpeedTypes.Contains(typeKey);

    /// <summary>Die Uhr: ihre Max. Geschwindigkeit beim Radfahren ist unzuverlässig (GPS-Sprünge).</summary>
    public const long WatchDeviceId = 3390593036L;

    /// <summary>
    /// Max. Geschwindigkeit (m/s), wenn sie glaubwürdig ist, sonst null:
    /// über 100 km/h nie; beim Radfahren nur mit bekanntem Gerät, das nicht die Uhr ist.
    /// </summary>
    public static double? TrustedMaxSpeed(string typeKey, double? maxSpeed, long? deviceId)
    {
        if (maxSpeed is not > 0 || maxSpeed > 100 / 3.6) return null;
        if (CyclingTypes.Contains(typeKey) && (deviceId is null or 0 || deviceId == WatchDeviceId)) return null;
        return maxSpeed;
    }

    /// <summary>"2023/24"</summary>
    public static string SeasonLabel(int startYear) => $"{startYear}/{(startYear + 1) % 100:00}";

    /// <summary>"23/24"</summary>
    public static string SeasonLabelShort(int startYear) => $"{startYear % 100:00}/{(startYear + 1) % 100:00}";
}
