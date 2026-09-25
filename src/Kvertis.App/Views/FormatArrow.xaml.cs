using Kvertis.App.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kvertis.App.Views;

/// <summary>
/// Two format chips with an arrow between them (draft <c>.w5-fa</c>, <c>.w5-chip</c>, <c>.w5-pfeil</c>). The
/// source chip wears the kind colour (<c>Kv&lt;Kind&gt;Tint12</c>/<c>Frame40</c>), the arrow fills with the progress
/// and the target chip turns solid mint when the file is done. Brushes are looked up from the theme and set again
/// when the theme changes.
/// </summary>
public sealed partial class FormatArrow : UserControl
{
    public static readonly DependencyProperty SourceTextProperty = DependencyProperty.Register(
        nameof(SourceText), typeof(string), typeof(FormatArrow), new PropertyMetadata(string.Empty, OnChanged));

    public static readonly DependencyProperty TargetTextProperty = DependencyProperty.Register(
        nameof(TargetText), typeof(string), typeof(FormatArrow), new PropertyMetadata(string.Empty, OnChanged));

    public static readonly DependencyProperty KindBrushKeyProperty = DependencyProperty.Register(
        nameof(KindBrushKey), typeof(string), typeof(FormatArrow), new PropertyMetadata(string.Empty, OnChanged));

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(FormatArrow), new PropertyMetadata(0d, OnChanged));

    public static readonly DependencyProperty IsDoneProperty = DependencyProperty.Register(
        nameof(IsDone), typeof(bool), typeof(FormatArrow), new PropertyMetadata(false, OnChanged));

    public FormatArrow()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => Update();
        Update();
    }

    /// <summary>Short code of the source format ("HEIC").</summary>
    public string SourceText
    {
        get => (string)GetValue(SourceTextProperty);
        set => SetValue(SourceTextProperty, value);
    }

    /// <summary>Short code of the target format ("JPG").</summary>
    public string TargetText
    {
        get => (string)GetValue(TargetTextProperty);
        set => SetValue(TargetTextProperty, value);
    }

    /// <summary>Theme brush key of the file's kind, e.g. "KvImageBrush".</summary>
    public string KindBrushKey
    {
        get => (string)GetValue(KindBrushKeyProperty);
        set => SetValue(KindBrushKeyProperty, value);
    }

    /// <summary>Progress of the file, 0 to 100.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    /// <summary>True once the file is written.</summary>
    public bool IsDone
    {
        get => (bool)GetValue(IsDoneProperty);
        set => SetValue(IsDoneProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((FormatArrow)d).Update();

    private void Update()
    {
        SourceLabel.Text = SourceText ?? string.Empty;
        SourceLabel.Foreground = Ui.Brush(KindBrushKey);
        SourceChip.Background = Ui.KindVariant(KindBrushKey, "Tint12");
        SourceChip.BorderBrush = Ui.KindVariant(KindBrushKey, "Frame40");

        var done = IsDone;
        Track.Fill = Ui.Brush("KvLineStrongBrush");
        Fill.Fill = Ui.Brush("KvMintBrush");
        FillScale.ScaleX = done ? 1d : ConvertUi.Fraction(Progress);
        Head.Fill = Ui.Brush(done ? "KvMintBrush" : "KvLineStrongBrush");

        TargetLabel.Text = TargetText ?? string.Empty;
        TargetLabel.Foreground = Ui.Brush(done ? "KvOnMintBrush" : "KvMintBrush");
        TargetDash.Stroke = Ui.Brush("KvMintFrameBrush");
        TargetDash.Visibility = done ? Visibility.Collapsed : Visibility.Visible;
        TargetFill.Background = Ui.Brush("KvMintBrush");
        TargetFill.Visibility = done ? Visibility.Visible : Visibility.Collapsed;
    }
}
