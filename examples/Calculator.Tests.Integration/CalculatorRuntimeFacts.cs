using DigitalBrain;
using DigitalBrain.Examples.Calculator;
using DigitalBrain.Identity.Local;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Calculator.Tests.Integration;

public sealed class CalculatorRuntimeFixture : BothRuntimesFixture
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "db-calc-" + Guid.NewGuid().ToString("N"));

    protected override IReadOnlyList<Type> Modules => [typeof(CalculatorScreen)];

    protected override void ConfigureSilo(IServiceCollection services)
        => services.AddLocalIdentity(SiloNeurons.Get, _home);
}

public sealed class CalculatorRuntimeFacts(CalculatorRuntimeFixture fixture) : IClassFixture<CalculatorRuntimeFixture>
{
    [Theory, MemberData(nameof(NeuronRuntimeKinds.Both), MemberType = typeof(NeuronRuntimeKinds))]
    public async Task CalculatorScreenAgreesOnBothRuntimes(NeuronRuntimeKind kind)
    {
        var runtime = await fixture.Runtime(kind);

        var calcId = runtime.Id("app.calculator", "default");
        var screen = runtime.Get<IScreen>(calcId);

        var initialView = await screen.Read();
        Assert.NotNull(initialView);
        Assert.Contains("0", initialView.View.GetRawText());

        await screen.Request<CalculatorPress>(new("7"));
        await screen.Request<CalculatorPress>(new("+"));
        await screen.Request<CalculatorPress>(new("5"));
        await screen.Request<CalculatorPress>(new("="));
        await runtime.Idle();

        var view = await screen.Read();
        Assert.NotNull(view);
        Assert.Contains("12", view.View.GetRawText());

        await screen.Request<CalculatorClear>(new());
        await runtime.Idle();

        var cleared = await screen.Read();
        Assert.NotNull(cleared);
        Assert.Contains("0", cleared.View.GetRawText());
    }
}
