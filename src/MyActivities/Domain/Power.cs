namespace MyActivities.Domain;

/// <summary>
/// Geschätzte beste Durchschnittsleistung am Berg (5/10/20 min) aus dem GPX – wie in der Android-App.
/// Am Anstieg dominiert die Hubarbeit (Masse · g · Höhengewinn), die sich aus dem Höhenprofil
/// zuverlässig bestimmen lässt. Gerechnet wird pro Fenster über die Energie: Hubarbeit + Rollwiderstand
/// + Luftwiderstand + Änderung der Bewegungsenergie, geteilt durch die Zeit. Flach und bergab zählen nicht.
/// </summary>
public static class Power
{
    /// <summary>Gewicht Fahrer + Rad.</summary>
    public const double SystemMassKg = 92.0;

    /// <summary>Zeitfenster: 5, 10 und 20 Minuten.</summary>
    public static readonly int[] WindowsS = [5 * 60, 10 * 60, 20 * 60];

    private const double G = 9.81;
    private const double DrivetrainEfficiency = 0.976;
    private const int MaxGapS = 10;                 // längere Lücken = Pause
    private const double MaxSpeedMs = 25.0;         // mehr als 90 km/h = GPS-Sprung
    private const double SpikeMs = 4.0;             // einzelner Tempo-Ausreißer = GPS-Sprung
    private const int SpeedSmoothS = 5;
    private const int EleMedianHalfS = 30;          // Höhe: Ausreißer gegen 1-Minuten-Median
    private const double EleOutlierM = 5.0;
    private const int EleSmoothS = 15;
    private const double MinWindowGrade = 0.03;     // nur Anstiege (mind. 3 %) ...
    private const double MinContextGrade = 0.03;    // ... auch in der Umgebung (± 30 s)
    private const int ContextS = 30;
    private const double MaxVerticalMs = 0.8;       // mehr als 2'900 Hm/h = Messfehler
    private const double MaxWindowVamMs = 0.5;      // mehr als 1'800 Hm/h über Minuten = Bahn/Lift

    private static (double Crr, double Cda) BikeParams(string typeKey) => typeKey switch
    {
        "road_biking" => (0.004, 0.30),
        "gravel_cycling" => (0.007, 0.35),
        "mountain_biking" => (0.010, 0.40),
        _ => (0.005, 0.33),
    };

