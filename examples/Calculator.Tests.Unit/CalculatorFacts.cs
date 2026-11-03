using DigitalBrain;
using DigitalBrain.Examples.Calculator;
using DigitalBrain.Rules;
using DigitalBrain.Testing;
using DigitalBrain.Testing.Views;

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
        Assert.Equal(4, report.Scenarios.Count);
        Assert.All(report.Scenarios, s => Assert.True(s.Passed, s.Error));

        ViewFixtures.Verify(report, ViewsDirectory());

        var bdd = os.Get<IBdd>(new NeuronId(new BrainId("test"), "bdd", "calculator"));
        var stored = await bdd.Read(TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        Assert.True(stored.Passed);
        Assert.Equal(4, stored.Scenarios.Count);
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

    [Fact]
    public async Task DividingByZeroIsRejected()
    {
        await using var os = TestOS.Local();
        var app = await os.Install<CalculatorApp>();

        await app.Request("press", "8");
        await app.Request("press", "/");
        await app.Request("press", "0");

        var error = await Assert.ThrowsAsync<RuleViolationException>(() => app.Request("press", "="));

        Assert.Equal(["divisor.not-zero"], error.Rejected.Rules);
        Assert.Contains("0", app.Main.Texts);
    }

    private static string ViewsDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "examples", "Calculator", "views");
            if (File.Exists(Path.Combine(dir.FullName, "examples", "Calculator", "Calculator.csproj")))
            {
                return candidate;
            }
        }
        throw new InvalidOperationException("Could not locate examples/Calculator from " + AppContext.BaseDirectory);
    }
}
