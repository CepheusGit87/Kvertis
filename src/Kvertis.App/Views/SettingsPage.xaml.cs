using Kvertis.App.Services;
using Kvertis.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kvertis.App.Views;

/// <summary>Settings (docs/06-design.md, section 6).</summary>
public sealed partial class SettingsPage : Page
{
    private readonly INavigationService _navigation;

    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        Pro = App.Services.GetRequiredService<ProViewModel>();
        _navigation = App.Services.GetRequiredService<INavigationService>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public SettingsViewModel ViewModel { get; }

    public ProViewModel Pro { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ViewModel.RefreshFfmpegStatusAsync();

    private void OnProClick(object sender, RoutedEventArgs e) => _navigation.Navigate(AppPage.Pro);

    private void OnAboutClick(object sender, RoutedEventArgs e) => _navigation.Navigate(AppPage.About);

    private void OnLicensesClick(object sender, RoutedEventArgs e) => _navigation.Navigate(AppPage.Licenses);
}
