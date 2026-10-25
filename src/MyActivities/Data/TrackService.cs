using System.IO;
using MyActivities.Domain;

namespace MyActivities.Data;

/// <summary>Track für die Karte (vereinfacht) und Zeitachse (alle Punkte mit Zeit) einer Aktivität.</summary>
public sealed record TrackData(List<GeoPoint> Points, List<TimedPoint> Timeline)
{
    public static readonly TrackData Empty = new([], []);
}

/// <summary>Lädt GPX-Tracks bei Bedarf von GitHub und hält sie lokal im Cache.</summary>
public sealed class TrackService(GitHubClient client, string cacheFolder)
{
    /// <summary>Mehr Punkte braucht die Karte nicht.</summary>
    private const int MaxPoints = 4000;

    public async Task<TrackData> LoadAsync(long id, CancellationToken ct = default)
    {
        Directory.CreateDirectory(cacheFolder);
        var path = Path.Combine(cacheFolder, $"{id}.gpx");

        string? gpx;
        if (File.Exists(path))
        {
            gpx = await File.ReadAllTextAsync(path, ct);
        }
        else
        {
            gpx = await client.FetchGpxAsync(id, ct);
            if (gpx is null) return TrackData.Empty;
            await File.WriteAllTextAsync(path, gpx, ct);
        }

        return await Task.Run(() => new TrackData(Downsample(GpxParser.Parse(gpx)), GpxParser.ParseTimed(gpx)), ct);
    }

    private static List<GeoPoint> Downsample(List<GeoPoint> points)
    {
        if (points.Count <= MaxPoints) return points;
        int step = (int)Math.Ceiling(points.Count / (double)MaxPoints);
        var result = new List<GeoPoint>(points.Count / step + 2);
        for (int i = 0; i < points.Count; i += step) result.Add(points[i]);
        if (result[^1] != points[^1]) result.Add(points[^1]);
        return result;
    }
}
