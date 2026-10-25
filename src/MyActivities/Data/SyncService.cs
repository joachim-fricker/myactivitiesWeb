using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace MyActivities.Data;

public sealed record SyncResult(int RemoteTotal, int Imported, int Failed);

/// <summary>Liest ids.txt und lädt nur die Aktivitäten, die lokal noch fehlen.</summary>
public sealed class SyncService(GitHubClient client, ActivityDatabase db)
{
    public async Task<SyncResult> SyncAsync(IProgress<(int Done, int Total)>? progress = null,
                                            CancellationToken ct = default)
    {
        var remote = (await client.FetchIdsAsync(ct)).ToHashSet();
        var local = db.AllIds();
        var missing = remote.Except(local).ToList();
        if (missing.Count == 0) return new SyncResult(remote.Count, 0, 0);

        var loaded = new ConcurrentBag<ActivityDto>();
        var errors = new ConcurrentBag<string>();
        int done = 0, failed = 0;

        var options = new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct };
        await Parallel.ForEachAsync(missing, options, async (id, token) =>
        {
            try
            {
                loaded.Add(await client.FetchActivityAsync(id, token));
            }
            catch (Exception e) when (!token.IsCancellationRequested)
            {
                Interlocked.Increment(ref failed);   // wird beim nächsten Sync erneut versucht
                errors.Add($"{id}: {e.GetType().Name}: {e.Message}");
            }
            progress?.Report((Interlocked.Increment(ref done), missing.Count));
        });

        db.Upsert(loaded);
        WriteLog(errors);
        return new SyncResult(remote.Count, loaded.Count, failed);
    }

    /// <summary>Fehler des letzten Syncs in sync.log neben der Datenbank.</summary>
    private void WriteLog(IEnumerable<string> errors)
    {
        var path = Path.Combine(Path.GetDirectoryName(db.FilePath)!, "sync.log");
        File.WriteAllLines(path, errors.Order().Prepend($"Sync {DateTime.Now:yyyy-MM-dd HH:mm:ss}"));
    }
}
