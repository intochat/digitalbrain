using DigitalBrain;
using DigitalBrain.Examples.Calculator;
using DigitalBrain.Rules;
using DigitalBrain.Testing;
using DigitalBrain.Testing.Views;
using DigitalBrain.Units;
using Xunit;

namespace Calculator.Tests.Unit;

public sealed class CalculatorFacts
{
    public static IEnumerable<TheoryDataRow<ScreenOperationCase>> Operations => ScreenBaseline.Cases(typeof(CalculatorScreen));

    [Theory]
    [MemberData(nameof(Operations))]
    public Task Baseline(ScreenOperationCase operation) => ScreenBaseline.RunAsync(operation);

    [Fact]
    public async Task Pressing_digits_and_operators_updates_the_rendered_view()
    {
        await using var os = TestOS.Local();
        os.Register(typeof(CalculatorScreen).Assembly);

        var screen = os.Get<IScreen>(new NeuronId(new BrainId("test"), "app.calculator", "default"));

        await screen.Request<CalculatorPress>(new("7"));
        await screen.Request<CalculatorPress>(new("+"));
        await screen.Request<CalculatorPress>(new("5"));
        await screen.Request<CalculatorPress>(new("="));
        await os.Idle();

        var view = await screen.Read();
        Assert.NotNull(view);
        var presented = PresentedView.From(view);
        Assert.Contains("12", presented.Texts);

        await screen.Request<CalculatorClear>(new());
        await os.Idle();

        var cleared = await screen.Read();
        Assert.NotNull(cleared);
        var clearedPresented = PresentedView.From(cleared);
        Assert.Contains("0", clearedPresented.Texts);
    }

    [Fact]
    public async Task DividingByZeroIsRejected()
    {
        await using var os = TestOS.Local();
        os.Register(typeof(CalculatorScreen).Assembly);

        var screen = os.Get<IScreen>(new NeuronId(new BrainId("test"), "app.calculator", "default"));

        await screen.Request<CalculatorPress>(new("8"));
        await screen.Request<CalculatorPress>(new("/"));
        await screen.Request<CalculatorPress>(new("0"));
        await screen.Request<CalculatorPress>(new("="));
        await os.Idle();

        var view = await screen.Read();
        Assert.NotNull(view);
        var presented = PresentedView.From(view);
        Assert.Contains("0", presented.Texts);
    }

    [Fact]
    public async Task ViewFixtures_Verify_MatchesOnDisk()
    {
        await using var os = TestOS.Local();
        os.Register(typeof(CalculatorScreen).Assembly);

        var screen = os.Get<IScreen>(new NeuronId(new BrainId("test"), "app.calculator", "default"));
        var view = await screen.Read();
        Assert.NotNull(view);

        ViewFixtures.Verify(view, ViewsDirectory());
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
