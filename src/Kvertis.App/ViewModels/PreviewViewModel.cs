using CommunityToolkit.Mvvm.ComponentModel;
using Kvertis.App.Helpers;
using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;
using Microsoft.Extensions.Logging;

namespace Kvertis.App.ViewModels;

/// <summary>
/// Before/after preview of one card (docs/06-design.md, "Vorschau-Dialog"). The converter writes a reduced
/// image or a short audio excerpt into a temporary file, which is deleted when the dialog closes.
/// </summary>
public sealed partial class PreviewViewModel : ObservableObject, IDisposable
{
    private readonly IConverterResolver _resolver;
    private readonly ErrorMessageMapper _errors;
    private readonly ILocalizer _loc;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();

    public PreviewViewModel(JobItemViewModel item, IConverterResolver resolver, ErrorMessageMapper errors, ILocalizer loc, ILogger logger)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        _resolver = resolver;
        _errors = errors;
        _loc = loc;
        _logger = logger;
        Title = loc.Format("Preview_Title_Text", item.FileName);
    }

    public JobItemViewModel Item { get; }

    public string Title { get; }

    public string BeforePath => Item.FilePath;

    public bool IsAudio => Item.Kind == MediaKind.Audio;

    public bool IsVisual => Item.Kind is MediaKind.Image or MediaKind.Video;

    [ObservableProperty]
    private bool isLoading = true;

    [ObservableProperty]
    private string? afterPath;

    [ObservableProperty]
    private string summaryText = string.Empty;

    [ObservableProperty]
    private string errorText = string.Empty;

    public async Task LoadAsync()
    {
        try
        {
            var input = Item.Input;
            if (input is null || Item.SelectedFormat is null)
            {
                ErrorText = _loc.Get("Preview_Unavailable_Text");
                return;
            }
            var settings = Item.BuildSettings();
            var converter = _resolver.Resolve(input, settings.Output);
            var result = converter is null ? null : await converter.PreviewAsync(input, settings, _cts.Token);
            if (result is null)
            {
                ErrorText = _loc.Get("Preview_Unavailable_Text");
                return;
            }
            AfterPath = result.PreviewPath;
            SummaryText = _loc.Format("Preview_Summary_Text",
                Formatting.Bytes(_loc, input.SizeBytes),
                Formatting.Bytes(_loc, result.EstimatedOutputBytes),
                Formatting.Change(_loc, input.SizeBytes, result.EstimatedOutputBytes));
        }
        catch (OperationCanceledException)
        {
            // Dialog closed while the preview was being made.
        }
        catch (ConversionException ex)
        {
            ErrorText = _errors.Map(ex.Code).Title;
            _logger.LogInformation(ex, "Preview failed with {Code}", ex.Code);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Stops a preview that is still being made (dialog closed).</summary>
    public void Cancel() => _cts.Cancel();

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        var path = AfterPath;
        if (path is not null)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The file may still be open in the media player; the temp folder is cleaned by Windows.
            }
        }
    }
}
