using System.Collections.Specialized;
using Kvertis.App.Helpers;
using Kvertis.App.Scenes;
using Kvertis.App.ViewModels;
using Kvertis.Engine.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace Kvertis.App.Views;

/// <summary>
/// The static galaxy: five ellipses, the hole and one dot per staged file, all at fixed positions. Used when
/// Windows asks for reduced motion and as the fallback after a drawing error (ADR-018).
/// </summary>
public sealed partial class GalaxyStaticView : UserControl
{
    private const double OrbitStrokeThickness = 1.5;
    private const double DotSize = 6;

    private MainViewModel? _viewModel;

    public GalaxyStaticView()
    {
        InitializeComponent();
        Unloaded += (_, _) => Attach(null);
    }

    /// <summary>The staged files whose dots are shown; setting it redraws the surface.</summary>
    public MainViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            Attach(value);
            Redraw();
        }
    }

    private void Attach(MainViewModel? value)
    {
        if (_viewModel is { } old)
        {
            old.Jobs.CollectionChanged -= OnJobsChanged;
            old.StagedChanged -= OnStagedChanged;
        }

        _viewModel = value;
        if (value is not null)
        {
            value.Jobs.CollectionChanged += OnJobsChanged;
            // A file only gets its kind when detection is done; without this the dot would never appear.
            value.StagedChanged += OnStagedChanged;
        }
    }

    private void OnJobsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Redraw();

    private void OnStagedChanged(object? sender, EventArgs e) => Redraw();

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        Surface.Children.Clear();
        var width = (float)ActualWidth;
        var height = (float)ActualHeight;
        if (width < 8f || height < 8f)
        {
            return;
        }

        // Exactly the geometry the moving scene uses, so switching the setting does not move anything.
        var layout = new GalaxyLayout(width, height);
        for (var k = 0; k < GalaxyLayout.OrbitCount; k++)
        {
            var radiusX = layout.BaseRadius(k);
            var radiusY = radiusX * GalaxyLayout.FlattenOverview;
            var ellipse = new Ellipse
            {
                Width = radiusX * 2,
                Height = radiusY * 2,
                StrokeThickness = OrbitStrokeThickness,
                Stroke = Ui.Brush(BrushKeyOf(GalaxyLayout.KindOf(k))),
                Opacity = 0.5,
            };
            Canvas.SetLeft(ellipse, layout.Center.X - radiusX);
            Canvas.SetTop(ellipse, layout.Center.Y - radiusY);
            Surface.Children.Add(ellipse);
        }

        var holeRadius = GalaxyLayout.HoleRadiusOverview * layout.Scale;
        var hole = new Ellipse
        {
            Width = holeRadius * 2,
            Height = holeRadius * 2,
            Fill = Ui.Brush("KvBackgroundBrush"),
            Stroke = Ui.Brush("KvMintBrush"),
            StrokeThickness = 2,
        };
        Canvas.SetLeft(hole, layout.Center.X - holeRadius);
        Canvas.SetTop(hole, layout.Center.Y - holeRadius);
        Surface.Children.Add(hole);

        if (_viewModel is not { } viewModel)
        {
            return;
        }

        var ordinal = 0;
        foreach (var job in viewModel.Jobs)
        {
            var kind = job.Kind;
            if (kind == MediaKind.Unknown || job.IsRejected)
            {
                ordinal++;
                continue;
            }

            var orbit = GalaxyLayout.OrbitOf(kind);
            var angle = ordinal * GalaxyScene.AngleStep;
            var radiusX = layout.BaseRadius(orbit);
            var radiusY = radiusX * GalaxyLayout.FlattenOverview;
            var dot = new Ellipse
            {
                Width = DotSize,
                Height = DotSize,
                Fill = Ui.Brush(BrushKeyOf(kind)),
            };
            Canvas.SetLeft(dot, layout.Center.X + radiusX * MathF.Cos(angle) - DotSize / 2);
            Canvas.SetTop(dot, layout.Center.Y + radiusY * MathF.Sin(angle) - DotSize / 2);
            Surface.Children.Add(dot);
            ordinal++;
        }
    }

    internal static string BrushKeyOf(MediaKind kind) => kind switch
    {
        MediaKind.Image => "KvImageBrush",
        MediaKind.Audio => "KvAudioBrush",
        MediaKind.Video => "KvVideoBrush",
        MediaKind.Document => "KvDocumentBrush",
        MediaKind.Model3D => "KvModelBrush",
        _ => "KvErrorBrush",
    };
}
