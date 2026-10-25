using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using MyActivities.Domain;

namespace MyActivities.Data;

/// <summary>
/// Besuchte Länder anhand der GPS-Positionen in den Fotos. Der Fotoordner wird einmal ganz
/// gelesen, danach nur noch neue oder geänderte Fotos (Ergebnis in photo_location).
/// </summary>
public sealed class PhotoCountryService(ActivityDatabase db, CountryLookup lookup, string root)
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg" };
    private Task<int>? _running;

    public CountryLookup Lookup => lookup;

    /// <summary>Fotoordner abgleichen; liefert die Anzahl neu gelesener Fotos.</summary>
    public Task<int> ScanAsync(IProgress<(int Done, int Total)>? progress) =>
        _running is { IsCompleted: false } ? _running : _running = Task.Run(() => Run(progress));

    private int Run(IProgress<(int Done, int Total)>? progress)
    {
        ReassignIfRulesChanged();
        if (!Directory.Exists(root)) return 0;

        progress?.Report((0, 0));
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        var files = Directory.EnumerateFiles(root, "*", options)
            .Where(f => Extensions.Contains(Path.GetExtension(f)) &&
                        !f.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase))
            .Select(f => (Full: f, Rel: Path.GetRelativePath(root, f), Modified: File.GetLastWriteTimeUtc(f).Ticks))
            .ToList();

        var known = db.PhotoKeys();
        var present = files.Select(f => f.Rel).ToHashSet(StringComparer.OrdinalIgnoreCase);
        db.DeletePhotos(known.Keys.Where(k => !present.Contains(k)).ToList());

        var todo = files.Where(f => !known.TryGetValue(f.Rel, out var m) || m != f.Modified).ToList();
        if (todo.Count == 0) return 0;

        int done = 0;
        progress?.Report((0, todo.Count));
        foreach (var chunk in todo.Chunk(200))
        {
            var rows = new ConcurrentBag<ActivityDatabase.PhotoLocation>();
            Parallel.ForEach(chunk, new ParallelOptions { MaxDegreeOfParallelism = 4 }, f =>
            {
                var exif = PhotoService.ReadExif(f.Full);
                var taken = PhotoService.TakenFromName(Path.GetFileName(f.Full)) ?? exif.Taken
                            ?? new DateTime(f.Modified, DateTimeKind.Utc).ToLocalTime();
                string? country = exif.Gps is GeoPoint g ? lookup.CountryAt(g.Lat, g.Lon) : null;
                rows.Add(new ActivityDatabase.PhotoLocation(f.Rel, f.Modified, taken, exif.Gps?.Lat, exif.Gps?.Lon, country));
            });
            db.UpsertPhotos(rows);
            done += chunk.Length;
            progress?.Report((done, todo.Count));
        }
        return todo.Count;
    }

    /// <summary>Neue Länderregeln: Land für alle gespeicherten Positionen neu bestimmen (ohne Fotos neu zu lesen).</summary>
    private void ReassignIfRulesChanged()
    {
        var version = Countries.RulesVersion.ToString(CultureInfo.InvariantCulture);
        if (db.GetMeta("countryRules") == version) return;
        var rows = db.PhotosWithPosition()
            .Select(p => p with { Country = lookup.CountryAt(p.Lat!.Value, p.Lon!.Value) })
            .ToList();
        db.UpsertPhotos(rows);
        db.SetMeta("countryRules", version);
    }

    /// <summary>Besuchte Länder: aus Fotos (ohne ausgeschlossene) plus manuell eingetragene, meiste Fotos zuerst.</summary>
    public List<ActivityDatabase.CountryStat> VisitedCountries()
    {
        var stats = db.CountryStats().Where(s => !Countries.Excluded.Contains(s.Country)).ToList();
        var known = stats.Select(s => s.Country).ToHashSet();
        stats.AddRange(Countries.ManualVisited.Where(c => !known.Contains(c))
                                               .Select(c => new ActivityDatabase.CountryStat(c, 0, null, null)));
        return stats;
    }

    /// <summary>Fotos eines Landes als TourPhoto (neueste zuerst).</summary>
    public List<TourPhoto> PhotosIn(string country) =>
        db.PhotosInCountry(country)
          .Select(p => new TourPhoto(Path.Combine(root, p.Path), p.Taken)
          {
              Gps = p.Lat is double lat && p.Lon is double lon ? new GeoPoint(lat, lon) : null,
          })
          .ToList();
}
