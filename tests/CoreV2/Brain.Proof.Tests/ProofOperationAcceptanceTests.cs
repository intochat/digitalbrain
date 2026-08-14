using Brain.Abstractions.Activities;
using Brain.Abstractions.Identity;
using Brain.Modules.Proof.Contracts;
using Brain.Testing;
using Xunit;

namespace Brain.Proof.Tests;

#pragma warning disable IDE1006

public sealed class ProofOperationAcceptanceTests
{
    [Fact]
    public async Task caller_invokes_public_operation_and_observes_only_the_terminal_result()
    {
        await using var host = await BrainTestHost.StartAsync();
        var caller = host.Caller("workspace/proof", "principal/alice");

        var accepted = await host.Operations.InvokeAsync<ProofInput, ProofResult>(
            ProofContracts.Run,
            new ProofInput("alpha"),
            caller,
            new IdempotencyKey("proof/1"),
            TestContext.Current.CancellationToken);

        var view = await host.Operations.ObserveAsync(
            accepted.Activity,
            caller,
            TestContext.Current.CancellationToken);
        var result = await host.ReadResultAsync<ProofResult>(view, caller);

        Assert.Equal(ActivityStatus.Completed, view.Status);
        Assert.Equal("summary", result.Route);
        Assert.DoesNotContain(
            typeof(ActivityView).GetProperties(),
            property => property.Name.Contains("Journal", StringComparison.Ordinal));
    }

    [Fact]
    public async Task retry_with_the_same_idempotency_key_returns_the_same_completed_activity()
    {
        await using var host = await BrainTestHost.StartAsync();
        var caller = host.Caller("workspace/proof", "principal/alice");
        var key = new IdempotencyKey("proof/retry");

        var first = await host.Operations.InvokeAsync<ProofInput, ProofResult>(
            ProofContracts.Run, new ProofInput("retry"), caller, key, TestContext.Current.CancellationToken);
        var retried = await host.Operations.InvokeAsync<ProofInput, ProofResult>(
            ProofContracts.Run, new ProofInput("retry"), caller, key, TestContext.Current.CancellationToken);

        Assert.Equal(first.Activity, retried.Activity);
        var view = await host.Operations.ObserveAsync(first.Activity, caller, TestContext.Current.CancellationToken);
        Assert.Equal(ActivityStatus.Completed, view.Status);
    }

    [Fact]
    public async Task correction_accepts_a_requested_route_and_replaces_the_authorized_graph_route()
    {
        await using var host = await BrainTestHost.StartAsync();
        var caller = host.Caller("workspace/proof", "principal/alice");

        var corrected = await host.Operations.InvokeAsync<CorrectionInput, CorrectionResult>(
            ProofContracts.Correct,
            new CorrectionInput("assessment"),
            caller,
            new IdempotencyKey("proof/correction"),
            TestContext.Current.CancellationToken);
        var correctionView = await host.Operations.ObserveAsync(corrected.Activity, caller, TestContext.Current.CancellationToken);
        var correction = await host.ReadResultAsync<CorrectionResult>(correctionView, caller);

        Assert.Equal("assessment", correction.AppliedRoute);
        Assert.Single(await host.RewireEvidenceAsync());

        var accepted = await host.Operations.InvokeAsync<ProofInput, ProofResult>(
            ProofContracts.Run,
            new ProofInput("beta"),
            caller,
            new IdempotencyKey("proof/after-correction"),
            TestContext.Current.CancellationToken);
        var view = await host.Operations.ObserveAsync(accepted.Activity, caller, TestContext.Current.CancellationToken);
        var result = await host.ReadResultAsync<ProofResult>(view, caller);

        Assert.Equal("assessment", result.Route);
    }

    [Fact]
    public async Task concurrent_callers_observe_their_own_terminal_results()
    {
        await using var host = await BrainTestHost.StartAsync();
        var alice = host.Caller("workspace/proof", "principal/alice");
        var bob = host.Caller("workspace/proof", "principal/bob");

        var accepted = await Task.WhenAll(
            host.Operations.InvokeAsync<ProofInput, ProofResult>(ProofContracts.Run, new ProofInput("alice"), alice, new IdempotencyKey("proof/alice"), TestContext.Current.CancellationToken),
            host.Operations.InvokeAsync<ProofInput, ProofResult>(ProofContracts.Run, new ProofInput("bob"), bob, new IdempotencyKey("proof/bob"), TestContext.Current.CancellationToken));

        var aliceView = await host.Operations.ObserveAsync(accepted[0].Activity, alice, TestContext.Current.CancellationToken);
        var bobView = await host.Operations.ObserveAsync(accepted[1].Activity, bob, TestContext.Current.CancellationToken);

        Assert.Equal("summary", (await host.ReadResultAsync<ProofResult>(aliceView, alice)).Route);
        Assert.Equal("summary", (await host.ReadResultAsync<ProofResult>(bobView, bob)).Route);
    }

    [Fact]
    public async Task public_operation_path_uses_the_registered_cluster_runtime_grain()
    {
        await using var host = await BrainTestHost.StartAsync();
        var caller = host.Caller("workspace/proof", "principal/alice");

        var accepted = await host.Operations.InvokeAsync<ProofInput, ProofResult>(
            ProofContracts.Run, new ProofInput("cluster"), caller, new IdempotencyKey("proof/cluster"), TestContext.Current.CancellationToken);

        Assert.NotEqual(Guid.Empty, await host.RuntimeInstanceIdAsync());
        Assert.Equal(1, await host.RuntimeDispatchCountAsync());
        var view = await host.Operations.ObserveAsync(accepted.Activity, caller, TestContext.Current.CancellationToken);
        Assert.Equal("summary", (await host.ReadResultAsync<ProofResult>(view, caller)).Route);
    }
}

#pragma warning restore IDE1006
