using System.Globalization;
using System.IO;
using System.Xml;
using MyActivities.Domain;

namespace MyActivities.Data;

public static class GpxParser
{
    private static readonly XmlReaderSettings Settings = new() { DtdProcessing = DtdProcessing.Ignore };

    /// <summary>Alle Trackpunkte (trkpt); falls keine vorhanden: Routenpunkte (rtept).</summary>
    public static List<GeoPoint> Parse(string gpx)
    {
        var trk = new List<GeoPoint>();
        var rte = new List<GeoPoint>();
        using var reader = XmlReader.Create(new StringReader(gpx), Settings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element) continue;
            var target = reader.LocalName switch { "trkpt" => trk, "rtept" => rte, _ => null };
            if (target is not null && TryLatLon(reader, out var lat, out var lon)) target.Add(new GeoPoint(lat, lon));
        }
        return trk.Count > 0 ? trk : rte;
    }

    /// <summary>Trackpunkte mit gültiger Zeit (und Höhe), nach Zeit sortiert.</summary>
    public static List<TimedPoint> ParseTimed(string gpx)
    {
        var result = new List<TimedPoint>();
        using var reader = XmlReader.Create(new StringReader(gpx), Settings);
        double lat = 0, lon = 0;
        double? ele = null;
        DateTime? time = null;
        bool inPoint = false;

        reader.MoveToContent();
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.LocalName)
                {
                    case "trkpt":
                        inPoint = TryLatLon(reader, out lat, out lon) && !reader.IsEmptyElement;
                        ele = null;
                        time = null;
                        break;
                    case "ele" when inPoint:
                        // ReadElementContentAsString steht danach schon auf dem nächsten Knoten
                        ele = double.TryParse(reader.ReadElementContentAsString(), NumberStyles.Float,
                                              CultureInfo.InvariantCulture, out var e) ? e : null;
                        continue;
                    case "time" when inPoint:
                        time = DateTime.TryParse(reader.ReadElementContentAsString(), CultureInfo.InvariantCulture,
                                   DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : null;
                        continue;
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "trkpt")
            {
                if (inPoint && time is DateTime utc) result.Add(new TimedPoint(lat, lon, utc, ele));
                inPoint = false;
            }
            reader.Read();
        }
        result.Sort((a, b) => a.Utc.CompareTo(b.Utc));
        return result;
    }

    private static bool TryLatLon(XmlReader reader, out double lat, out double lon)
    {
        lon = 0;
        return double.TryParse(reader.GetAttribute("lat"), NumberStyles.Float, CultureInfo.InvariantCulture, out lat) &&
               double.TryParse(reader.GetAttribute("lon"), NumberStyles.Float, CultureInfo.InvariantCulture, out lon);
    }
}
