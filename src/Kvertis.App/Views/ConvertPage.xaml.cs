using Kvertis.App.Helpers;
using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kvertis.App.Views;

/// <summary>
/// Step 3 "Umwandeln". Still a placeholder for the swirl and the closing sequence; it already reports what
/// step 2 planned, so the hand-over through <see cref="IWorkflowSession"/> is visible.
/// </summary>
public sealed partial class ConvertPage : Page
{
    private readonly IWorkflowSession _session;
    private readonly IEstimator _estimator;
    private readonly ILocalizer _loc;

    public ConvertPage()
    {
        _session = App.Services.GetRequiredService<IWorkflowSession>();
        _estimator = App.Services.GetRequiredService<IEstimator>();
        _loc = App.Services.GetRequiredService<ILocalizer>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        PlanText.Text = Describe();
    }

    private string Describe()
    {
        if (_session.Plan is not { Items.Count: > 0 } plan)
        {
            return _loc.Get("Convert_NoPlan_Text");
        }
        long total = 0;
        foreach (var item in plan.Items)
        {
            try
            {
                total += _estimator.Estimate(item.Input, item.Settings).OutputBytes;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                total += item.Input.SizeBytes;
            }
        }
        return _loc.Format("Convert_Plan_Text", plan.Items.Count, Formatting.Bytes(_loc, total));
    }
}
