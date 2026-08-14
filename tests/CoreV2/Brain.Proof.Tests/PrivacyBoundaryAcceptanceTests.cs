using System.Reflection;
using Brain.Abstractions.Activities;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Events;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Modules;
using Brain.Abstractions.Operations;
using Brain.Core.Modules;
using Brain.Modules.Proof.Contracts;
using Brain.Testing;
using Xunit;

namespace Brain.Proof.Tests;

#pragma warning disable IDE1006

public sealed class PrivacyBoundaryAcceptanceTests
{
    [Fact]
    public async Task public_activity_projection_exposes_only_activity_safe_references()
    {
        await using var host = await BrainTestHost.StartAsync();
        var caller = host.Caller("workspace/privacy", "principal/alice");
        var accepted = await host.Operations.InvokeAsync<ProofInput, ProofResult>(
            ProofContracts.Run, new ProofInput("alpha"), caller, new IdempotencyKey("privacy/view"), TestContext.Current.CancellationToken);

        var view = await host.Operations.ObserveAsync(accepted.Activity, caller, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["Activity", "Operation", "Status", "TerminalResultContract", "Progress", "Result", "Problem"],
            typeof(ActivityView).GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(property => property.Name));
        Assert.Equal(ActivityStatus.Completed, view.Status);
    }

    [Fact]
    public async Task unregistered_operation_is_rejected_by_the_real_gateway_without_creating_an_activity()
    {
        await using var host = await BrainTestHost.StartAsync();
        var caller = host.Caller("workspace/privacy", "principal/alice");
        var unregistered = new OperationDescriptor(
            new OperationId("proof.unregistered@1"), ProofContracts.Run.InputContract, ProofContracts.Run.TerminalResultContract,
            ProofContracts.Run.EntryRole, ProofContracts.Run.Owner, ProofContracts.Run.Version);

        var activityCount = await host.ActivityCountAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => host.Operations.InvokeAsync<ProofInput, ProofResult>(
            unregistered, new ProofInput("alpha"), caller, new IdempotencyKey("privacy/unregistered"), TestContext.Current.CancellationToken));

        Assert.Equal(activityCount, await host.ActivityCountAsync());
    }

    [Fact]
    public void consumer_cannot_register_another_modules_private_event()
    {
        var privateEvent = new ContractId("private/raised@1");
        var owner = new ModuleManifest(new ModuleId("private"), new ModuleVersion(1, 0, 0), [], [], [],
            [new EventDescriptor(privateEvent, new ModuleId("private"), typeof(PrivateRaised), EventVisibility.Internal)], [privateEvent], [], [], []);
        var consumer = new ModuleManifest(new ModuleId("consumer"), new ModuleVersion(1, 0, 0), [], [], [], [], [privateEvent], [], [], []);

        var failure = Assert.Throws<ManifestValidationException>(() => ManifestValidator.Validate([owner, consumer]));

        Assert.Contains("internal event", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task another_principal_cannot_observe_an_activity_without_workspace_policy_permission()
    {
        await using var host = await BrainTestHost.StartAsync();
        var alice = host.Caller("workspace/privacy", "principal/alice");
        var bob = host.Caller("workspace/privacy", "principal/bob");
        var accepted = await host.Operations.InvokeAsync<ProofInput, ProofResult>(
            ProofContracts.Run, new ProofInput("alpha"), alice, new IdempotencyKey("privacy/owner"), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => host.Operations.ObserveAsync(accepted.Activity, bob, TestContext.Current.CancellationToken));
    }

    private sealed class PrivateRaised : IDomainEvent;
}

#pragma warning restore IDE1006
