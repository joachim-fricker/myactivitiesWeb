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
    public static int? PhotoIndex(CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
            return doc.RootElement.TryGetProperty("photo", out var value) ? value.GetInt32() : null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
