using System.Text.RegularExpressions;
using DigitalBrain;
using DigitalBrain.Testing;

namespace APP_NAMESPACE.Tests.Unit;

public sealed class APP_CLASSFacts
{
    // The generated baseline: one row per operation, the empty input, a fake for every required capability, under 5 s. Keep it.
    public static IEnumerable<TheoryDataRow<AppOperationCase>> Operations => AppBaseline.Cases(typeof(APP_CLASS));

    [Theory]
    [MemberData(nameof(Operations))]
    public Task Baseline(AppOperationCase operation) => AppBaseline.RunAsync(operation);

    // Scenario: Opening shows the start view. Add one fact per scenario in the spec comment.
    [Fact]
    public async Task Opening_shows_the_start_view()
    {
        var report = await BddRunner.VerifyAsync<APP_CLASS>();
        Assert.True(report.Passed);

        await using var os = TestOS.Local();
        var app = await os.Install<APP_CLASS>();

        await app.Request("open");

        Assert.Contains("Welcome to APP_CLASS.", app.Main.Texts);
        Assert.Null(app.App.RecordedError);
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
        Assert.Matches(new Regex("^[a-z][a-z0-9-]*(\\.[a-z][a-z0-9-]*)*$"), TestOS.ManifestOf(typeof(APP_CLASS)).Id);
    }
}
