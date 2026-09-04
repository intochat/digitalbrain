using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Scripting.Startup;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Microsoft.Extensions.Logging.Abstractions;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

[Binding]
public sealed class BehaviorSteps
{
    private BrainSimulation? _brain;
    private BehaviorScriptWorker? _worker;

    [Given("a running brain")]
    public async Task GivenARunningBrain()
    {
        _brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest([typeof(UIModule)]),
            Configuration = new Dictionary<string, string?>
            {
                [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode,
            },
        });
    }

    [Given("DigitalBrain is activated")]
    public async Task GivenDigitalBrainIsActivated()
        => await Brain.Brain.ActivateAsync(TestContext.Current.CancellationToken);

    [When(@"the user requests a behavior that charts new posts from X account ""(.*)"" onto chart ""(.*)""")]
    public async Task WhenTheUserRequestsAChartingBehavior(string account, string chart)
    {
        var source = $$"""
            var chart = Brain.GetEntity<IChart>("{{chart}}");
            await chart.Render(new ChartState("Elon on X", "line", Array.Empty<ChartPoint>()));
            await Brain.GetEntity<ISurface>("{{ISurface.DefaultInstanceName}}").Open(
                new SurfaceScene("chart:{{chart}}", "Elon on X"),
                8);
            await foreach (var page in Brain.Get<IXAccount>("{{account}}").WatchJournalAsync(
                JournalKind.Outgoing,
                0,
                CancellationToken))
            {
                foreach (var delivery in page.Delta)
                {
                    if (delivery.Signal is NewPost post)
                    {
                        await chart.Append(
                            new ChartPoint(post.Text, 1, EventId: delivery.SignalId.ToString()),
                            "Elon on X");
                    }
                }
            }
            """;

        var outcome = await Brain.Brain.Get<IBehaviors>().SendAsync(
            new AdmitBehavior($"{account}-chart", source),
            TestContext.Current.CancellationToken);
        Assert.Equal(DeliveryOutcome.Handled, outcome);

        var admitted = await Brain.Brain.Get<IBehaviors>().ReadJournalAsync(
            JournalKind.Outgoing,
            0,
            TestContext.Current.CancellationToken);
        Assert.Contains(admitted.Delta, delivery => delivery.Signal is BehaviorAdmitted);

        _worker = new BehaviorScriptWorker(
            new DigitalBrainBehaviorAdmissionSource(Brain.Brain),
            new CSharpStartupScriptRunner(),
            Brain.Brain,
            NullLogger<BehaviorScriptWorker>.Instance);
        await _worker.StartAsync(TestContext.Current.CancellationToken);

        await WaitUntilAsync(async () =>
        {
            var state = await Brain.Brain.GetEntity<IChart>(chart).Read();
            return state is not null;
        });
    }

    [When(@"X account ""(.*)"" publishes ""(.*)""")]
    public async Task WhenXAccountPublishes(string account, string text)
    {
        var outcome = await Brain.Brain.Get<IXAccount>(account).SendAsync(
            new PublishPost(text),
            TestContext.Current.CancellationToken);
        Assert.Equal(DeliveryOutcome.Handled, outcome);
    }

    [Then(@"chart ""(.*)"" has a point labeled ""(.*)""")]
    public async Task ThenChartHasAPointLabeled(string chart, string label)
    {
        await WaitUntilAsync(async () =>
        {
            var state = await Brain.Brain.GetEntity<IChart>(chart).Read();
            return state?.Points.Any(point => string.Equals(point.Label, label, StringComparison.Ordinal)) == true;
        });
    }

    [Then(@"the dashboard includes chart ""(.*)""")]
    public async Task ThenTheDashboardIncludesChart(string chart)
    {
        var surface = await Brain.Brain.GetEntity<ISurface>(ISurface.DefaultInstanceName).Read();
        Assert.NotNull(surface);
        Assert.Contains(
            surface.Scenes,
            scene => string.Equals(scene.SurfaceKey, $"chart:{chart}", StringComparison.Ordinal));
    }

    [AfterScenario]
    public async Task AfterScenario()
    {
        if (_worker is not null)
        {
            await _worker.StopAsync(CancellationToken.None);
            _worker = null;
        }

        if (_brain is not null)
        {
            await _brain.DisposeAsync();
            _brain = null;
        }
    }

    private BrainSimulation Brain
        => _brain ?? throw new InvalidOperationException("Given a running brain first.");

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            timeout.Token,
            TestContext.Current.CancellationToken);
        while (!await condition().ConfigureAwait(false))
        {
            await Task.Delay(50, linked.Token).ConfigureAwait(false);
        }
    }
}
