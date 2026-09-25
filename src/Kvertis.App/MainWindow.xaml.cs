using Kvertis.App.Helpers;
using Kvertis.App.Services;
using Kvertis.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Kvertis.App;

/// <summary>The only window: Mica backdrop, custom title bar, a frame for the pages.</summary>
public sealed partial class MainWindow : Window
{
    private const int MinWidth = 640;
    private const int MinHeight = 480;
    private const int InitialWidth = 1000;
    private const int InitialHeight = 720;

    private readonly FrameNavigationService _navigation;
    private readonly HistoryViewModel _history;
    private readonly ProViewModel _pro;

    /// <summary>Last bounds seen while the window was neither maximized nor minimized, in physical pixels.</summary>
    private RectInt32 _restoredBounds;
    private bool _isMaximized;

    public MainWindow()
    {
        InitializeComponent();

        var loc = App.Services.GetRequiredService<ILocalizer>();
        _navigation = App.Services.GetRequiredService<FrameNavigationService>();
        _history = App.Services.GetRequiredService<HistoryViewModel>();
        _pro = App.Services.GetRequiredService<ProViewModel>();

        Title = loc.Get("Window_Title");
        // Mica on Windows 11; on Windows 10 the backdrop is not supported and the theme background shows instead.
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = MinWidth;
            presenter.PreferredMinimumHeight = MinHeight;
        }

        // Reopen where the window stood last time, but only if that spot is still on a connected display.
        var placement = App.Services.GetRequiredService<ISettingsService>().Current.WindowPlacement;
        if (placement is not null && WindowPlacementHelper.TryApply(AppWindow, placement, MinWidth, MinHeight))
        {
            _restoredBounds = new RectInt32(
                placement.X,
                placement.Y,
                Math.Max(placement.Width, MinWidth),
                Math.Max(placement.Height, MinHeight));
            _isMaximized = placement.IsMaximized;
        }
        else
        {
            AppWindow.Resize(new SizeInt32(InitialWidth, InitialHeight));
            _restoredBounds = new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        }
        AppWindow.Changed += OnAppWindowChanged;

        _navigation.Attach(ContentFrame);
        _navigation.Navigated += (_, _) => BackButton.Visibility = _navigation.CanGoBack ? Visibility.Visible : Visibility.Collapsed;
        _pro.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProViewModel.StatusText))
            {
                ProStatusText.Text = _pro.StatusText;
            }
        };
        ProStatusText.Text = _pro.StatusText;
        RootGrid.Loaded += (_, _) => UpdateCaptionInset();
        _navigation.Navigate(AppPage.Main);
    }

    /// <summary>Where the window stands now, ready to be stored. Reads no window state, so it is safe while closing.</summary>
    public WindowPlacement? CurrentPlacement => WindowPlacementHelper.Capture(_restoredBounds, _isMaximized);

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPositionChange && !args.DidSizeChange && !args.DidPresenterChange)
        {
            return;
        }
        if (sender.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }
        _isMaximized = presenter.State == OverlappedPresenterState.Maximized;
        // A minimized or maximized window reports bounds we must not store; keep the last normal ones.
        if (presenter.State == OverlappedPresenterState.Restored && sender.Size.Width > 0 && sender.Size.Height > 0)
        {
            _restoredBounds = new RectInt32(sender.Position.X, sender.Position.Y, sender.Size.Width, sender.Size.Height);
        }
    }

    private void UpdateCaptionInset()
    {
        // Leave room for the system caption buttons (minimize, maximize, close), scaled for the monitor DPI.
        var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
        var inset = AppWindow.TitleBar.RightInset / scale;
        if (inset > 0)
        {
            CaptionButtonsColumn.Width = new GridLength(inset);
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => _navigation.GoBack();

    private void OnHistoryClick(object sender, RoutedEventArgs e)
    {
        _navigation.Navigate(AppPage.Main);
        _history.IsOpen = !_history.IsOpen;
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => _navigation.Navigate(AppPage.Settings);

    private void OnProClick(object sender, RoutedEventArgs e) => _navigation.Navigate(AppPage.Pro);
}
