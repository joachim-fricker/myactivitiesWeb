using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Xml;
using MyActivities.Domain;

namespace MyActivities.Data;

/// <summary>Vereinfachte Tracks aller Touren für die Heatmap (im Speicher, solange die App läuft).</summary>
public sealed class HeatService(TrackService tracks)
{
    /// <summary>Punkte, die näher als das am letzten behaltenen liegen, werden weggelassen.</summary>
    private const double MinStepM = 60;

    private readonly ConcurrentDictionary<long, List<GeoPoint>> _cache = new();

    /// <summary>Tracks der Touren laden (GPX aus dem Cache oder von GitHub); Fehler werden übersprungen.</summary>
    public async Task<Dictionary<long, List<GeoPoint>>> LoadAsync(IEnumerable<Activity> activities,
                                                                  IProgress<(int Done, int Total)>? progress)
    {
        var todo = activities.Where(a => !_cache.ContainsKey(a.Id)).ToList();
        int done = 0;
        await Parallel.ForEachAsync(todo, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (a, ct) =>
        {
            try
            {
                var track = await tracks.LoadAsync(a.Id, ct);
                _cache[a.Id] = Thin(track.Points);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is XmlException ||
                                       ex is TaskCanceledException)
            {
                // fehlt dann eben in der Heatmap; beim nächsten Öffnen erneut versucht
            }
            progress?.Report((Interlocked.Increment(ref done), todo.Count));
        });
        return activities.Where(a => _cache.TryGetValue(a.Id, out var p) && p.Count >= 2)
                         .ToDictionary(a => a.Id, a => _cache[a.Id]);
    }

    private static List<GeoPoint> Thin(List<GeoPoint> points)
    {
        if (points.Count < 2) return points;
        var result = new List<GeoPoint> { points[0] };
        foreach (var p in points)
        {
            var last = result[^1];
            if (Geo.DistanceM(last.Lat, last.Lon, p.Lat, p.Lon) >= MinStepM) result.Add(p);
        }
        if (result[^1] != points[^1]) result.Add(points[^1]);
        return result;
    }
}
