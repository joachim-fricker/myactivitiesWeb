using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MyActivities.Data;

namespace MyActivities.Views;

/// <summary>Lädt die Leaflet-Karte (Assets/map.html) in ein WebView2.</summary>
public static class MapHost
{
    public static async Task InitAsync(WebView2 webView)
    {
        var env = await AppServices.WebViewEnvironment;
        await webView.EnsureCoreWebView2Async(env);
        // map.html über eine https-Adresse ausliefern (Kartenserver verlangen einen Referer)
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "app.local", Path.Combine(AppContext.BaseDirectory, "Assets"), CoreWebView2HostResourceAccessKind.Allow);
        // Fotos für die Bildchen auf der Karte
        if (Directory.Exists(AppServices.PhotoFolder))
        {
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "photos.local", AppServices.PhotoFolder, CoreWebView2HostResourceAccessKind.Allow);
        }

        var loaded = new TaskCompletionSource();
        webView.NavigationCompleted += (_, _) => loaded.TrySetResult();
        webView.Source = new Uri("https://app.local/map.html");
        await loaded.Task;
    }

    /// <summary>Index des angeklickten Fotos aus einer Nachricht von map.html, sonst null.</summary>
    public static int? PhotoIndex(CoreWebView2WebMessageReceivedEventArgs e) =>
        MessageValue(e, "photo") is long v ? (int)v : null;

    /// <summary>ID der Tour, deren Details angezeigt werden sollen (Link im Popup), sonst null.</summary>
    public static long? DetailsId(CoreWebView2WebMessageReceivedEventArgs e) => MessageValue(e, "details");

    /// <summary>ID der angeklickten Tour (Weltkarte), sonst null.</summary>
    public static long? SelectedId(CoreWebView2WebMessageReceivedEventArgs e) => MessageValue(e, "select");

    /// <summary>Kartenausschnitt der Weltkarte: Touren im Ausschnitt, davon nicht geöffnet, Zoomstufe.</summary>
    public static (int Count, int Closed, double Zoom)? ViewInfo(CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
            if (!doc.RootElement.TryGetProperty("view", out var v)) return null;
            return (v.GetProperty("count").GetInt32(), v.GetProperty("closed").GetInt32(), v.GetProperty("zoom").GetDouble());
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Heatmap: IDs der Touren, die am angeklickten Punkt vorbeiführen; tooFar = zu weit herausgezoomt.</summary>
    public static (List<long> Ids, bool TooFar)? NearIds(CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
            if (!doc.RootElement.TryGetProperty("near", out var near)) return null;
            var ids = near.EnumerateArray()
                .Select(v => long.TryParse(v.GetString(), out var id) ? id : 0)
                .Where(id => id != 0)
                .ToList();
            bool tooFar = doc.RootElement.TryGetProperty("tooFar", out var tf) && tf.GetBoolean();
            return (ids, tooFar);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Zahl unter key aus einer JSON-Nachricht von map.html, z. B. {"photo": 3}.</summary>
    private static long? MessageValue(CoreWebView2WebMessageReceivedEventArgs e, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
            if (!doc.RootElement.TryGetProperty(key, out var value)) return null;
            return value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetInt64(out var l) => l,
                JsonValueKind.Number => (long)Math.Round(value.GetDouble()),   // z. B. 1.7774635432E10
                JsonValueKind.String when long.TryParse(value.GetString(), out var s) => s,
                _ => null,
            };
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
