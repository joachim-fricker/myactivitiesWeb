using System.IO;
using Microsoft.Data.Sqlite;
using MyActivities.Domain;

namespace MyActivities.Data;

/// <summary>Lokale SQLite-Datenbank mit allen Aktivitäten.</summary>
public sealed class ActivityDatabase
{
    private readonly string _connectionString;

    public string FilePath { get; }

    public ActivityDatabase(string filePath)
    {
        FilePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = filePath }.ToString();
        CreateSchema();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void CreateSchema()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS activity (
                id            INTEGER PRIMARY KEY,
                name          TEXT    NOT NULL,
                typeKey       TEXT    NOT NULL,
                startTime     TEXT    NOT NULL,
                year          INTEGER NOT NULL,
                month         INTEGER NOT NULL,
                distanceM     REAL    NOT NULL,
                durationS     REAL    NOT NULL,
                elevationGain REAL    NOT NULL,
                startLat REAL, startLon REAL, endLat REAL, endLon REAL,
                locationName TEXT,
                avgSpeed REAL, maxSpeed REAL, avgHr REAL, calories REAL,
                minElev REAL, maxElev REAL,
                deviceId INTEGER
            );
            CREATE INDEX IF NOT EXISTS ix_activity_year ON activity(year);
            CREATE INDEX IF NOT EXISTS ix_activity_type ON activity(typeKey);
            CREATE INDEX IF NOT EXISTS ix_activity_start ON activity(startTime);
            """;
        cmd.ExecuteNonQuery();

        // Später ergänzte Spalten (bestehende Datenbanken werden erweitert).
        // Geschätzte beste Leistung am Berg in Watt: null = noch nicht berechnet, -1 = nicht verfügbar
        EnsureColumn(c, "bestP5W", "REAL");
        EnsureColumn(c, "bestP10W", "REAL");
        EnsureColumn(c, "bestP20W", "REAL");
    }

    private static void EnsureColumn(SqliteConnection c, string name, string type)
    {
        using var check = c.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('activity') WHERE name = $name";
        check.Parameters.AddWithValue("$name", name);
        if (Convert.ToInt32(check.ExecuteScalar()) > 0) return;
        using var alter = c.CreateCommand();
        alter.CommandText = $"ALTER TABLE activity ADD COLUMN {name} {type}";
        alter.ExecuteNonQuery();
    }

    private static readonly object WriteLock = new();

    /// <summary>Radtouren ohne berechnete Leistung.</summary>
    public List<(long Id, string TypeKey, string Name)> MissingPower(IEnumerable<string> types)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        var keys = types.ToList();
        var names = keys.Select((_, i) => $"$t{i}").ToList();
        cmd.CommandText = $"SELECT id, typeKey, name FROM activity WHERE bestP5W IS NULL AND typeKey IN ({string.Join(",", names)})";
        for (int i = 0; i < keys.Count; i++) cmd.Parameters.AddWithValue(names[i], keys[i]);
        using var r = cmd.ExecuteReader();
        var list = new List<(long, string, string)>();
        while (r.Read()) list.Add((r.GetInt64(0), r.GetString(1), r.GetString(2)));
        return list;
    }

    /// <summary>Leistung speichern (5/10/20 min); null wird als -1 = nicht verfügbar gespeichert.</summary>
    public void UpdatePower(long id, double?[] watts)
    {
        lock (WriteLock)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE activity SET bestP5W = $p5, bestP10W = $p10, bestP20W = $p20 WHERE id = $id";
            cmd.Parameters.AddWithValue("$p5", watts.ElementAtOrDefault(0) ?? -1);
            cmd.Parameters.AddWithValue("$p10", watts.ElementAtOrDefault(1) ?? -1);
            cmd.Parameters.AddWithValue("$p20", watts.ElementAtOrDefault(2) ?? -1);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public int Count()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM activity";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public HashSet<long> AllIds()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id FROM activity";
        using var r = cmd.ExecuteReader();
        var ids = new HashSet<long>();
        while (r.Read()) ids.Add(r.GetInt64(0));
        return ids;
    }

    public void Upsert(IEnumerable<ActivityDto> items)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT OR REPLACE INTO activity
              (id, name, typeKey, startTime, year, month, distanceM, durationS, elevationGain,
               startLat, startLon, endLat, endLon, locationName,
               avgSpeed, maxSpeed, avgHr, calories, minElev, maxElev, deviceId)
            VALUES
              ($id, $name, $typeKey, $startTime, $year, $month, $distanceM, $durationS, $elevationGain,
               $startLat, $startLon, $endLat, $endLon, $locationName,
               $avgSpeed, $maxSpeed, $avgHr, $calories, $minElev, $maxElev, $deviceId)
            """;

