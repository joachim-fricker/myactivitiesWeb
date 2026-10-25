using System.Globalization;

namespace MyActivities.Domain;

public static class Format
{
    /// <summary>startTimeLocal von Garmin, z. B. "2021-05-01 10:12:00".</summary>
    public static DateTime ParseStart(string s)
    {
        if (DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : DateTime.MinValue;
    }

    /// <summary>"3:25" bzw. mit Sekunden "3:25:07".</summary>
    public static string Duration(double seconds, bool withSeconds = false)
    {
        var t = TimeSpan.FromSeconds(Math.Round(seconds));
        int h = (int)t.TotalHours;
        return withSeconds ? $"{h}:{t.Minutes:00}:{t.Seconds:00}" : $"{h}:{t.Minutes:00}";
    }

    /// <summary>Pace "5:12" (min/km) aus Sekunden pro km.</summary>
    public static string Pace(double secondsPerKm)
    {
        var t = TimeSpan.FromSeconds(Math.Round(secondsPerKm));
        return $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }
}
