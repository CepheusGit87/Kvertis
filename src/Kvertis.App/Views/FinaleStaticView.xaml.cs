using System.ComponentModel;
using Kvertis.App.Helpers;
using Kvertis.App.Services;
using Kvertis.App.ViewModels.Convert;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Kvertis.App.Views;

/// <summary>
/// The static ring of the finale (worksheet "Reduced Motion"): the ellipse of the animated finale (Rx =
/// min(0.44·W, 440), Ry = 116 around (W/2, 150)), an 8 px dot per file in its kind colour (failed: coral
/// ring), sorted like the animated ring, and the check mark at (W/2, 96). Nothing moves. Used for reduced
/// motion and as the fallback after a drawing error.
/// </summary>
public sealed partial class FinaleStaticView : UserControl
{
    private const double DotSize = 8;
    private const double RingRadiusY = 116;
    private const double RingCentreY = 150;
    private const double CheckCentreY = 96;

    private readonly ILocalizer _loc;
    private ConvertPageViewModel? _viewModel;

    public FinaleStaticView()
    {
        _loc = App.Services.GetRequiredService<ILocalizer>();
        InitializeComponent();
        Unloaded += (_, _) => Attach(null);
    }

    /// <summary>The round whose files are shown; setting it redraws the ring.</summary>
    public ConvertPageViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            Attach(value);
            Redraw();
        }
    }

    private void Attach(ConvertPageViewModel? value)
    {
        if (_viewModel is { } old)
        {
            old.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = value;
        if (value is not null)
        {
            value.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConvertPageViewModel.State) or nameof(ConvertPageViewModel.HasReport))
        {
            Redraw();
        }
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        Surface.Children.Clear();
        var width = Root.ActualWidth;
        if (_viewModel is not { IsFinished: true } vm || width <= 0 || vm.Rows.Count == 0)
        {
            CheckBadge.Visibility = Visibility.Collapsed;
            AutomationProperties.SetName(Root, string.Empty);
            return;
        }

        var files = vm.Rows
            .Where(r => r.IsDone || r.IsFailed)
            .OrderBy(r => KindRank(r.Item.Input.Kind))
            .ThenBy(r => vm.Rows.IndexOf(r))
            .ToList();
        var rx = Math.Min(width * 0.44, 440);
        var cx = width / 2;

        var ring = new Ellipse
        {
            Width = rx * 2,
            Height = RingRadiusY * 2,
            Stroke = (Brush)Application.Current.Resources["KvMintBrush"],
            StrokeThickness = 1,
            Opacity = 0.22,
        };
        Canvas.SetLeft(ring, cx - rx);
        Canvas.SetTop(ring, RingCentreY - RingRadiusY);
        Surface.Children.Add(ring);

        for (var o = 0; o < files.Count; o++)
        {
            var row = files[o];
            var w = -Math.PI / 2 + (double)o / files.Count * Math.PI * 2;
            var x = cx + Math.Cos(w) * rx;
            var y = RingCentreY + Math.Sin(w) * RingRadiusY;
            var brush = Ui.Brush(row.IsFailed ? "KvErrorBrush" : row.ColorBrushKey);
            var dot = new Ellipse { Width = DotSize, Height = DotSize };
            if (row.IsFailed)
            {
                dot.Stroke = brush;
                dot.StrokeThickness = 1.5;
            }
            else
            {
                dot.Fill = brush;
            }

            Canvas.SetLeft(dot, x - DotSize / 2);
            Canvas.SetTop(dot, y - DotSize / 2);
            Surface.Children.Add(dot);
        }

        CheckBadge.Margin = new Thickness(0, CheckCentreY - CheckBadge.Height / 2, 0, 0);
        CheckBadge.Visibility = Visibility.Visible;
        AutomationProperties.SetName(CheckBadge, _loc.Get("Finale_Check_AutomationName"));
        AutomationProperties.SetName(
            Root,
            _loc.Format("Finale_Ring_AutomationName", files.Count(r => r.IsDone), vm.Rows.Count));
    }

    private static int KindRank(Engine.Abstractions.MediaKind kind)
    {
        for (var i = 0; i < TargetPlanner.KindOrder.Count; i++)
        {
            if (TargetPlanner.KindOrder[i] == kind)
            {
                return i;
            }
        }

        return TargetPlanner.KindOrder.Count;
    }
}
