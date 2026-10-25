namespace MyActivities.Data;

/// <summary>Garmin-JSON einer Aktivität (nur die Felder, die wir brauchen).</summary>
public sealed class ActivityDto
{
    public long ActivityId { get; set; }
    public string? ActivityName { get; set; }
    public string StartTimeLocal { get; set; } = "";
    public TypeDto? ActivityType { get; set; }
    public double? Distance { get; set; }
    public double? Duration { get; set; }
    public double? ElevationGain { get; set; }
    public double? StartLatitude { get; set; }
    public double? StartLongitude { get; set; }
    public double? EndLatitude { get; set; }
    public double? EndLongitude { get; set; }
    public string? LocationName { get; set; }
    public double? AverageSpeed { get; set; }
    public double? MaxSpeed { get; set; }
    public double? AverageHR { get; set; }
    public double? Calories { get; set; }
    public double? MinElevation { get; set; }
    public double? MaxElevation { get; set; }
    public long? DeviceId { get; set; }
}

public sealed class TypeDto
{
    public string? TypeKey { get; set; }
}
