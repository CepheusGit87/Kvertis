using Kvertis.App.Services;
using Kvertis.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

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

        AppWindow.Resize(new Windows.Graphics.SizeInt32(InitialWidth, InitialHeight));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = MinWidth;
            presenter.PreferredMinimumHeight = MinHeight;
        }

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
