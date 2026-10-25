using System.Globalization;
using System.IO;
using System.Xml;
using MyActivities.Domain;

namespace MyActivities.Data;

public static class GpxParser
{
    /// <summary>Alle Trackpunkte (trkpt); falls keine vorhanden: Routenpunkte (rtept).</summary>
    public static List<GeoPoint> Parse(string gpx)
    {
        var trk = new List<GeoPoint>();
        var rte = new List<GeoPoint>();
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore };
        using var reader = XmlReader.Create(new StringReader(gpx), settings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element) continue;
            var target = reader.LocalName switch { "trkpt" => trk, "rtept" => rte, _ => null };
            if (target is null) continue;
            if (double.TryParse(reader.GetAttribute("lat"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
                double.TryParse(reader.GetAttribute("lon"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            {
                target.Add(new GeoPoint(lat, lon));
            }
        }
        return trk.Count > 0 ? trk : rte;
    }
}
