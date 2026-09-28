using System.Text.Json;
using DigitalBrain.Client;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Microsoft.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ScriptEdgeFacts
{
    private static readonly CallerContext Alice = new()
    {
        PrincipalId = "alice", AccountId = "account-a", WorkspaceId = "workspace-a",
        Kind = CallerKind.User, StampedBy = TrustedEdge.AuthenticatedHttp,
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
        await Assert.ThrowsAsync<ArgumentException>(() => edge.InvokeAsync(token, Call("Double", 1) with { Contract = typeof(ICSharpFile).FullName! }, ct));
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
    public async Task TheModuleAllowsTheInstalledContractAssemblies()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await SandboxBrain.StartAsync(new FakeSandbox(), ct);

        var contracts = brain.SiloServices.GetRequiredService<ScriptContracts>();

        Assert.Equal(typeof(DigitalBrain.Microsoft.Aspire.IAspire), contracts.Find(typeof(DigitalBrain.Microsoft.Aspire.IAspire).FullName!));
        Assert.Equal(typeof(ICSharpFile), contracts.Find(typeof(ICSharpFile).FullName!));
    }

    private static ScriptInvocation Call(string method, params int[] arguments)
        => new(typeof(IPinger).FullName!, "pinger", method, [.. arguments.Select(argument => JsonSerializer.SerializeToElement(argument))]);

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
