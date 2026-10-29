using DigitalBrain;
using DigitalBrain.Examples.Calculator;
using DigitalBrain.Testing;

namespace Calculator.Tests.Unit;

public sealed class CalculatorFacts
{
    public static IEnumerable<TheoryDataRow<AppOperationCase>> Operations => AppBaseline.Cases(typeof(CalculatorApp));

    [Theory]
    [MemberData(nameof(Operations))]
    public Task Baseline(AppOperationCase operation) => AppBaseline.RunAsync(operation);

    [Fact]
    public async Task BddRunner_verifies_all_scenarios_and_records_report_into_BddNeuron()
    {
        await using var os = TestOS.Local();
        var report = await BddRunner.VerifyAsync<CalculatorApp>(targetOs: os, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(report.Passed, report.BaselineError);
        Assert.Equal("calculator", report.AppId);
        Assert.Equal(3, report.Scenarios.Count);
        Assert.All(report.Scenarios, s => Assert.True(s.Passed, s.Error));

        var bdd = os.Get<IBdd>(new NeuronId(new BrainId("test"), "bdd", "calculator"));
        var stored = await bdd.Read(TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        Assert.True(stored.Passed);
        Assert.Equal(3, stored.Scenarios.Count);
    }

    [Fact]
    public async Task Pressing_digits_and_operators_updates_the_rendered_view()
    {
        await using var os = TestOS.Local();
        var app = await os.Install<CalculatorApp>();

        await app.Request("press", "7");
        await app.Request("press", "+");
        await app.Request("press", "5");
        await app.Request("press", "=");

        Assert.Contains("12", app.Main.Texts);
        await app.Request("clear");
        Assert.Contains("0", app.Main.Texts);
    }
}
