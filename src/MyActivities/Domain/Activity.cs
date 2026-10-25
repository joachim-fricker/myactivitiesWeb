using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MyActivities.Domain;

public readonly record struct GeoPoint(double Lat, double Lon);

/// <summary>GPX-Punkt mit Zeitstempel (UTC) und Höhe (m, falls vorhanden).</summary>
public readonly record struct TimedPoint(double Lat, double Lon, DateTime Utc, double? Ele = null);

/// <summary>Eine Aktivität aus der lokalen Datenbank.</summary>
public sealed class Activity : INotifyPropertyChanged
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

    // Geschätzte beste Leistung am Berg (W); werden im Hintergrund nachberechnet, deshalb mit Benachrichtigung
    private double? _bestP5W, _bestP10W, _bestP20W;
    public double? BestP5W { get => _bestP5W; set => Set(ref _bestP5W, value); }
    public double? BestP10W { get => _bestP10W; set => Set(ref _bestP10W, value); }
    public double? BestP20W { get => _bestP20W; set => Set(ref _bestP20W, value); }

    public int Year => Start.Year;

    /// <summary>Ø Geschwindigkeit in km/h (nicht bei Wandern/Gehen/Bergtour).</summary>
    public double? AvgKmh => Stats.ShowsSpeed(TypeKey) && AvgSpeed > 0 ? AvgSpeed * 3.6 : null;

    /// <summary>Max. Geschwindigkeit in km/h, nur wenn glaubwürdig (siehe Stats.TrustedMaxSpeed).</summary>
    public double? MaxKmh => Stats.ShowsSpeed(TypeKey) ? Stats.TrustedMaxSpeed(TypeKey, MaxSpeed, DeviceId) * 3.6 : null;

    public void SetPower(double?[] watts)
    {
        BestP5W = watts.ElementAtOrDefault(0);
        BestP10W = watts.ElementAtOrDefault(1);
        BestP20W = watts.ElementAtOrDefault(2);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref double? field, double? value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    public double Km => DistanceM / 1000.0;
    public string TypeLabel => Stats.TypeLabel(TypeKey);
    public string DurationText => Format.Duration(DurationS);
}
