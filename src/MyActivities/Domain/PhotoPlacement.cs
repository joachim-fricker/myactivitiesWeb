namespace MyActivities.Domain;

public static class PhotoPlacement
{
    /// <summary>Fotos so viel vor dem Start bzw. nach dem Ende der Tour gehören noch dazu.</summary>
    public static readonly TimeSpan Buffer = TimeSpan.FromMinutes(15);

    /// <summary>Liegt die GPS-Position eines Fotos weiter weg vom Track, wird sie über die Aufnahmezeit bestimmt.</summary>
    public const double MaxOffTrackM = 500;

    /// <summary>
    /// Differenz Ortszeit − UTC der Tour: Startzeit (lokal, von Garmin) minus erster GPX-Zeitstempel,
    /// auf 15 Minuten gerundet. null = keine Zeitstempel im GPX.
    /// </summary>
    public static TimeSpan? LocalOffset(DateTime startLocal, List<TimedPoint> timeline)
    {
        if (timeline.Count == 0) return null;
        double minutes = (startLocal - timeline[0].Utc).TotalMinutes;
        return TimeSpan.FromMinutes(Math.Round(minutes / 15) * 15);
    }

    /// <summary>Position auf dem Track zum Zeitpunkt utc (linear interpoliert). null = keine Zeitachse.</summary>
    public static GeoPoint? PositionAt(List<TimedPoint> timeline, DateTime utc)
    {
        if (timeline.Count == 0) return null;
        if (utc <= timeline[0].Utc) return new GeoPoint(timeline[0].Lat, timeline[0].Lon);
        if (utc >= timeline[^1].Utc) return new GeoPoint(timeline[^1].Lat, timeline[^1].Lon);

        int lo = 0, hi = timeline.Count - 1;          // binäre Suche: timeline[lo] <= utc < timeline[hi]
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (timeline[mid].Utc <= utc) lo = mid; else hi = mid;
        }
        var a = timeline[lo];
        var b = timeline[hi];
        double span = (b.Utc - a.Utc).TotalMilliseconds;
        double f = span <= 0 ? 0 : (utc - a.Utc).TotalMilliseconds / span;
        return new GeoPoint(a.Lat + f * (b.Lat - a.Lat), a.Lon + f * (b.Lon - a.Lon));
    }

    /// <summary>Kürzester Abstand (m) eines Punkts zum Track (lokal eben gerechnet, genügt für kurze Distanzen).</summary>
    public static double DistanceToTrackM(GeoPoint p, List<GeoPoint> track)
    {
        if (track.Count == 0) return double.MaxValue;
        double kx = 111_320 * Math.Cos(p.Lat * Math.PI / 180);
        const double ky = 110_540;
        double best = double.MaxValue;
        for (int i = 0; i < track.Count; i++)
        {
            double ax = (track[i].Lon - p.Lon) * kx, ay = (track[i].Lat - p.Lat) * ky;
            if (i == 0 || track.Count == 1)
            {
                best = Math.Min(best, Math.Sqrt(ax * ax + ay * ay));
                continue;
            }
            double bx = (track[i - 1].Lon - p.Lon) * kx, by = (track[i - 1].Lat - p.Lat) * ky;
            double dx = ax - bx, dy = ay - by;
            double len2 = dx * dx + dy * dy;
            double t = len2 == 0 ? 0 : Math.Clamp(-(bx * dx + by * dy) / len2, 0, 1);
            double x = bx + t * dx, y = by + t * dy;
            best = Math.Min(best, Math.Sqrt(x * x + y * y));
        }
        return best;
    }

    /// <summary>
    /// Wo das Foto auf der Karte erscheint: GPS aus dem Foto, wenn es nahe am Track liegt,
    /// sonst die Position auf dem Track zur Aufnahmezeit. null = nicht bestimmbar.
    /// </summary>
    public static GeoPoint? PhotoPosition(GeoPoint? gps, DateTime? takenUtc, List<GeoPoint> track, List<TimedPoint> timeline)
    {
        if (gps is GeoPoint g && (track.Count == 0 || DistanceToTrackM(g, track) <= MaxOffTrackM)) return g;
        if (takenUtc is DateTime utc && PositionAt(timeline, utc) is GeoPoint onTrack) return onTrack;
        return gps;
    }
}
