using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using MyActivities.Data;

namespace MyActivities.Views;

public partial class EinstellungenPage : Page
{
    public EinstellungenPage()
    {
        InitializeComponent();
        PhotoText.Text = $"Fotoordner: {AppServices.PhotoFolder}";
        DbText.Text = $"Datenbank: {AppServices.Db.FilePath}";
        Loaded += (_, _) => UpdateCount();
    }

    private void UpdateCount() =>
        CountText.Text = $"{AppServices.Db.Count()} Aktivitäten in der lokalen Datenbank";

    private async void OnSyncClick(object sender, RoutedEventArgs e)
    {
        SyncButton.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        Progress.IsIndeterminate = true;
        StatusText.Text = "Lese ids.txt …";

        var progress = new Progress<(int Done, int Total)>(p =>
        {
            Progress.IsIndeterminate = false;
            Progress.Maximum = p.Total;
            Progress.Value = p.Done;
            StatusText.Text = $"Lade Aktivitäten: {p.Done} / {p.Total}";
        });

        try
        {
            var r = await AppServices.Sync.SyncAsync(progress);
            StatusText.Text = r.Imported == 0 && r.Failed == 0
                ? $"Alles aktuell ({r.RemoteTotal} Aktivitäten auf GitHub)."
                : $"{r.Imported} neue Aktivitäten geladen" +
                  (r.Failed > 0 ? $", {r.Failed} fehlgeschlagen (nächstes Mal erneut)." : ".");
        }
        catch (HttpRequestException ex)
        {
            StatusText.Text = $"Fehler beim Laden von GitHub: {ex.Message}";
        }
        catch (TaskCanceledException)
        {
            StatusText.Text = "Zeitüberschreitung beim Laden von GitHub.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Fehler: {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            Progress.Visibility = Visibility.Collapsed;
            SyncButton.IsEnabled = true;
            UpdateCount();
        }
    }
}