    /// <summary>Beste Leistung (W) je Zeitfenster; null = nicht bestimmbar (keine Zeiten/Höhen, kein Anstieg).</summary>
    public static double?[] BestPowers(List<TimedPoint> points, string typeKey)
    {
        var none = WindowsS.Select(_ => (double?)null).ToArray();

        // nach Zeit sortiert, höchstens ein Punkt pro Sekunde
        var pts = new List<TimedPoint>(points.Count);
        long lastSecond = long.MinValue;
        foreach (var p in points.OrderBy(p => p.Utc))
        {
            long second = (long)Math.Floor(Seconds(p));
            if (second == lastSecond) continue;
            lastSecond = second;
            pts.Add(p);
        }
        if (pts.Count < 2 || pts.All(p => p.Ele is null)) return none;

        // Kumulierte Distanz und Höhe je Originalpunkt
        int n = pts.Count;
        var dist = new double[n];
        var ele = new double[n];
        double lastEle = pts.First(p => p.Ele is not null).Ele!.Value;
        for (int k = 0; k < n; k++)
        {
            if (k > 0) dist[k] = dist[k - 1] + Geo.DistanceM(pts[k - 1].Lat, pts[k - 1].Lon, pts[k].Lat, pts[k].Lon);
            lastEle = pts[k].Ele ?? lastEle;
            ele[k] = lastEle;
        }

        // Auf 1-Sekunden-Raster interpolieren; Lücken > MaxGapS markieren
        long t0 = (long)Math.Floor(Seconds(pts[0]));
        int total = (int)((long)Math.Floor(Seconds(pts[^1])) - t0);
        if (total <= WindowsS.Min()) return none;
        var d = new double[total + 1];
        var e = new double[total + 1];
        var moving = new bool[total + 1];
        int i = 0;
        for (int s = 0; s <= total; s++)
        {
            double ts = t0 + s;
            while (i < n - 2 && Seconds(pts[i + 1]) <= ts) i++;
            double ta = Seconds(pts[i]), tb = Seconds(pts[i + 1]);
            double span = tb - ta;
            double f = span > 0 ? Math.Clamp((ts - ta) / span, 0, 1) : 0;
            d[s] = dist[i] + f * (dist[i + 1] - dist[i]);
            e[s] = ele[i] + f * (ele[i + 1] - ele[i]);
            moving[s] = span <= MaxGapS;
        }

        // Tempo je Sekunde, GPS-Sprünge durch den vorherigen Wert ersetzen
        var vRaw = new double[total + 1];
        for (int s = 0; s < total; s++) vRaw[s] = d[s + 1] - d[s];
        var v = (double[])vRaw.Clone();
        for (int s = 0; s < total; s++)
        {
            double prev = s > 0 ? vRaw[s - 1] : vRaw[Math.Min(s + 1, total)];
            double next = s + 1 < total ? vRaw[s + 1] : prev;
            bool spike = Math.Abs(vRaw[s] - prev) > SpikeMs && Math.Abs(vRaw[s] - next) > SpikeMs;
            if (vRaw[s] > MaxSpeedMs || spike) v[s] = s > 0 ? v[s - 1] : next;
        }
        var vSmooth = MovingAverage(v, SpeedSmoothS);

        // Höhe bereinigen: Ausreißer überbrücken, dann glätten
        var eSmooth = MovingAverage(RemoveElevationOutliers(e), EleSmoothS);

        // Präfixsummen: Distanz, Luftwiderstandsarbeit, Pausen/unplausible Höhensprünge
        var (crr, cda) = BikeParams(typeKey);
        const double m = SystemMassKg;
        var distP = new double[total + 2];
        var aeroP = new double[total + 2];
        var badP = new int[total + 2];
        for (int s = 0; s <= total; s++)
        {
            double rho = 1.225 * Math.Exp(-eSmooth[s] / 8400.0);
            distP[s + 1] = distP[s] + v[s];
            aeroP[s + 1] = aeroP[s] + 0.5 * rho * cda * v[s] * v[s] * v[s];
            bool jump = s < total && Math.Abs(eSmooth[s + 1] - eSmooth[s]) > MaxVerticalMs;
            badP[s + 1] = badP[s] + (!moving[s] || jump ? 1 : 0);
        }

        var result = new double?[WindowsS.Length];
        for (int wi = 0; wi < WindowsS.Length; wi++)
        {
            int w = WindowsS[wi];
            if (w >= total) continue;
            double best = 0;
            for (int s = 0; s <= total - w; s++)
            {
                int end = s + w;
                if (badP[end] - badP[s] > 0) continue;
                double dd = distP[end] - distP[s];
                double dh = eSmooth[end] - eSmooth[s];
                if (dd < 10.0 || dh / dd < MinWindowGrade || dh / w > MaxWindowVamMs) continue;
                // Fenster muss in einem Anstieg liegen, nicht nur eine kurze Kuppe sein
                int a = Math.Max(0, s - ContextS);
                int z = Math.Min(total, end + ContextS);
                double ctxDist = distP[z] - distP[a];
                if (ctxDist <= 0 || (eSmooth[z] - eSmooth[a]) / ctxDist < MinContextGrade) continue;

                double work = m * G * dh +
                              m * G * crr * dd +
                              (aeroP[end] - aeroP[s]) +
                              0.5 * m * (vSmooth[end] * vSmooth[end] - vSmooth[s] * vSmooth[s]);
                best = Math.Max(best, work / w / DrivetrainEfficiency);
            }
            if (best > 0) result[wi] = best;
        }
        return result;
    }

    private static double Seconds(TimedPoint p) => (p.Utc - DateTime.UnixEpoch).TotalSeconds;

    /// <summary>Höhenwerte, die mehr als EleOutlierM vom 1-Minuten-Median abweichen, linear ersetzen.</summary>
    private static double[] RemoveElevationOutliers(double[] e)
    {
        int n = e.Length;
        var bad = new bool[n];
        var buf = new double[2 * EleMedianHalfS + 1];
        for (int k = 0; k < n; k++)
        {
            int lo = Math.Max(0, k - EleMedianHalfS);
            int hi = Math.Min(n - 1, k + EleMedianHalfS);
            int len = hi - lo + 1;
            Array.Copy(e, lo, buf, 0, len);
            Array.Sort(buf, 0, len);
            bad[k] = Math.Abs(e[k] - buf[len / 2]) > EleOutlierM;
        }
        var result = (double[])e.Clone();
        int i = 0;
        while (i < n)
        {
            if (!bad[i]) { i++; continue; }
            int j = i;
            while (j < n && bad[j]) j++;
            double a = i > 0 ? e[i - 1] : j < n ? e[j] : e[i];
            double b = j < n ? e[j] : a;
            for (int q = i; q < j; q++) result[q] = a + (b - a) * (q - i + 1) / (j - i + 1);
            i = j;
        }
        return result;
    }

    private static double[] MovingAverage(double[] x, int window)
    {
        int half = window / 2;
        var prefix = new double[x.Length + 1];
        for (int k = 0; k < x.Length; k++) prefix[k + 1] = prefix[k] + x[k];
        var result = new double[x.Length];
        for (int k = 0; k < x.Length; k++)
        {
            int lo = Math.Max(0, k - half);
            int hi = Math.Min(x.Length - 1, k + half);
            result[k] = (prefix[hi + 1] - prefix[lo]) / (hi - lo + 1);
        }
        return result;
    }
}

public static class Geo
{
    /// <summary>Distanz zwischen zwei Punkten in Metern (Haversine).</summary>
    public static double DistanceM(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6_371_000;
        double dLat = (lat2 - lat1) * Math.PI / 180;
        double dLon = (lon2 - lon1) * Math.PI / 180;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * r * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}
