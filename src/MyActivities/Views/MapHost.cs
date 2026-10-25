using System.IO;
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

        var loaded = new TaskCompletionSource();
        webView.NavigationCompleted += (_, _) => loaded.TrySetResult();
        webView.Source = new Uri("https://app.local/map.html");
        await loaded.Task;
    }
}
