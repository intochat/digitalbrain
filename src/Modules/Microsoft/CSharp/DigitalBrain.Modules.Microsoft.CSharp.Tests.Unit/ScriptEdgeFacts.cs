using System.Text.Json;
using DigitalBrain.Client;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Microsoft.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit;

public sealed class ScriptEdgeFacts
{
    private static readonly CallerContext Alice = new()
    {
        PrincipalId = "alice",
        AccountId = "account-a",
        BrainId = "workspace-a",
        Kind = CallerKind.User,
        StampedBy = TrustedEdge.AuthenticatedHttp,
    };

    [Fact]
    public async Task ARunCallsNeuronsAsItsFileOwnerStampedAsAnApp()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await Brain(sandbox, ct);
        var token = await StartAsAlice(brain, sandbox, "workspace-a/report", ct);
        var edge = brain.SiloServices.GetRequiredService<ScriptEdge>();

        var doubled = await edge.InvokeAsync(token, Call("Double", 21), ct);
        var caller = await edge.InvokeAsync(token, Call("Caller"), ct);

        Assert.Equal(42, doubled!.Value.GetInt32());
        Assert.Equal("alice App AppProxy workspace-a/report", caller!.Value.GetString());
    }

    [Fact]
    public async Task TokensStopSpeakingForAFileOnceItStops()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await Brain(sandbox, ct);
        var token = await StartAsAlice(brain, sandbox, "workspace-a/stopped", ct);
        var edge = brain.SiloServices.GetRequiredService<ScriptEdge>();

        await brain.Get<ICSharpFile>("workspace-a/stopped").Stop(ct);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => edge.InvokeAsync(token, Call("Double", 1), ct));
    }

    [Fact]
    public async Task RefusesForgedTokensAndContractsOutsideTheInstalledOnes()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await Brain(sandbox, ct);
        var token = await StartAsAlice(brain, sandbox, "workspace-a/forged", ct);
        var edge = brain.SiloServices.GetRequiredService<ScriptEdge>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => edge.InvokeAsync(token[..^2] + "AA", Call("Double", 1), ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => edge.InvokeAsync(null, Call("Double", 1), ct));
        await Assert.ThrowsAsync<ArgumentException>(() => edge.InvokeAsync(token, (typeof(ICSharpFile).FullName!, "pinger", "Double", [JsonSerializer.SerializeToElement(1)]), ct));
        await Assert.ThrowsAsync<ArgumentException>(() => edge.InvokeAsync(token, Call("Watch", 1), ct));
    }

    [Fact]
    public async Task SignalsReachTheScriptAsJsonOfTheRequestedTypeOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await Brain(sandbox, ct);
        var token = await StartAsAlice(brain, sandbox, "workspace-a/listener", ct);
        var edge = brain.SiloServices.GetRequiredService<ScriptEdge>();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var signals = await edge.OpenSignalsAsync(token, typeof(IPinger).FullName!, "pinger", nameof(Pinged), stop.Token);
        await brain.Get<IPinger>("pinger").Ping(5);

        await foreach (var signal in signals)
        {
            Assert.Equal("""{"number":5}""", signal);
            break;
        }
        await stop.CancelAsync();
    }

    [Fact]
    public async Task TheModuleAllowsTheComposedModulesContractAssembliesOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await SandboxBrain.StartAsync(new FakeSandbox(), ct);

        var contracts = brain.SiloServices.GetRequiredService<ScriptContracts>();

        Assert.Equal(typeof(ICSharpFile), contracts.Find(typeof(ICSharpFile).FullName!));
        // Aspire's contracts are deployed beside this host, but its module is not composed here.
        Assert.Throws<ArgumentException>(() => contracts.Find(typeof(DigitalBrain.Microsoft.Aspire.IAspire).FullName!));
    }

    [Fact]
    public void PlatformOnlyContractsAreNotInTheScriptCatalog()
    {
        var contracts = new ScriptContracts([typeof(IPinger).Assembly, typeof(DigitalBrain.Sdk.Secrets.ISecrets).Assembly]);

        Assert.Equal(typeof(IPinger), contracts.Find(typeof(IPinger).FullName!));
        Assert.Throws<ArgumentException>(() => contracts.Find(typeof(DigitalBrain.Sdk.Secrets.ISecrets).FullName!));
        Assert.Throws<ArgumentException>(() => contracts.Find(typeof(DigitalBrain.Platform.Integrations.IIntegrationRegistration).FullName!));
        Assert.Throws<ArgumentException>(() => contracts.Find(typeof(IPlatformOnlyPinger).FullName!));
    }

    [Fact]
    public void TheWholePlatformAssemblyContributesNoScriptContractsEvenWithoutAttributesOnTheTypes()
    {
        var platform = typeof(DigitalBrain.Platform.PlatformHosting).Assembly;
        Assert.True(DigitalBrain.Contracts.PlatformAssemblyAttribute.IsPlatform(platform));
        var contracts = new ScriptContracts([platform]);

        foreach (var neuronContract in platform.GetExportedTypes().Where(type => type.IsInterface && typeof(DigitalBrain.Contracts.INeuron).IsAssignableFrom(type)))
        {
            Assert.Throws<ArgumentException>(() => contracts.Find(neuronContract.FullName!));
            Assert.True(DigitalBrain.Contracts.PlatformOnlyAttribute.AppliesTo(neuronContract));
        }
    }

    [Fact]
    public void ThePlatformAssemblyIsNeverAContractsAssemblyOfAComposedModule()
    {
        var inventory = new DigitalBrain.Core.ModuleInventory([typeof(DigitalBrain.Platform.PlatformHosting)]);

        Assert.DoesNotContain(typeof(DigitalBrain.Platform.PlatformHosting).Assembly, inventory.ContractAssemblies());
        Assert.Empty(DigitalBrain.Core.ModuleInventory.ContractAssembliesOf(typeof(DigitalBrain.Platform.PlatformHosting).Assembly));
    }

    [Fact]
    public void TheIntegrationRegistrationContractIsAbsentFromTheScriptCatalogBecauseReleaseReliesOnThat()
    {
        var contracts = new ScriptContracts([typeof(DigitalBrain.Platform.Integrations.IIntegrationRegistration).Assembly]);

        Assert.True(DigitalBrain.Contracts.PlatformOnlyAttribute.AppliesTo(typeof(DigitalBrain.Platform.Integrations.IIntegrationRegistration)));
        Assert.Throws<ArgumentException>(() => contracts.Find(typeof(DigitalBrain.Platform.Integrations.IIntegrationRegistration).FullName!));
    }

    [Fact]
    public async Task TheEdgeRefusesToInvokeAPlatformOnlyContractWithAForgedPlatformCaller()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await SandboxBrain.StartAsync(sandbox, ct, silo => silo.Services.AddSingleton(
            new ScriptContracts([typeof(IPinger).Assembly, typeof(DigitalBrain.Sdk.Secrets.ISecrets).Assembly])));
        var token = await StartAsAlice(brain, sandbox, "workspace-a/attacker", ct);
        var edge = brain.SiloServices.GetRequiredService<ScriptEdge>();
        var forged = JsonSerializer.SerializeToElement(new
        {
            principalId = "x",
            accountId = "x",
            brainId = "x",
            kind = "Platform",
            stampedBy = "Platform",
            appId = "x",
        });

        await Assert.ThrowsAsync<ArgumentException>(() => edge.InvokeAsync(token,
            (typeof(DigitalBrain.Platform.Integrations.IIntegrationRegistration).FullName!, "integration/openai", "Release", [forged]), ct));
        await Assert.ThrowsAsync<ArgumentException>(() => edge.InvokeAsync(token,
            (typeof(DigitalBrain.Sdk.Secrets.ISecrets).FullName!, "owner", "Resolve", [forged, forged]), ct));
        await Assert.ThrowsAsync<ArgumentException>(() => edge.OpenSignalsAsync(token, typeof(IPlatformOnlyPinger).FullName!, "x", "Pinged", ct));
    }

    [Fact]
    public async Task SignalsPublishedBetweenRunsArriveWhenTheNextRunSubscribes()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await Brain(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var edge = brain.SiloServices.GetRequiredService<ScriptEdge>();
        var token = await StartAsAlice(brain, sandbox, "workspace-a/durable", ct);

        // The first run subscribes, which registers durably, then dies with its stream.
        using var first = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await edge.OpenSignalsAsync(token, typeof(IPinger).FullName!, "pinger", nameof(Pinged), first.Token);
        await first.CancelAsync();
        sandbox.ExitLatest(0);

        await pinger.Ping(41);                        // nobody is streaming: buffered, wakes a run
        await Eventually(() => sandbox.Started == 2, ct);
        var next = LatestToken(sandbox);              // the wake-run's token

        var resumed = await edge.OpenSignalsAsync(next, typeof(IPinger).FullName!, "pinger", nameof(Pinged), ct);
        await foreach (var json in resumed.WithCancellation(ct))
        {
            Assert.Contains("\"number\":41", json, StringComparison.Ordinal);   // the buffered signal came first
            break;
        }
    }

    [Fact]
    public async Task ALiveStreamReceivesASignalPromptlyThroughTheDoorbell()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await Brain(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var edge = brain.SiloServices.GetRequiredService<ScriptEdge>();
        var token = await StartAsAlice(brain, sandbox, "workspace-a/live", ct);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var stream = await edge.OpenSignalsAsync(token, typeof(IPinger).FullName!, "pinger", nameof(Pinged), stop.Token);
        var reading = ReadOneAsync(stream, stop.Token);
        await pinger.Ping(7);

        Assert.Contains("\"number\":7", await reading.WaitAsync(TimeSpan.FromSeconds(5), ct), StringComparison.Ordinal);
        await stop.CancelAsync();
    }

    [Fact]
    public async Task StreamsRefuseTokensThatNoLongerSpeakForTheFile()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await Brain(sandbox, ct);
        var edge = brain.SiloServices.GetRequiredService<ScriptEdge>();
        var token = await StartAsAlice(brain, sandbox, "workspace-a/revoked", ct);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await edge.OpenSignalsAsync(token, typeof(IPinger).FullName!, "pinger", nameof(Pinged), stop.Token);
        await stop.CancelAsync();

        await brain.Get<ICSharpFile>("workspace-a/revoked").Stop(ct);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => edge.OpenSignalsAsync(token, typeof(IPinger).FullName!, "pinger", nameof(Pinged), ct));
    }

    private static async Task<string> ReadOneAsync(IAsyncEnumerable<string> stream, CancellationToken ct)
    {
        await foreach (var json in stream.WithCancellation(ct)) { return json; }
        throw new InvalidOperationException("The stream ended without a signal.");
    }

    private static string LatestToken(FakeSandbox sandbox)
        => sandbox.Requests.Last(FakeSandbox.IsStart).Body!["environment"]!["DigitalBrain__Token"]!.GetValue<string>();

    private static async Task Eventually(Func<bool> condition, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition()) { await Task.Delay(TimeSpan.FromMilliseconds(50), deadline.Token); }
    }

    private static (string Contract, string Key, string Method, JsonElement[] Arguments) Call(string method, params int[] arguments)
        => (typeof(IPinger).FullName!, "pinger", method, [.. arguments.Select(argument => JsonSerializer.SerializeToElement(argument))]);

    private static async Task<string> StartAsAlice(UnitBrain brain, FakeSandbox sandbox, string fileId, CancellationToken ct)
    {
        var file = brain.Get<ICSharpFile>(fileId);
        await file.Write("Console.WriteLine(1);", ct);
        CallerContextStamper.Stamp(Alice);
        try { await file.Start(ct); }
        finally { Orleans.Runtime.RequestContext.Clear(); }
        return sandbox.Requests.Last(FakeSandbox.IsStart).Body!["environment"]!["DigitalBrain__Token"]!.GetValue<string>();
    }

    // The test's own IPinger stands in for an installed module contract.
    private static Task<UnitBrain> Brain(FakeSandbox sandbox, CancellationToken ct)
        => SandboxBrain.StartAsync(sandbox, ct, silo => silo.Services.AddSingleton(new ScriptContracts([typeof(IPinger).Assembly])));
}
