using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MyActivities.Domain;

namespace MyActivities.Data;

/// <summary>Ein Foto zu einer Tour. Thumb wird nachgeladen (deshalb INotifyPropertyChanged).</summary>
public sealed class TourPhoto(string path, DateTime taken) : INotifyPropertyChanged
{
    public string Path { get; } = path;
    /// <summary>Aufnahmezeit (Ortszeit, aus dem Dateinamen).</summary>
    public DateTime Taken { get; } = taken;
    public GeoPoint? Gps { get; set; }
    public int Orientation { get; set; } = 1;
    public GeoPoint? MapPosition { get; set; }

    private BitmapSource? _thumb;
    public BitmapSource? Thumb
    {
        get => _thumb;
        set { _thumb = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumb))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Findet Fotos im Fotoordner über den Zeitstempel im Dateinamen (z. B. 20250102_101929.jpg).</summary>
public sealed partial class PhotoService(string root)
{
    private sealed record PhotoFile(string Path, DateTime Taken);

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };

    [GeneratedRegex(@"(\d{8})_(\d{6})")]
    private static partial Regex StampRegex();

    private Task<List<PhotoFile>>? _index;

    public string Root => root;

    /// <summary>Verzeichnis aller Fotos (einmal pro Programmstart im Hintergrund aufgebaut).</summary>
    private Task<List<PhotoFile>> Index => _index ??= Task.Run(BuildIndex);

    private List<PhotoFile> BuildIndex()
    {
        if (!Directory.Exists(root)) return [];
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        var byName = new Dictionary<string, PhotoFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(root, "*", options))
        {
            if (!Extensions.Contains(System.IO.Path.GetExtension(file))) continue;
            var name = System.IO.Path.GetFileName(file);
            if (name.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase)) continue;
            var m = StampRegex().Match(name);
            if (!m.Success) continue;
            if (!DateTime.TryParseExact(m.Groups[1].Value + m.Groups[2].Value, "yyyyMMddHHmmss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var taken)) continue;
            byName.TryAdd(name, new PhotoFile(file, taken));   // Kopien desselben Fotos nur einmal
        }
        return byName.Values.OrderBy(p => p.Taken).ToList();
    }

    /// <summary>Fotos, die während der Tour (± Puffer) aufgenommen wurden, älteste zuerst.</summary>
    public async Task<List<TourPhoto>> PhotosForAsync(Activity a)
    {
        var index = await Index;
        var from = a.Start - PhotoPlacement.Buffer;
        var to = a.Start.AddSeconds(a.DurationS) + PhotoPlacement.Buffer;
        var photos = index.Where(p => p.Taken >= from && p.Taken <= to)
                          .Select(p => new TourPhoto(p.Path, p.Taken))
                          .ToList();
        await Task.Run(() => photos.ForEach(ReadMetadata));
        return photos;
    }

    /// <summary>GPS-Position und Drehung aus den EXIF-Daten.</summary>
    private static void ReadMetadata(TourPhoto photo)
    {
        try
        {
            using var stream = File.OpenRead(photo.Path);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile,
                                           BitmapCacheOption.None);
            if (frame.Metadata is not BitmapMetadata md) return;

            if (md.GetQuery("/app1/ifd/{ushort=274}") is ushort orientation) photo.Orientation = orientation;

            var lat = Degrees(md.GetQuery("/app1/ifd/gps/{ushort=2}"));
            var lon = Degrees(md.GetQuery("/app1/ifd/gps/{ushort=4}"));
            if (lat is double la && lon is double lo && (la != 0 || lo != 0))
            {
                if (md.GetQuery("/app1/ifd/gps/{ushort=1}") as string == "S") la = -la;
                if (md.GetQuery("/app1/ifd/gps/{ushort=3}") as string == "W") lo = -lo;
                photo.Gps = new GeoPoint(la, lo);
            }
        }
        catch (Exception)
        {
            // keine oder unlesbare EXIF-Daten: dann eben ohne GPS/Drehung
        }
    }

    /// <summary>EXIF-Grad/Minuten/Sekunden (je ein Bruch: unten Zähler, oben Nenner) in Dezimalgrad.</summary>
    private static double? Degrees(object? value)
    {
        if (value is not ulong[] { Length: 3 } parts) return null;
        static double Rational(ulong v)
        {
            uint numerator = (uint)(v & 0xFFFFFFFF);
            uint denominator = (uint)(v >> 32);
            return denominator == 0 ? 0 : (double)numerator / denominator;
        }
        return Rational(parts[0]) + Rational(parts[1]) / 60 + Rational(parts[2]) / 3600;
    }

    /// <summary>Bild laden (verkleinert auf decodeWidth, 0 = Originalgröße), richtig gedreht. Threadsicher (Freeze).</summary>
    public static BitmapSource? LoadImage(string path, int decodeWidth, int orientation)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;
            image.EndInit();
            image.Freeze();

            double angle = orientation switch { 3 => 180, 6 => 90, 8 => 270, _ => 0 };
            if (angle == 0) return image;
            var rotated = new TransformedBitmap(image, new RotateTransform(angle));
            rotated.Freeze();
            return rotated;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>URL des Fotos für die Karte (WebView2 bildet photos.local auf den Fotoordner ab).</summary>
    public string MapUrl(TourPhoto photo)
    {
        var relative = System.IO.Path.GetRelativePath(root, photo.Path);
        return "https://photos.local/" + string.Join("/", relative.Split('\\', '/').Select(Uri.EscapeDataString));
    }
}
