using DigitalBrain;
using DigitalBrain.Examples.Calculator;
using DigitalBrain.Testing;

namespace Calculator.Tests.Integration;

public sealed class CalculatorRuntimeFixture : BothRuntimesFixture
{
    protected override IReadOnlyList<Type> Modules => [typeof(CalculatorApp)];
}

public sealed class CalculatorRuntimeFacts(CalculatorRuntimeFixture fixture) : IClassFixture<CalculatorRuntimeFixture>
{
    [Theory, MemberData(nameof(NeuronRuntimeKinds.Both), MemberType = typeof(NeuronRuntimeKinds))]
    public async Task CalculatorScenariosAgreeOnBothRuntimes(NeuronRuntimeKind kind)
    {
        var ct = TestContext.Current.CancellationToken;
        var runtime = await fixture.Runtime(kind);

        var report = await BddRunner.VerifyAsync<CalculatorApp>(cancellationToken: ct);
        Assert.True(report.Passed, report.BaselineError);
        Assert.Equal(4, report.Scenarios.Count);

        var bddId = runtime.Id("bdd", "calculator");
        var bdd = runtime.Get<IBdd>(bddId);
        await bdd.Record(report, ct);
        await runtime.Idle();

        var stored = await bdd.Read(ct);
        Assert.NotNull(stored);
        Assert.True(stored.Passed);
        Assert.Equal("calculator", stored.AppId);
        Assert.Equal(4, stored.Scenarios.Count);

        var signals = await runtime.WaitForSignals<BddVerified>(bddId, 1, TimeSpan.FromSeconds(10), ct);
        var verified = Assert.Single(signals);
        Assert.Equal("calculator", verified.AppId);
        Assert.True(verified.Passed);
    }
}
