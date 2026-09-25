using Kvertis.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kvertis.App.Views;

/// <summary>About Kvertis: version, privacy statement, link to the licenses.</summary>
public sealed partial class AboutPage : Page
{
    private readonly INavigationService _navigation;

    public AboutPage()
    {
        _navigation = App.Services.GetRequiredService<INavigationService>();
        var loc = App.Services.GetRequiredService<ILocalizer>();
        VersionText = loc.Format("About_Version_Text", PackageInfo.VersionText);
        InitializeComponent();
    }

    public string VersionText { get; }

    private void OnLicensesClick(object sender, RoutedEventArgs e) => _navigation.Navigate(AppPage.Licenses);
}
