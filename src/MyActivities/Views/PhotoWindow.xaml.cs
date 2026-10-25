using System.Windows;
using System.Windows.Input;
using MyActivities.Data;
using Wpf.Ui.Controls;

namespace MyActivities.Views;

/// <summary>Bildbetrachter: Pfeiltasten blättern, Esc schließt.</summary>
public partial class PhotoWindow : FluentWindow
{
    private readonly IReadOnlyList<TourPhoto> _photos;
    private readonly string _tourName;
    private int _index;
    private int _loadRequest;

    public PhotoWindow(IReadOnlyList<TourPhoto> photos, int index, string tourName)
    {
        InitializeComponent();
        WindowBounds.Apply(this, "photo", widthFraction: 0.6, heightFraction: 0.75);
        _photos = photos;
        _index = index;
        _tourName = tourName;
        Loaded += async (_, _) => await ShowCurrentAsync();
        PreviewKeyDown += async (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Escape: Close(); break;
                case Key.Left: await MoveAsync(-1); e.Handled = true; break;
                case Key.Right: await MoveAsync(+1); e.Handled = true; break;
            }
        };
    }

    private async Task MoveAsync(int delta)
    {
        int next = _index + delta;
        if (next < 0 || next >= _photos.Count) return;
        _index = next;
        await ShowCurrentAsync();
    }

    private async Task ShowCurrentAsync()
    {
        var photo = _photos[_index];
        int request = ++_loadRequest;
        Title = $"{_tourName} – Foto {_index + 1} / {_photos.Count}";
        AppTitleBar.Title = Title;
        Caption.Text = photo.TourName is null
            ? $"{photo.Taken:dd.MM.yyyy HH:mm:ss} · {System.IO.Path.GetFileName(photo.Path)}"
            : $"{photo.TourName} · {photo.Taken:HH:mm} · {System.IO.Path.GetFileName(photo.Path)}";
        PrevButton.IsEnabled = _index > 0;
        NextButton.IsEnabled = _index < _photos.Count - 1;

        Picture.Source = photo.Thumb;   // sofort die Vorschau, dann das große Bild
        Busy.Visibility = Visibility.Visible;
        var image = await Task.Run(() =>
        {
            PhotoService.ReadMetadata(photo);
            return PhotoService.LoadImage(photo.Path, 2560, photo.Orientation);
        });
        if (request != _loadRequest) return;
        Busy.Visibility = Visibility.Collapsed;
        if (image is not null) Picture.Source = image;
    }

    private async void OnPrev(object sender, RoutedEventArgs e) => await MoveAsync(-1);

    private async void OnNext(object sender, RoutedEventArgs e) => await MoveAsync(+1);

    private void OnShowInExplorer(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_photos[_index].Path}\"");
}
