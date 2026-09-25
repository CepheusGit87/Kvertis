using System.Globalization;
using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Estimation;
using Kvertis.Engine.Formats;
using NSubstitute;

namespace Kvertis.App.Tests;

/// <summary>Fakes the step 2 view models need. No WinUI, nothing asynchronous.</summary>
internal static class TestDoubles
{
    /// <summary>Returns the resource key itself, with the arguments appended, so texts stay checkable.</summary>
    public static ILocalizer Localizer()
    {
        var loc = Substitute.For<ILocalizer>();
        loc.Get(Arg.Any<string>()).Returns(c => c.Arg<string>());
        loc.Format(Arg.Any<string>(), Arg.Any<object?[]>()).Returns(c =>
            c.Arg<string>() + "(" + string.Join(
                ",",
                c.Arg<object?[]>().Select(a => System.Convert.ToString(a, CultureInfo.InvariantCulture))) + ")");
        return loc;
    }

    /// <summary>Runs the posted work right away; the tests have no UI thread.</summary>
    public static IUiDispatcher Dispatcher()
    {
        var ui = Substitute.For<IUiDispatcher>();
        ui.HasThreadAccess.Returns(true);
        ui.When(u => u.Post(Arg.Any<Action>())).Do(c => c.Arg<Action>()());
        return ui;
    }

    /// <summary>Everything can become everything; the format rules are the registry's business.</summary>
    public static IConverterResolver Resolver()
    {
        var resolver = Substitute.For<IConverterResolver>();
        resolver.CanConvert(Arg.Any<InputInfo>(), Arg.Any<FormatId>()).Returns(true);
        return resolver;
    }

    /// <summary>
    /// The real size model behind a trivial estimator: the output is the input size times the factor of the
    /// settings, so the grade table reacts to quality and dimensions exactly as it does in the app.
    /// </summary>
    public sealed class SizeModelEstimator : IEstimator
    {
        private readonly FormatRegistry _registry;

        public SizeModelEstimator(FormatRegistry registry)
        {
            _registry = registry;
        }

        public Estimate Estimate(InputInfo input, ConversionSettings settings) =>
            new(
                TimeSpan.FromSeconds(1),
                Math.Max(1, (long)(input.SizeBytes * SizeModel.Factor(input, settings, _registry))),
                1);

        public void Record(InputInfo input, ConversionSettings settings, ConversionResult result)
        {
        }
    }
}
