using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace MyActivities.Data;

/// <summary>Lädt ids.txt und Aktivitäts-JSONs aus dem öffentlichen GitHub-Repo.</summary>
public sealed class GitHubClient
{
    private const string RawBase =
        "https://raw.githubusercontent.com/joachim-fricker/myactivities/main/activities";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<List<long>> FetchIdsAsync(CancellationToken ct = default)
    {
        // Zeitstempel umgeht den CDN-Cache von raw.githubusercontent.com
        var url = $"{RawBase}/ids.txt?nocache={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var text = await Http.GetStringAsync(url, ct);
        var ids = new List<long>();
        foreach (var line in text.Split('\n'))
        {
            if (long.TryParse(line.Trim(), out var id)) ids.Add(id);
        }
        return ids;
    }

    /// <summary>GPX als Text; null, wenn es keine gibt (HTTP 404, z. B. Aktivität ohne GPS).</summary>
    public async Task<string?> FetchGpxAsync(long id, CancellationToken ct = default)
    {
        using var response = await Http.GetAsync($"{RawBase}/{id}.gpx", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    public async Task<ActivityDto> FetchActivityAsync(long id, CancellationToken ct = default)
    {
        await using var stream = await Http.GetStreamAsync($"{RawBase}/{id}.json", ct);
        return await JsonSerializer.DeserializeAsync<ActivityDto>(stream, JsonOptions, ct)
               ?? throw new InvalidDataException($"Leeres JSON bei {id}");
    }
}
