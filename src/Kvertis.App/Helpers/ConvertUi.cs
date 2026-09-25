using Microsoft.UI.Xaml.Media;

namespace Kvertis.App.Helpers;

/// <summary>
/// x:Bind functions of the list in step 3 (draft `.w5-zeile`): the frame and text colours per row state. The
/// brushes come from the theme (KvertisColors.xaml), so high contrast gets the system colours.
/// </summary>
public static class ConvertUi
{
    /// <summary>Row frame: mint frame while running, coral frame after a failure, the plain line otherwise.</summary>
    public static Brush RowFrame(bool running, bool failed) =>
        Ui.Brush(failed ? "KvErrorFrameBrush" : running ? "KvMintFrameBrush" : "KvLineBrush");

    /// <summary>Status column (.w5-st): mint when done, coral after a failure, muted otherwise.</summary>
    public static Brush StatusBrush(bool done, bool failed) =>
        Ui.Brush(done ? "KvMintBrush" : failed ? "KvErrorBrush" : "KvMutedBrush");

    /// <summary>The file name of the target path (.w5-pfad b): mint once written.</summary>
    public static Brush FileBrush(bool done) => Ui.Brush(done ? "KvMintBrush" : "KvInkBrush");

    /// <summary>The tag above the path (.w5-tag): mint for a file with its own target.</summary>
    public static Brush TagBrush(bool own) => Ui.Brush(own ? "KvMintBrush" : "KvMutedBrush");

    /// <summary>A progress value of 0 to 100 as the scale of the progress line (0 to 1).</summary>
    public static double Fraction(double percent) => Math.Clamp(percent / 100d, 0d, 1d);
}
