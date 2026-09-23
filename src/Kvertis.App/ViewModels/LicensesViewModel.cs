using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kvertis.App.Services;

namespace Kvertis.App.ViewModels;

/// <summary>Third-party licenses page. Texts are read from the package, never downloaded.</summary>
public sealed partial class LicensesViewModel : ObservableObject
{
    private readonly ThirdPartyLicensesProvider _provider;
    private bool _loaded;

    public LicensesViewModel(ThirdPartyLicensesProvider provider)
    {
        _provider = provider;
    }

    public ObservableCollection<ThirdPartyLicense> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedText))]
    private ThirdPartyLicense? selectedItem;

    [ObservableProperty]
    private bool isEmpty;

    public string SelectedText => SelectedItem?.Text ?? string.Empty;

    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;
        foreach (var item in await _provider.LoadAsync())
        {
            Items.Add(item);
        }
        IsEmpty = Items.Count == 0;
        SelectedItem = Items.FirstOrDefault();
    }
}
