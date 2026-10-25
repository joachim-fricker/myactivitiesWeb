using System.IO;
using Microsoft.Web.WebView2.Core;

namespace MyActivities.Data;

/// <summary>Zentrale Objekte der App (einfacher Ersatz für Dependency Injection).</summary>
public static class AppServices
{
    public const string PhotoFolder = @"C:\Data\Images";

    /// <summary>%LOCALAPPDATA%\MyActivities</summary>
    public static string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyActivities");

    public static ActivityDatabase Db { get; } = new(Path.Combine(DataFolder, "activities.db"));

    public static GitHubClient GitHub { get; } = new();

    public static SyncService Sync { get; } = new(GitHub, Db);

    public static TrackService Tracks { get; } = new(GitHub, Path.Combine(DataFolder, "gpx"));

    public static PhotoService Photos { get; } = new(PhotoFolder);

    public static PowerService Power { get; } = new(Db, Tracks);

    public static HeatService Heat { get; } = new(Tracks);

    private static Task<CoreWebView2Environment>? _webViewEnvironment;

    /// <summary>Gemeinsame WebView2-Umgebung für alle Karten (Daten unter DataFolder\WebView2).</summary>
    public static Task<CoreWebView2Environment> WebViewEnvironment =>
        _webViewEnvironment ??= CoreWebView2Environment.CreateAsync(null, Path.Combine(DataFolder, "WebView2"));
}
