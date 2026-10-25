namespace MyActivities.Domain;

public readonly record struct GeoPoint(double Lat, double Lon);

/// <summary>Eine Aktivität aus der lokalen Datenbank.</summary>
public sealed class Activity
{
    public required long Id { get; init; }
    public required string Name { get; init; }
    public required string TypeKey { get; init; }
    public required DateTime Start { get; init; }
    public double DistanceM { get; init; }
    public double DurationS { get; init; }
    public double ElevationGain { get; init; }
    public string? LocationName { get; init; }
    public double? StartLat { get; init; }
    public double? StartLon { get; init; }
    public double? AvgSpeed { get; init; }   // m/s
    public double? MaxSpeed { get; init; }   // m/s
    public double? AvgHr { get; init; }
    public double? Calories { get; init; }
    public double? MinElev { get; init; }
    public double? MaxElev { get; init; }
    public long? DeviceId { get; init; }

    public int Year => Start.Year;
    public double Km => DistanceM / 1000.0;
    public string TypeLabel => Stats.TypeLabel(TypeKey);
    public string DurationText => Format.Duration(DurationS);
}
