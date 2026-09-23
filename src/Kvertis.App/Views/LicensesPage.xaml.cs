using Kvertis.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kvertis.App.Views;

/// <summary>Third-party licenses, read from the packaged ThirdParty folder.</summary>
public sealed partial class LicensesPage : Page
{
    public LicensesPage()
    {
        ViewModel = App.Services.GetRequiredService<LicensesViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public LicensesViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ViewModel.LoadAsync();
}