        foreach (var a in items)
        {
            var start = a.StartTimeLocal;              // z. B. "2021-05-01 10:12:00"
            int year = int.Parse(start[..4]);
            int month = start.Length >= 7 ? int.Parse(start.Substring(5, 2)) : 1;

            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$id", a.ActivityId);
            cmd.Parameters.AddWithValue("$name", a.ActivityName ?? $"Aktivität {a.ActivityId}");
            cmd.Parameters.AddWithValue("$typeKey", a.ActivityType?.TypeKey ?? "unknown");
            cmd.Parameters.AddWithValue("$startTime", start);
            cmd.Parameters.AddWithValue("$year", year);
            cmd.Parameters.AddWithValue("$month", month);
            cmd.Parameters.AddWithValue("$distanceM", a.Distance ?? 0.0);
            cmd.Parameters.AddWithValue("$durationS", a.Duration ?? 0.0);
            cmd.Parameters.AddWithValue("$elevationGain", a.ElevationGain ?? 0.0);
            cmd.Parameters.AddWithValue("$startLat", Nullable(a.StartLatitude));
            cmd.Parameters.AddWithValue("$startLon", Nullable(a.StartLongitude));
            cmd.Parameters.AddWithValue("$endLat", Nullable(a.EndLatitude));
            cmd.Parameters.AddWithValue("$endLon", Nullable(a.EndLongitude));
            cmd.Parameters.AddWithValue("$locationName", Nullable(a.LocationName));
            cmd.Parameters.AddWithValue("$avgSpeed", Nullable(a.AverageSpeed));
            cmd.Parameters.AddWithValue("$maxSpeed", Nullable(a.MaxSpeed));
            cmd.Parameters.AddWithValue("$avgHr", Nullable(a.AverageHR));
            cmd.Parameters.AddWithValue("$calories", Nullable(a.Calories));
            cmd.Parameters.AddWithValue("$minElev", Nullable(a.MinElevation));
            cmd.Parameters.AddWithValue("$maxElev", Nullable(a.MaxElevation));
            cmd.Parameters.AddWithValue("$deviceId", Nullable(a.DeviceId));
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static object Nullable(object? value) => value ?? DBNull.Value;

    /// <summary>Alle Aktivitäten für Tabelle und Karte.</summary>
    public List<Activity> AllActivities()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, typeKey, startTime, distanceM, durationS, elevationGain, locationName,
                   startLat, startLon, avgSpeed, maxSpeed, avgHr, calories, minElev, maxElev, deviceId,
                   bestP5W, bestP10W, bestP20W
            FROM activity
            """;
        using var r = cmd.ExecuteReader();
        var list = new List<Activity>();
        while (r.Read())
        {
            list.Add(new Activity
            {
                Id = r.GetInt64(0),
                Name = r.GetString(1),
                TypeKey = r.GetString(2),
                Start = Format.ParseStart(r.GetString(3)),
                DistanceM = r.GetDouble(4),
                DurationS = r.GetDouble(5),
                ElevationGain = r.GetDouble(6),
                LocationName = r.IsDBNull(7) ? null : r.GetString(7),
                StartLat = NullableDouble(r, 8),
                StartLon = NullableDouble(r, 9),
                AvgSpeed = NullableDouble(r, 10),
                MaxSpeed = NullableDouble(r, 11),
                AvgHr = NullableDouble(r, 12),
                Calories = NullableDouble(r, 13),
                MinElev = NullableDouble(r, 14),
                MaxElev = NullableDouble(r, 15),
                DeviceId = r.IsDBNull(16) ? null : r.GetInt64(16),
                BestP5W = Positive(NullableDouble(r, 17)),
                BestP10W = Positive(NullableDouble(r, 18)),
                BestP20W = Positive(NullableDouble(r, 19)),
            });
        }
        return list;
    }

    private static double? NullableDouble(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDouble(i);

    private static double? Positive(double? v) => v > 0 ? v : null;

    /// <summary>Summen je Kalenderjahr und typeKey.</summary>
    public List<YearTypeSum> YearTypeSums() => QuerySums("""
        SELECT year, typeKey, SUM(distanceM), SUM(elevationGain), COUNT(*)
        FROM activity GROUP BY year, typeKey
        """);

    /// <summary>Summen je Skisaison (Start 1. September, Saison 2023/24 = 2023) und typeKey.</summary>
    public List<YearTypeSum> SeasonTypeSums() => QuerySums($"""
        SELECT (year - (month < {Stats.SkiSeasonStartMonth})) AS season, typeKey,
               SUM(distanceM), SUM(elevationGain), COUNT(*)
        FROM activity GROUP BY season, typeKey
        """);

    private List<YearTypeSum> QuerySums(string sql)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var list = new List<YearTypeSum>();
        while (r.Read())
        {
            list.Add(new YearTypeSum(r.GetInt32(0), r.GetString(1), r.GetDouble(2), r.GetDouble(3), r.GetInt32(4)));
        }
        return list;
    }
}
