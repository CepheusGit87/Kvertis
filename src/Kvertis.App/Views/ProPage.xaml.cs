using Kvertis.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Kvertis.App.Views;

/// <summary>Kvertis Pro: what it unlocks, buy and restore (Store purchase only).</summary>
public sealed partial class ProPage : Page
{
    public ProPage()
    {
        ViewModel = App.Services.GetRequiredService<ProViewModel>();
        InitializeComponent();
    }

    public ProViewModel ViewModel { get; }
}
