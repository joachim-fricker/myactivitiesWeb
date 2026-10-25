using System.IO;
using System.Net.Http;
using System.Xml;
using MyActivities.Domain;

namespace MyActivities.Data;

/// <summary>Berechnet die geschätzte Leistung am Berg für alle Radtouren, die noch keine haben.</summary>
public sealed class PowerService(ActivityDatabase db, TrackService tracks)
{
    private Task? _running;

    /// <summary>Läuft schon eine Berechnung, wird sie nicht doppelt gestartet.</summary>
    public Task BackfillAsync(IProgress<(int Done, int Total)>? progress, Action<long, double?[]> onResult) =>
        _running is { IsCompleted: false } ? _running : _running = RunAsync(progress, onResult);

    private async Task RunAsync(IProgress<(int Done, int Total)>? progress, Action<long, double?[]> onResult)
    {
        var todo = db.MissingPower(Stats.CyclingTypes);
        if (todo.Count == 0) return;
        int done = 0;
        await Parallel.ForEachAsync(todo, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (t, ct) =>
        {
            try
            {
                double?[] watts;
                if (Stats.IsIndoor(t.TypeKey, t.Name))
                {
                    watts = [null, null, null];          // virtuelle Höhen: nicht aussagekräftig
                }
                else
                {
                    var track = await tracks.LoadAsync(t.Id, ct);
                    watts = Power.BestPowers(track.Timeline, t.TypeKey);
                }
                db.UpdatePower(t.Id, watts);
                onResult(t.Id, watts);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is XmlException ||
                                       ex is TaskCanceledException)
            {
                // bleibt offen und wird beim nächsten Mal erneut versucht
            }
            progress?.Report((Interlocked.Increment(ref done), todo.Count));
        });
    }
}
