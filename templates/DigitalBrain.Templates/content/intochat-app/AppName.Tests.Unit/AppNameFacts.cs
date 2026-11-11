using System.Reflection;
using System.Text.RegularExpressions;
using DigitalBrain;
using DigitalBrain.Neurons;
using DigitalBrain.Testing;
using DigitalBrain.Units;

namespace APP_NAMESPACE.Tests.Unit;

public sealed class APP_CLASSFacts
{
    // The generated baseline: one row per operation, the empty input, a fake for every required capability, under 30 s. Keep it.
    public static IEnumerable<TheoryDataRow<ScreenOperationCase>> Operations => ScreenBaseline.Cases(typeof(APP_CLASS));

    [Theory]
    [MemberData(nameof(Operations))]
    public Task Baseline(ScreenOperationCase operation) => ScreenBaseline.RunAsync(operation);

    // Scenario: Opening shows the start view. Add one fact per scenario in the spec comment.
    [Fact]
    public async Task Opening_shows_the_start_view()
    {
        await using var os = TestOS.Local();
        os.Register(typeof(APP_CLASS).Assembly);

        var screen = os.Get<IScreen>(new NeuronId(new BrainId("test"), "app.APP_ID", "default"));

        await screen.Request<Opened>(new());
        await os.Idle();

        var view = await screen.Read();
        Assert.NotNull(view);
        var presented = PresentedView.From(view);
        Assert.Contains("Welcome to APP_CLASS.", presented.Texts);
    }

    [Fact]
    public async Task Counter_survives_runtime_restart()
    {
        await using var os = TestOS.Local();
        os.Register<CounterNeuron>();
        var id = new NeuronId(new BrainId("test"), "APP_ID.counter", "starter");
        Assert.Equal(1, await os.Get<ICounter>(id).Increment());
        await os.Restart();
        Assert.Equal(1, await os.Get<ICounter>(id).Count());
        Assert.Equal(2, await os.Get<ICounter>(id).Increment());
    }

    [Fact]
    public void App_id_is_lowercase_and_dotted()
    {
        var neuronType = typeof(APP_CLASS).GetCustomAttribute<NeuronAttribute>()!.Type;
        Assert.StartsWith("app.", neuronType);
        Assert.Matches(new Regex("^[a-z][a-z0-9-]*(\\.[a-z][a-z0-9-]*)*$"), neuronType["app.".Length..]);
    }
}
