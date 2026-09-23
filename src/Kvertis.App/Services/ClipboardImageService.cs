using System.Globalization;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Kvertis.App.Services;

/// <summary>What Ctrl+V delivered: file paths, and whether they are temporary copies made by Kvertis.</summary>
public sealed record ClipboardPaste(IReadOnlyList<string> Paths, bool IsTemporary)
{
    public static readonly ClipboardPaste Empty = new([], false);
}

/// <summary>
/// Ctrl+V: files copied in Explorer are taken as they are; a bitmap is written to a temporary PNG in the
/// local cache folder so it can go through the normal pipeline. Nothing leaves the device.
/// </summary>
public sealed class ClipboardImageService
{
    public async Task<ClipboardPaste> GetPasteAsync()
    {
        var content = Clipboard.GetContent();
        if (content is null)
        {
            return ClipboardPaste.Empty;
        }

        if (content.Contains(StandardDataFormats.StorageItems))
        {
            var items = await content.GetStorageItemsAsync();
            var paths = items.OfType<IStorageFile>().Select(f => f.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
            if (paths.Count > 0)
            {
                return new ClipboardPaste(paths, false);
            }
        }

        if (content.Contains(StandardDataFormats.Bitmap))
        {
            var reference = await content.GetBitmapAsync();
            var path = await SaveAsPngAsync(reference);
            return new ClipboardPaste([path], true);
        }

        return ClipboardPaste.Empty;
    }

    /// <summary>Deletes temporary clipboard images older than a day (called on start).</summary>
    public static void CleanupOldFiles()
    {
        try
        {
            var folder = AppPaths.ClipboardFolder;
            if (!Directory.Exists(folder))
            {
                return;
            }
            var limit = DateTime.UtcNow.AddDays(-1);
            foreach (var file in Directory.EnumerateFiles(folder, "*.png"))
            {
                if (File.GetLastWriteTimeUtc(file) < limit)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort; the files live in the app's own cache folder.
        }
    }

    private static async Task<string> SaveAsPngAsync(RandomAccessStreamReference reference)
    {
        Directory.CreateDirectory(AppPaths.ClipboardFolder);
        var folder = await StorageFolder.GetFolderFromPathAsync(AppPaths.ClipboardFolder);
        var name = "clipboard_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + ".png";
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.GenerateUniqueName);

        using (var input = await reference.OpenReadAsync())
        {
            var decoder = await BitmapDecoder.CreateAsync(input);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            using var output = await file.OpenAsync(FileAccessMode.ReadWrite);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();
        }
        return file.Path;
    }
}
