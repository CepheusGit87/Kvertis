using Kvertis.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Core;
using Windows.Storage;

namespace Kvertis.App.Views;

/// <summary>Before/after preview. Images are read through streams; audio excerpts play in media elements.</summary>
public sealed partial class PreviewDialog : ContentDialog
{
    public PreviewDialog(PreviewViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
    }

    public PreviewViewModel ViewModel { get; }

    /// <summary>Makes the preview and fills the image or media elements.</summary>
    public async Task LoadAsync()
    {
        if (ViewModel.IsVisual)
        {
            BeforeImage.Source = await LoadImageAsync(ViewModel.BeforePath);
        }
        else if (ViewModel.IsAudio)
        {
            BeforePlayer.Source = await LoadMediaAsync(ViewModel.BeforePath);
        }

        await ViewModel.LoadAsync();
        if (ViewModel.AfterPath is not { } after)
        {
            return;
        }
        if (ViewModel.IsVisual)
        {
            AfterImage.Source = await LoadImageAsync(after);
        }
        else if (ViewModel.IsAudio)
        {
            AfterPlayer.Source = await LoadMediaAsync(after);
        }
    }

    /// <summary>Stops playback and releases the files so the temporary excerpt can be deleted.</summary>
    public void StopPlayback()
    {
        foreach (var player in new[] { BeforePlayer, AfterPlayer })
        {
            player.MediaPlayer?.Pause();
            if (player.Source is MediaSource source)
            {
                source.Dispose();
            }
            player.Source = null;
        }
    }

    private static async Task<ImageSource?> LoadImageAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }
        catch (Exception)
        {
            // Formats the system cannot display (for example RAW) simply show no picture.
            return null;
        }
    }

    private static async Task<MediaSource?> LoadMediaAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            return MediaSource.CreateFromStorageFile(file);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
