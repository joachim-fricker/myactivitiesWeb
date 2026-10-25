using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Xml;
using MyActivities.Data;
using MyActivities.Domain;

namespace MyActivities.Views;

/// <summary>Ergebnis für die Karte: JavaScript für map.html, die Fotos der Tour und ein Statustext.</summary>
public sealed record TourMapResult(string Script, List<TourPhoto> Photos, string Status, bool HasContent, TrackData Track);

/// <summary>Lädt Track und Fotos einer Tour und baut daraus die Aufrufe für map.html.</summary>
public static class TourMap
{
    public static async Task<TourMapResult> BuildAsync(Activity a)
    {
        // Track und Fotos parallel laden
        var photosTask = AppServices.Photos.PhotosForAsync(a);
        var track = TrackData.Empty;
        string? trackError = null;
        try
        {
            track = await AppServices.Tracks.LoadAsync(a.Id);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or XmlException)
        {
            trackError = ex.Message;
        }

        List<TourPhoto> photos;
        try
        {
            photos = await photosTask;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            photos = [];
        }

        // Fotos platzieren (Dateiname = Ortszeit, GPX = UTC)
        var offset = PhotoPlacement.LocalOffset(a.Start, track.Timeline);
        foreach (var photo in photos)
        {
            DateTime? takenUtc = offset is TimeSpan o ? photo.Taken - o : null;
            photo.MapPosition = PhotoPlacement.PhotoPosition(photo.Gps, takenUtc, track.Points, track.Timeline);
        }

        string script;
        string status;
        bool hasContent = true;
        if (track.Points.Count >= 2)
        {
            script = $"showTrack({JsonSerializer.Serialize(track.Points.Select(p => new[] { p.Lat, p.Lon }))})";
            status = "";
        }
        else if (a.StartLat is double lat && a.StartLon is double lon)
        {
            script = $"showPoint({lat.ToString(CultureInfo.InvariantCulture)}, {lon.ToString(CultureInfo.InvariantCulture)})";
            status = trackError is null
                ? "Kein GPS-Track vorhanden – Startpunkt angezeigt."
                : $"Track konnte nicht geladen werden: {trackError}";
        }
        else
        {
            script = "clearTrack()";
            status = trackError is null ? "Kein GPS-Track vorhanden." : $"Track konnte nicht geladen werden: {trackError}";
            hasContent = false;
        }

        var markers = photos
            .Select((photo, index) => (photo, index))
            .Where(x => x.photo.MapPosition is not null)
            .Select(x => new
            {
                i = x.index,
                lat = x.photo.MapPosition!.Value.Lat,
                lon = x.photo.MapPosition!.Value.Lon,
                url = AppServices.Photos.MapUrl(x.photo),
            })
            .ToList();
        if (markers.Count > 0) script += $"; showPhotos({JsonSerializer.Serialize(markers)})";

        return new TourMapResult(script, photos, status, hasContent, track);
    }
}
