using DigitalBrain.Microsoft.Aspire;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Modules.Microsoft.Aspire.Tests.Unit;

public sealed class AspireNeuronFacts
{
    private const string Application = "DigitalBrain";
    private const string StampedPublisher = "microsoft.aspire/" + Application;

    [Fact]
    public async Task EveryReportedStateChangeIsSignalledOnceAndListed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AspireModule>().StartAsync(ct);
        var aspire = brain.Get<IAspire>(Application);
        var reporter = brain.Grains.GetGrain<IAspireResourceReporter>(Application);
        await using var changes = await brain.Observe<ResourceStateChanged>(aspire, ct);

        await reporter.Report(new("csharp-sandbox", "Container", "Starting", null, []));
        await reporter.Report(new("csharp-sandbox", "Container", "Starting", null, []));
        await reporter.Report(new("csharp-sandbox", "Container", "Running", "Healthy", ["http://localhost:5000"]));

        Assert.Equal(new ResourceStateChanged("csharp-sandbox", "Starting", null, null) { Publisher = StampedPublisher }, await changes.NextAsync(ct: ct));
        Assert.Equal(new ResourceStateChanged("csharp-sandbox", "Running", "Healthy", "Starting") { Publisher = StampedPublisher }, await changes.NextAsync(ct: ct));
        var sandbox = Assert.Single(await aspire.ListResources(ct));
        Assert.Equal("http://localhost:5000", Assert.Single(sandbox.Urls));
    }

    [Fact]
    public async Task CommandsFailClearlyWithoutAConnectedAppHost()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AspireModule>().StartAsync(ct);
        brain.SiloServices.GetRequiredService<AspireBridge>().ConnectTimeout = TimeSpan.FromMilliseconds(200);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => brain.Get<IAspire>(Application).StartResource("csharp-sandbox", ct));

        Assert.Contains("No Aspire AppHost", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommandsTravelToTheAppHostAndReturnItsResult()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AspireModule>().StartAsync(ct);
        var bridge = brain.SiloServices.GetRequiredService<AspireBridge>();
        using var appHost = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var served = ServeAsync(bridge, command => command.Command == "start" ? new(true, null) : new(false, "no such command"), appHost.Token);
        var aspire = brain.Get<IAspire>(Application);

        await aspire.StartResource("csharp-sandbox", ct);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => aspire.StopResource("csharp-sandbox", ct));

        Assert.Contains("no such command", error.Message, StringComparison.Ordinal);
        await appHost.CancelAsync();
        Assert.Equal(["start csharp-sandbox", "stop csharp-sandbox"], await served);
    }

    private static async Task<List<string>> ServeAsync(AspireBridge bridge, Func<AspireBridgeCommand, AspireBridgeCommandResult> execute, CancellationToken cancellationToken)
    {
        List<string> served = [];
        try
        {
            await foreach (var command in bridge.ReadCommandsAsync(cancellationToken))
            {
                served.Add(command.Command + " " + command.Resource);
                bridge.Complete(command.Id, execute(command));
            }
        }
        catch (OperationCanceledException) { }
        return served;
    }
}
