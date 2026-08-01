using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;
using DigitalBrain.Google;
using DigitalBrain.Kernel;
using DigitalBrain.Mcp;
using DigitalBrain.Mcp.Testing;
using DigitalBrain.Tasks;
using DigitalBrain.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

public sealed class UserActionProductionRail(UserActionProductionRailFixture fixture)
{
    private const string BrokerCredential = "user-action-production-rail-credential";

    [Fact(
        Timeout = 120_000,
        DisplayName =
            "StageDispatch EnsureAuthorizedAsync parks Task Waiting; DeliverCallback resumes same attempt via bridge Continue without replaying provider side effect")]
    public async Task ProductionMcpStageDispatchParksAndCallbackResumesSameAttempt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        AuthRequiringDispatchProbe.Reset();
        var probeText = $"auth-rail-{Guid.NewGuid():N}";
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        CatalogGmail(brain);

        var worker = brain.Neuron<IWorker>("auth-rail-worker");
        var task = brain.Neuron<ITask>("auth-rail-task");
        var probe = brain.Neuron<IAuthRequiringProbe>("auth-rail-probe");
        var auth = brain.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);

        var activation = new BehaviorTaskActivation(
            new BehaviorId("com.digitalbrain.sample"),
            new BehaviorRevisionId("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
            contractVersion: "1",
            caseId: "auth-rail",
            protectedPayload: new ProtectedPayloadReference(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            triggerTypeName: "SampleTrigger",
            capabilities: []);
        var goal = new BehaviorActivationGoal(
            activation.BehaviorId,
            activation.Revision,
            activation.ContractVersion,
            activation.CaseId,
            activation.ProtectedPayload,
            activation.TriggerTypeName,
            activation.Capabilities);

        var started = await task.Reference.Start(new StartTask(
            CommandId.New(),
            goal,
            worker.Id,
            new TaskPolicy(1, TimeSpan.Zero, null),
            Activation: activation));

        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        var running = await WaitForStateAsync(task, TaskState.Running, cancellationToken);
        Assert.NotNull(running.ActiveAttempt);
        Assert.Equal(started.ActiveAttempt, running.ActiveAttempt);
        var attempt = running.ActiveAttempt.Value;

        var catalog = brain.Cluster.ClientServices.GetRequiredService<ActiveCapabilityCatalog>();
        var payloads = new GrainBehaviorProtectedPayloadAccess(brain.Cluster.Client);
        var dispatch = new GrainBehaviorCapabilityDispatchAccess(brain.Cluster.Client, catalog, payloads);

        var edge = new BehaviorCapabilityEdge(
            new NeuronId(AuthRequiringDispatchProbe.NeuronContractId, task.Id.Owner, probe.Id.Name),
            AuthRequiringDispatchProbe.RequestContractId,
            1,
            AuthRequiringDispatchProbe.ResponseContractId,
            1);

        var commandId = CommandId.New();
        var requestBytes = BehaviorPayloadJson.Serialize(
            new AuthRequiringProbeRequest(commandId.Value, probeText),
            typeof(AuthRequiringProbeRequest));
        var requestRef = await payloads.StoreAsync(
            task.Id.Owner,
            task.Id,
            attempt,
            requestBytes,
            cancellationToken);

        // Production StageDispatch → EnsureAuthorizedAsync → bridge bind → park exception.
        var parkException = await Assert.ThrowsAsync<BehaviorUserActionRequiredException>(() =>
            dispatch.DispatchAsync(
                task.Id.Owner,
                task.Id,
                attempt,
                edge,
                requestRef,
                cancellationToken).AsTask());
        Assert.NotNull(parkException.Requirement);
        var requirement = parkException.Requirement!;
        Assert.Equal(task.Id, requirement.Task);
        Assert.Equal(attempt, requirement.Attempt);
        Assert.Equal("google.gmail", requirement.ModuleId);
        Assert.Equal(UserActionCompletionBridge.For(task.Id.Owner, requirement.ActionEpoch), requirement.Completer);
        Assert.Equal(0, AuthRequiringDispatchProbe.CountFor(probeText));

        // Host/relay envelope: Worker delivers UserActionRequired (same path CompleteHosted uses).
        await brain.Client.SendAsync(
            worker.Id,
            new CompleteHostedBehaviorExecution(
                new AttemptRequest(task.Id, worker.Id, attempt, requirement.ParkRevision, goal),
                Succeeded: false,
                BehaviorExecutionCodes.UserActionRequired,
                Cancelled: false,
                requirement),
            cancellationToken);

        var waiting = await WaitForStateAsync(task, TaskState.Waiting, cancellationToken);
        Assert.Equal(attempt, waiting.ActiveAttempt);
        Assert.Equal(1, waiting.AttemptCount);
        var blocker = Assert.IsType<UserActionPending>(waiting.Blocker);
        Assert.Equal(requirement.ActionReference, blocker.ActionReference);
        Assert.Equal(requirement.ActionEpoch, blocker.ActionEpoch);
        Assert.Equal(requirement.Completer, blocker.Completer);

        var surface = ModuleUserActionBoundary.SerializeSafeSurface(requirement);
        Assert.False(ModuleUserActionBoundary.SurfaceContainsSecretFragments(surface));
        var snapshotJson = JsonSerializer.Serialize(waiting);
        Assert.DoesNotContain("https://", snapshotJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization_code", snapshotJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", snapshotJson, StringComparison.OrdinalIgnoreCase);

        // Production OAuth callback → MCP notifies durable bridge → Task Continue same attempt.
        var requiredFacts = await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken);
        var requiredFact = Assert.Single(requiredFacts).Synapse;
        Assert.Equal(commandId, requiredFact.CommandId);

        var callback = await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback(requiredFact.State, "oauth-code", Error: null, Iss: null),
            cancellationToken);
        Assert.True(callback.Accepted);
        Assert.True(callback.Completed);
        Assert.False(callback.Denied);

        var resumed = await WaitForStateAsync(task, TaskState.Running, cancellationToken);
        Assert.Equal(attempt, resumed.ActiveAttempt);
        Assert.Null(resumed.Blocker);
        Assert.Equal(waiting.Revision + 1, resumed.Revision);

        // Duplicate callback is one-shot: no second Continue / no re-park.
        var duplicate = await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback(requiredFact.State, "oauth-code", Error: null, Iss: null),
            cancellationToken);
        Assert.True(duplicate.Accepted);
        Assert.True(duplicate.Completed);
        var stillResumed = await task.Reference.Read();
        Assert.Equal(TaskState.Running, stillResumed.State);
        Assert.Equal(resumed.Revision, stillResumed.Revision);
        Assert.Null(stillResumed.Blocker);

        // After authorization, StageDispatch completes provider once.
        var responseRef = await dispatch.DispatchAsync(
            task.Id.Owner,
            task.Id,
            attempt,
            edge,
            requestRef,
            cancellationToken);
        Assert.NotEqual(Guid.Empty, responseRef.Id);
        Assert.Equal(1, AuthRequiringDispatchProbe.CountFor(probeText));

        // Forged session complete (session is not the bridge completer) fails closed — Task stays Running.
        await brain.Client.SendAsync(
            task.Id,
            new CompleteUserAction(
                CommandId.New(),
                requirement.ActionReference,
                requirement.ActionEpoch,
                requirement.ParkRevision),
            cancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        Assert.Equal(TaskState.Running, (await task.Reference.Read()).State);
    }

    [Fact(
        Timeout = 120_000,
        DisplayName =
            "COMPLETED MCP callback before Task park still resumes same attempt once after park; duplicate one-shot; provider once after auth")]
    public async Task CompletedCallbackBeforeParkResumesSameAttemptOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        AuthRequiringDispatchProbe.Reset();
        var probeText = $"auth-before-park-{Guid.NewGuid():N}";
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        CatalogGmail(brain);

        var worker = brain.Neuron<IWorker>("auth-before-park-worker");
        var task = brain.Neuron<ITask>("auth-before-park-task");
        var probe = brain.Neuron<IAuthRequiringProbe>("auth-before-park-probe");
        var auth = brain.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);

        var (goal, attempt, edge, requestRef, requirement, commandId) = await StageDispatchParkAsync(
            brain,
            worker,
            task,
            probe,
            probeText,
            caseId: "auth-before-park",
            protectedPayload: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            cancellationToken);

        Assert.Equal(0, AuthRequiringDispatchProbe.CountFor(probeText));
        Assert.Equal(TaskState.Running, (await task.Reference.Read()).State);

        var requiredFacts = await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken);
        var requiredFact = Assert.Single(requiredFacts).Synapse;
        Assert.Equal(commandId, requiredFact.CommandId);

        // Production OAuth can complete while Task is still Running (host park not applied yet).
        var callback = await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback(requiredFact.State, "oauth-code-before-park", Error: null, Iss: null),
            cancellationToken);
        Assert.True(callback.Accepted);
        Assert.True(callback.Completed);
        Assert.False(callback.Denied);
        Assert.Equal(0, AuthRequiringDispatchProbe.CountFor(probeText));

        // Let MCP→bridge→CompleteUserAction outbox drain while Task is still Running so the
        // pre-park completion is refused as NeuronAuthorizationException (permanent, not retried).
        await WaitWhileStillRunningAsync(task, settle: TimeSpan.FromSeconds(1), cancellationToken);
        Assert.Equal(TaskState.Running, (await task.Reference.Read()).State);
        Assert.Equal(0, AuthRequiringDispatchProbe.CountFor(probeText));

        await ApplyHostParkAsync(brain, worker, task, attempt, goal, requirement, cancellationToken);

        // Durable completion must apply after park — same attempt, one resume.
        var resumed = await WaitForStateAsync(task, TaskState.Running, cancellationToken);
        Assert.Equal(attempt, resumed.ActiveAttempt);
        Assert.Null(resumed.Blocker);
        Assert.Equal(1, resumed.AttemptCount);

        var duplicate = await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback(requiredFact.State, "oauth-code-before-park", Error: null, Iss: null),
            cancellationToken);
        Assert.True(duplicate.Accepted);
        Assert.True(duplicate.Completed);
        var stillResumed = await task.Reference.Read();
        Assert.Equal(TaskState.Running, stillResumed.State);
        Assert.Equal(resumed.Revision, stillResumed.Revision);
        Assert.Null(stillResumed.Blocker);

        var catalog = brain.Cluster.ClientServices.GetRequiredService<ActiveCapabilityCatalog>();
        var payloads = new GrainBehaviorProtectedPayloadAccess(brain.Cluster.Client);
        var dispatch = new GrainBehaviorCapabilityDispatchAccess(brain.Cluster.Client, catalog, payloads);
        var responseRef = await dispatch.DispatchAsync(
            task.Id.Owner,
            task.Id,
            attempt,
            edge,
            requestRef,
            cancellationToken);
        Assert.NotEqual(Guid.Empty, responseRef.Id);
        Assert.Equal(1, AuthRequiringDispatchProbe.CountFor(probeText));
    }

    [Fact(
        Timeout = 120_000,
        DisplayName =
            "DENIED MCP callback before Task park yields stable safe denial after park and never executes provider")]
    public async Task DeniedCallbackBeforeParkYieldsStableSafeDenial()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        AuthRequiringDispatchProbe.Reset();
        var probeText = $"auth-deny-before-park-{Guid.NewGuid():N}";
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        CatalogGmail(brain);

        var worker = brain.Neuron<IWorker>("auth-deny-before-park-worker");
        var task = brain.Neuron<ITask>("auth-deny-before-park-task");
        var probe = brain.Neuron<IAuthRequiringProbe>("auth-deny-before-park-probe");
        var auth = brain.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);

        var (goal, attempt, _, _, requirement, commandId) = await StageDispatchParkAsync(
            brain,
            worker,
            task,
            probe,
            probeText,
            caseId: "auth-deny-before-park",
            protectedPayload: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            cancellationToken);

        var requiredFacts = await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken);
        var requiredFact = Assert.Single(requiredFacts).Synapse;
        Assert.Equal(commandId, requiredFact.CommandId);

        var deniedCallback = await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback(requiredFact.State, Code: null, Error: "access_denied", Iss: null),
            cancellationToken);
        Assert.True(deniedCallback.Accepted);
        Assert.True(deniedCallback.Denied);
        Assert.Equal(0, AuthRequiringDispatchProbe.CountFor(probeText));

        // Settle pre-park DenyUserAction refuse (permanent) before host park.
        await WaitWhileStillRunningAsync(task, settle: TimeSpan.FromSeconds(1), cancellationToken);
        Assert.Equal(TaskState.Running, (await task.Reference.Read()).State);

        await ApplyHostParkAsync(brain, worker, task, attempt, goal, requirement, cancellationToken);

        var failed = await WaitForStateAsync(task, TaskState.Failed, cancellationToken);
        Assert.Null(failed.ActiveAttempt);
        Assert.Null(failed.Blocker);
        var failure = Assert.IsType<UserActionDenied>(failed.Failure);
        Assert.Equal("google.gmail", failure.ModuleId);
        Assert.Equal(0, AuthRequiringDispatchProbe.CountFor(probeText));

        var payload = JsonSerializer.Serialize(failed);
        Assert.DoesNotContain("oauth-code", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(requiredFact.State, payload, StringComparison.Ordinal);

        var second = await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback(requiredFact.State, Code: null, Error: "access_denied", Iss: null),
            cancellationToken);
        Assert.True(second.Denied);
        Assert.Equal(TaskState.Failed, (await task.Reference.Read()).State);
        Assert.Equal(0, AuthRequiringDispatchProbe.CountFor(probeText));
    }

    [Fact(
        Timeout = 90_000,
        DisplayName =
            "Unauthorized same-owner neuron cannot first-bind UserActionCompletionBridge; legitimate binder still binds")]
    public async Task UnauthorizedSameOwnerCannotFirstBindCompletionBridge()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        AuthRequiringDispatchProbe.Reset();
        var probeText = $"auth-bridge-bind-{Guid.NewGuid():N}";
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        CatalogGmail(brain);

        var worker = brain.Neuron<IWorker>("auth-bridge-bind-worker");
        var task = brain.Neuron<ITask>("auth-bridge-bind-task");
        var probe = brain.Neuron<IAuthRequiringProbe>("auth-bridge-bind-probe");
        var binder = brain.Neuron<IUnauthorizedUserActionBinder>("auth-bridge-bind-attacker");

        var activation = new BehaviorTaskActivation(
            new BehaviorId("com.digitalbrain.sample"),
            new BehaviorRevisionId("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
            contractVersion: "1",
            caseId: "auth-bridge-bind",
            protectedPayload: new ProtectedPayloadReference(Guid.Parse("55555555-5555-5555-5555-555555555555")),
            triggerTypeName: "SampleTrigger",
            capabilities: []);
        var goal = new BehaviorActivationGoal(
            activation.BehaviorId,
            activation.Revision,
            activation.ContractVersion,
            activation.CaseId,
            activation.ProtectedPayload,
            activation.TriggerTypeName,
            activation.Capabilities);

        _ = await task.Reference.Start(new StartTask(
            CommandId.New(),
            goal,
            worker.Id,
            new TaskPolicy(1, TimeSpan.Zero, null),
            Activation: activation));
        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        var running = await WaitForStateAsync(task, TaskState.Running, cancellationToken);
        var attempt = running.ActiveAttempt!.Value;

        var actionEpoch = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var forgedBind = new BindUserActionCompletion(
            task.Id,
            attempt,
            new NeuronId(AuthRequiringDispatchProbe.NeuronContractId, task.Id.Owner, probe.Id.Name),
            "google.gmail",
            new ProtectedPayloadReference(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), brain.Clock.UtcNow.AddHours(1)),
            actionEpoch,
            ParkRevision: running.Revision,
            ExpiresAt: brain.Clock.UtcNow.AddHours(1),
            CommandId.New(),
            "google.gmail",
            "forged-authorization-state");

        var unauthorized = await Assert.ThrowsAsync<NeuronAuthorizationException>(() =>
            binder.Reference.TryBindBridge(forgedBind, cancellationToken));
        Assert.False(string.IsNullOrWhiteSpace(unauthorized.Message));

        // Legitimate StageDispatch binder still creates and binds its own production bridge.
        var parkException = await Assert.ThrowsAsync<BehaviorUserActionRequiredException>(async () =>
        {
            var catalog = brain.Cluster.ClientServices.GetRequiredService<ActiveCapabilityCatalog>();
            var payloads = new GrainBehaviorProtectedPayloadAccess(brain.Cluster.Client);
            var dispatch = new GrainBehaviorCapabilityDispatchAccess(brain.Cluster.Client, catalog, payloads);
            var edge = new BehaviorCapabilityEdge(
                new NeuronId(AuthRequiringDispatchProbe.NeuronContractId, task.Id.Owner, probe.Id.Name),
                AuthRequiringDispatchProbe.RequestContractId,
                1,
                AuthRequiringDispatchProbe.ResponseContractId,
                1);
            var requestRef = await payloads.StoreAsync(
                task.Id.Owner,
                task.Id,
                attempt,
                BehaviorPayloadJson.Serialize(
                    new AuthRequiringProbeRequest(CommandId.New().Value, probeText),
                    typeof(AuthRequiringProbeRequest)),
                cancellationToken);
            await dispatch.DispatchAsync(
                task.Id.Owner,
                task.Id,
                attempt,
                edge,
                requestRef,
                cancellationToken).AsTask();
        });
        Assert.NotNull(parkException.Requirement);
        Assert.Equal(
            UserActionCompletionBridge.For(task.Id.Owner, parkException.Requirement!.ActionEpoch),
            parkException.Requirement.Completer);
        Assert.NotEqual(actionEpoch, parkException.Requirement.ActionEpoch);
        Assert.Equal(0, AuthRequiringDispatchProbe.CountFor(probeText));
    }

    [Fact(
        Timeout = 90_000,
        DisplayName =
            "Unauthorized same-owner neuron cannot bind or hijack MCP completion target after Begin; legitimate requester still binds")]
    public async Task UnauthorizedSameOwnerCannotHijackMcpCompletionTargetAfterBegin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        AuthRequiringDispatchProbe.Reset();
        var probeText = $"auth-mcp-hijack-{Guid.NewGuid():N}";
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        CatalogGmail(brain);

        var worker = brain.Neuron<IWorker>("auth-mcp-hijack-worker");
        var task = brain.Neuron<ITask>("auth-mcp-hijack-task");
        var probe = brain.Neuron<IAuthRequiringProbe>("auth-mcp-hijack-probe");
        var binder = brain.Neuron<IUnauthorizedUserActionBinder>("auth-mcp-hijack-attacker");
        var auth = brain.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);

        var pendingCommand = CommandId.New();
        var pendingState = $"pending-state-{Guid.NewGuid():N}";
        var begun = await auth.Reference.Begin(
            new BeginMcpAuthorization(
                pendingCommand,
                "google.gmail",
                "DigitalBrain Gmail",
                new Uri($"https://accounts.google.com/o/oauth2/v2/auth?state={pendingState}"),
                pendingState),
            cancellationToken);
        Assert.Equal(pendingCommand, begun.CommandId);
        Assert.Equal(pendingState, begun.State);

        var hijackTarget = UserActionCompletionBridge.For(
            task.Id.Owner,
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        var hijack = await Assert.ThrowsAsync<NeuronAuthorizationException>(() =>
            binder.Reference.TryBindCompletionTarget(
                new BindMcpAuthorizationCompletionTarget(pendingCommand, hijackTarget),
                cancellationToken));
        Assert.False(string.IsNullOrWhiteSpace(hijack.Message));

        // Legitimate StageDispatch requester still binds its own completion target and parks.
        var (_, _, _, _, requirement, _) = await StageDispatchParkAsync(
            brain,
            worker,
            task,
            probe,
            probeText,
            caseId: "auth-mcp-hijack",
            protectedPayload: Guid.Parse("66666666-6666-6666-6666-666666666666"),
            cancellationToken);
        Assert.Equal(
            UserActionCompletionBridge.For(task.Id.Owner, requirement.ActionEpoch),
            requirement.Completer);
        Assert.NotEqual(hijackTarget, requirement.Completer);
        Assert.Equal(0, AuthRequiringDispatchProbe.CountFor(probeText));
    }

    [Fact(
        Timeout = 120_000,
        DisplayName =
            "StageDispatch redelivery of same command/task/attempt after lost auth-required surface reproduces same completer/epoch/ref/revision, parks, and resumes once")]
    public async Task StageDispatchRedeliveryReproducesSameUserActionSurfaceAndResumesOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        AuthRequiringDispatchProbe.Reset();
        var probeText = $"auth-stage-redeliver-{Guid.NewGuid():N}";
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        CatalogGmail(brain);

        var worker = brain.Neuron<IWorker>("auth-stage-redeliver-worker");
        var task = brain.Neuron<ITask>("auth-stage-redeliver-task");
        var probe = brain.Neuron<IAuthRequiringProbe>("auth-stage-redeliver-probe");
        var auth = brain.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);

        var activation = new BehaviorTaskActivation(
            new BehaviorId("com.digitalbrain.sample"),
            new BehaviorRevisionId("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
            contractVersion: "1",
            caseId: "auth-stage-redeliver",
            protectedPayload: new ProtectedPayloadReference(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd")),
            triggerTypeName: "SampleTrigger",
            capabilities: []);
        var goal = new BehaviorActivationGoal(
            activation.BehaviorId,
            activation.Revision,
            activation.ContractVersion,
            activation.CaseId,
            activation.ProtectedPayload,
            activation.TriggerTypeName,
            activation.Capabilities);

        _ = await task.Reference.Start(new StartTask(
            CommandId.New(),
            goal,
            worker.Id,
            new TaskPolicy(1, TimeSpan.Zero, null),
            Activation: activation));
        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        var running = await WaitForStateAsync(task, TaskState.Running, cancellationToken);
        var attempt = running.ActiveAttempt!.Value;

        var catalog = brain.Cluster.ClientServices.GetRequiredService<ActiveCapabilityCatalog>();
        var payloads = new GrainBehaviorProtectedPayloadAccess(brain.Cluster.Client);
        var dispatch = new GrainBehaviorCapabilityDispatchAccess(brain.Cluster.Client, catalog, payloads);
        var edge = new BehaviorCapabilityEdge(
            new NeuronId(AuthRequiringDispatchProbe.NeuronContractId, task.Id.Owner, probe.Id.Name),
            AuthRequiringDispatchProbe.RequestContractId,
            1,
            AuthRequiringDispatchProbe.ResponseContractId,
            1);
        var commandId = CommandId.New();
        var requestRef = await payloads.StoreAsync(
            task.Id.Owner,
            task.Id,
            attempt,
            BehaviorPayloadJson.Serialize(
                new AuthRequiringProbeRequest(commandId.Value, probeText),
                typeof(AuthRequiringProbeRequest)),
            cancellationToken);

        // First StageDispatch persists MCP completion target + custody, then surface is "lost"
        // (host never parks / never accepts the first BehaviorUserActionRequiredException).
        var firstPark = await Assert.ThrowsAsync<BehaviorUserActionRequiredException>(() =>
            dispatch.DispatchAsync(
                task.Id.Owner,
                task.Id,
                attempt,
                edge,
                requestRef,
                cancellationToken).AsTask());
        Assert.NotNull(firstPark.Requirement);
        var firstRequirement = firstPark.Requirement!;
        Assert.Equal(task.Id, firstRequirement.Task);
        Assert.Equal(attempt, firstRequirement.Attempt);
        Assert.Equal("google.gmail", firstRequirement.ModuleId);
        Assert.Equal(
            UserActionCompletionBridge.For(task.Id.Owner, firstRequirement.ActionEpoch),
            firstRequirement.Completer);
        Assert.Equal(0, AuthRequiringDispatchProbe.CountFor(probeText));
        Assert.Equal(TaskState.Running, (await task.Reference.Read()).State);

        // Redeliver the exact same command/task/attempt/request — must not mint a conflicting
        // bridge epoch or throw on BindCompletionTarget; same safe surface/binding.
        var secondPark = await Assert.ThrowsAsync<BehaviorUserActionRequiredException>(() =>
            dispatch.DispatchAsync(
                task.Id.Owner,
                task.Id,
                attempt,
                edge,
                requestRef,
                cancellationToken).AsTask());
        Assert.NotNull(secondPark.Requirement);
        var secondRequirement = secondPark.Requirement!;
        Assert.Equal(firstRequirement.ActionEpoch, secondRequirement.ActionEpoch);
        Assert.Equal(firstRequirement.Completer, secondRequirement.Completer);
        Assert.Equal(firstRequirement.ActionReference, secondRequirement.ActionReference);
        Assert.Equal(firstRequirement.ParkRevision, secondRequirement.ParkRevision);
        Assert.Equal(firstRequirement.ModuleId, secondRequirement.ModuleId);
        Assert.Equal(firstRequirement.Task, secondRequirement.Task);
        Assert.Equal(firstRequirement.Attempt, secondRequirement.Attempt);
        Assert.Equal(0, AuthRequiringDispatchProbe.CountFor(probeText));
        Assert.Equal(TaskState.Running, (await task.Reference.Read()).State);

        await ApplyHostParkAsync(brain, worker, task, attempt, goal, secondRequirement, cancellationToken);
        var waiting = await WaitForStateAsync(task, TaskState.Waiting, cancellationToken);
        Assert.Equal(attempt, waiting.ActiveAttempt);
        Assert.Equal(1, waiting.AttemptCount);
        var blocker = Assert.IsType<UserActionPending>(waiting.Blocker);
        Assert.Equal(secondRequirement.ActionReference, blocker.ActionReference);
        Assert.Equal(secondRequirement.ActionEpoch, blocker.ActionEpoch);
        Assert.Equal(secondRequirement.Completer, blocker.Completer);

        var requiredFacts = await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken);
        var requiredFact = Assert.Single(requiredFacts).Synapse;
        Assert.Equal(commandId, requiredFact.CommandId);

        var callback = await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback(requiredFact.State, "oauth-code-redeliver", Error: null, Iss: null),
            cancellationToken);
        Assert.True(callback.Accepted);
        Assert.True(callback.Completed);
        Assert.False(callback.Denied);

        var resumed = await WaitForStateAsync(task, TaskState.Running, cancellationToken);
        Assert.Equal(attempt, resumed.ActiveAttempt);
        Assert.Null(resumed.Blocker);
        Assert.Equal(waiting.Revision + 1, resumed.Revision);

        var duplicate = await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback(requiredFact.State, "oauth-code-redeliver", Error: null, Iss: null),
            cancellationToken);
        Assert.True(duplicate.Accepted);
        Assert.True(duplicate.Completed);
        var stillResumed = await task.Reference.Read();
        Assert.Equal(TaskState.Running, stillResumed.State);
        Assert.Equal(resumed.Revision, stillResumed.Revision);
        Assert.Null(stillResumed.Blocker);

        var responseRef = await dispatch.DispatchAsync(
            task.Id.Owner,
            task.Id,
            attempt,
            edge,
            requestRef,
            cancellationToken);
        Assert.NotEqual(Guid.Empty, responseRef.Id);
        Assert.Equal(1, AuthRequiringDispatchProbe.CountFor(probeText));
    }

    [Fact(
        Timeout = 180_000,
        DisplayName =
            "COMPLETED callback durable across host reactivation still resumes same attempt after later park")]
    public async Task CompletedCallbackSurvivesHostReactivationThenResumesAfterPark()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        AuthRequiringDispatchProbe.Reset();
        var probeText = $"auth-durable-before-park-{Guid.NewGuid():N}";
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        CatalogGmail(brain);

        var worker = brain.Neuron<IWorker>("auth-durable-before-park-worker");
        var task = brain.Neuron<ITask>("auth-durable-before-park-task");
        var probe = brain.Neuron<IAuthRequiringProbe>("auth-durable-before-park-probe");
        var auth = brain.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);

        var (goal, attempt, edge, requestRef, requirement, commandId) = await StageDispatchParkAsync(
            brain,
            worker,
            task,
            probe,
            probeText,
            caseId: "auth-durable-before-park",
            protectedPayload: Guid.Parse("77777777-7777-7777-7777-777777777777"),
            cancellationToken);

        var requiredFacts = await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken);
        var requiredFact = Assert.Single(requiredFacts).Synapse;
        Assert.Equal(commandId, requiredFact.CommandId);

        var callback = await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback(requiredFact.State, "oauth-code-durable", Error: null, Iss: null),
            cancellationToken);
        Assert.True(callback.Accepted);
        Assert.True(callback.Completed);

        await WaitWhileStillRunningAsync(task, settle: TimeSpan.FromSeconds(1), cancellationToken);
        Assert.Equal(TaskState.Running, (await task.Reference.Read()).State);

        // Deactivate/restart hosting silo between pending completion and park to force durable reload.
        // Limitation: RestartHostAsync restarts the whole silo that hosts the grain (Orleans test cluster),
        // not a single-grain deactivation; multi-grain reactivation order is harness-defined. Strongest
        // durable-state ordering available without a production single-grain deactivate seam.
        await auth.RestartHostAsync(cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);

        // Re-resolve after silo restart (same owner scope / fixture lease).
        task = brain.Neuron<ITask>("auth-durable-before-park-task");
        worker = brain.Neuron<IWorker>("auth-durable-before-park-worker");

        await ApplyHostParkAsync(brain, worker, task, attempt, goal, requirement, cancellationToken);

        await brain.Clock.AdvanceAsync(TimeSpan.FromMinutes(1), cancellationToken);

        var resumed = await WaitForStateAsync(task, TaskState.Running, cancellationToken);
        Assert.Equal(attempt, resumed.ActiveAttempt);
        Assert.Null(resumed.Blocker);
        Assert.Equal(1, resumed.AttemptCount);

        var catalog = brain.Cluster.ClientServices.GetRequiredService<ActiveCapabilityCatalog>();
        var payloads = new GrainBehaviorProtectedPayloadAccess(brain.Cluster.Client);
        var dispatch = new GrainBehaviorCapabilityDispatchAccess(brain.Cluster.Client, catalog, payloads);
        var responseRef = await dispatch.DispatchAsync(
            task.Id.Owner,
            task.Id,
            attempt,
            edge,
            requestRef,
            cancellationToken);
        Assert.NotEqual(Guid.Empty, responseRef.Id);
        Assert.Equal(1, AuthRequiringDispatchProbe.CountFor(probeText));
    }

    [Fact(
        Timeout = 120_000,
        DisplayName =
            "Successful directed AuthorizationCompleted resume leaves no undeliverable/retrying bridge-broadcast entry in the MCP durable outbox")]
    public async Task SuccessfulDirectedAuthorizationCompletionLeavesNoRetryingBridgeBroadcastInMcpOutbox()
    {
        // MCP both EmitAsync-broadcasts AuthorizationCompleted and directs the epoch-bound bridge.
        // Broadcast catalog delivery targets user-action-completion-bridge/{owner}/{correlation-D},
        // not the bound epoch instance. Unbound throws retryable InvalidOperationException today,
        // retaining a doomed MCP outbox entry for the 30m/1000-attempt horizon. Denied shares the
        // same EmitAsync+NotifyCompletionTargetAsync path, so one completed representative covers both.
        var cancellationToken = TestContext.Current.CancellationToken;
        AuthRequiringDispatchProbe.Reset();
        var probeText = $"auth-outbox-broadcast-{Guid.NewGuid():N}";
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        CatalogGmail(brain);

        var worker = brain.Neuron<IWorker>("auth-outbox-broadcast-worker");
        var task = brain.Neuron<ITask>("auth-outbox-broadcast-task");
        var probe = brain.Neuron<IAuthRequiringProbe>("auth-outbox-broadcast-probe");
        var auth = brain.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);

        var (goal, attempt, _, _, requirement, commandId) = await StageDispatchParkAsync(
            brain,
            worker,
            task,
            probe,
            probeText,
            caseId: "auth-outbox-broadcast",
            protectedPayload: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            cancellationToken);

        await ApplyHostParkAsync(brain, worker, task, attempt, goal, requirement, cancellationToken);
        var waiting = await WaitForStateAsync(task, TaskState.Waiting, cancellationToken);
        Assert.Equal(attempt, waiting.ActiveAttempt);

        var requiredFacts = await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken);
        var requiredFact = Assert.Single(requiredFacts).Synapse;
        Assert.Equal(commandId, requiredFact.CommandId);

        var callback = await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback(requiredFact.State, "oauth-code-outbox", Error: null, Iss: null),
            cancellationToken);
        Assert.True(callback.Accepted);
        Assert.True(callback.Completed);
        Assert.False(callback.Denied);

        var resumed = await WaitForStateAsync(task, TaskState.Running, cancellationToken);
        Assert.Equal(attempt, resumed.ActiveAttempt);
        Assert.Null(resumed.Blocker);

        // Structural dual-path: Emit journals AuthorizationCompleted; directed notify resumed the Task.
        var completedFacts = await auth.Outgoing.ReadAsync<AuthorizationCompleted>(afterSequence: 0, cancellationToken);
        Assert.Contains(completedFacts, fact => fact.Synapse.CommandId == commandId);

        // Drain settle: successful path must leave MCP outbox empty (wakeup disarmed), not retrying
        // an undeliverable bridge-broadcast receiver for the ordinary outbox horizon.
        await brain.Clock.AdvanceAsync(TimeSpan.FromSeconds(2), cancellationToken);
        Assert.False(
            await brain.HasOutboxWakeupAsync(auth.Id),
            "Successful directed authorization completion must not leave a retrying undeliverable bridge-broadcast entry in the MCP durable outbox.");
    }

    [Fact(
        Timeout = 120_000,
        DisplayName =
            "AuthorizationCompleted after bridge expiry is permanently refused (NeuronAuthorizationException), not retryable InvalidOperationException")]
    public async Task AuthorizationCompletedAfterBridgeExpiryIsPermanentlyRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        AuthRequiringDispatchProbe.Reset();
        var probeText = $"auth-bridge-expiry-{Guid.NewGuid():N}";
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        CatalogGmail(brain);

        var worker = brain.Neuron<IWorker>("auth-bridge-expiry-worker");
        var task = brain.Neuron<ITask>("auth-bridge-expiry-task");
        var probe = brain.Neuron<IAuthRequiringProbe>("auth-bridge-expiry-probe");
        var auth = brain.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);
        var dispositionProbe = brain.Neuron<IUserActionBridgeDispositionProbe>("auth-bridge-expiry-disposition");

        var (_, _, _, _, requirement, commandId) = await StageDispatchParkAsync(
            brain,
            worker,
            task,
            probe,
            probeText,
            caseId: "auth-bridge-expiry",
            protectedPayload: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            cancellationToken);

        var requiredFacts = await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken);
        var requiredFact = Assert.Single(requiredFacts).Synapse;
        Assert.Equal(commandId, requiredFact.CommandId);

        var bridgeId = requirement.Completer;
        Assert.Equal(UserActionCompletionBridge.For(task.Id.Owner, requirement.ActionEpoch), bridgeId);
        Assert.True(requirement.ExpiresAt > brain.Clock.UtcNow);

        var pastExpiry = requirement.ExpiresAt - brain.Clock.UtcNow + TimeSpan.FromSeconds(1);
        await brain.Clock.AdvanceAsync(pastExpiry, cancellationToken);
        Assert.True(brain.Clock.UtcNow > requirement.ExpiresAt);

        var probeId = Guid.NewGuid();
        UserActionBridgeDispositionProbe.Clear(probeId);
        await dispositionProbe.Reference.ProbeAuthorizationCompleted(
            probeId,
            bridgeId,
            requiredFact.CommandId,
            requiredFact.ServerKey,
            requiredFact.State,
            cancellationToken);

        var disposition = await WaitForDispositionAsync(probeId, cancellationToken);
        Assert.StartsWith(
            $"{nameof(NeuronAuthorizationException)}:",
            disposition,
            StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), disposition, StringComparison.Ordinal);
    }

    [Fact(
        Timeout = 180_000,
        DisplayName =
            "Bridge AuthorizationCompleted turn that fails after terminal Outcome mutation still resumes Task exactly once after redelivery (turn state+outbox atomicity)")]
    public async Task BridgeTurnFailureAfterTerminalOutcomeStillResumesTaskExactlyOnce()
    {
        // Behavioral transaction boundary: FailNextJournalCommit fires on the bridge turn's final
        // CommitAsync (WriteStateAsync) after terminal Outcome and CompleteUserAction are staged
        // for that turn — not an intermediate SaveAsync. Turn retraction discards staged
        // outbox/journals and in-memory Outcome staging. Expected GREEN: Task resumes exactly once
        // after redelivery recovers the completion.
        var cancellationToken = TestContext.Current.CancellationToken;
        AuthRequiringDispatchProbe.Reset();
        var probeText = $"auth-bridge-atomicity-{Guid.NewGuid():N}";
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        CatalogGmail(brain);

        var worker = brain.Neuron<IWorker>("auth-bridge-atomicity-worker");
        var task = brain.Neuron<ITask>("auth-bridge-atomicity-task");
        var probe = brain.Neuron<IAuthRequiringProbe>("auth-bridge-atomicity-probe");
        var auth = brain.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);

        var (goal, attempt, _, _, requirement, commandId) = await StageDispatchParkAsync(
            brain,
            worker,
            task,
            probe,
            probeText,
            caseId: "auth-bridge-atomicity",
            protectedPayload: Guid.Parse("88888888-8888-8888-8888-888888888888"),
            cancellationToken);

        await ApplyHostParkAsync(brain, worker, task, attempt, goal, requirement, cancellationToken);
        var waiting = await WaitForStateAsync(task, TaskState.Waiting, cancellationToken);
        Assert.Equal(attempt, waiting.ActiveAttempt);

        var requiredFacts = await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken);
        var requiredFact = Assert.Single(requiredFacts).Synapse;
        Assert.Equal(commandId, requiredFact.CommandId);

        var bridgeId = requirement.Completer;
        Assert.Equal(UserActionCompletionBridge.For(task.Id.Owner, requirement.ActionEpoch), bridgeId);

        await using (var fault = brain.ArmJournalFault(
            bridgeId,
            "bridge turn final CommitAsync fails after terminal Outcome and completion outbox are staged"))
        {
            var callback = await auth.Reference.DeliverCallback(
                new DeliverMcpAuthorizationCallback(requiredFact.State, "oauth-code-atomicity", Error: null, Iss: null),
                cancellationToken);
            Assert.True(callback.Accepted);
            Assert.True(callback.Completed);

            var faultDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (!fault.IsConsumed && DateTime.UtcNow < faultDeadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
            }

            Assert.True(
                fault.IsConsumed,
                "Bridge journal fault must fire on the turn's final CommitAsync during AuthorizationCompleted.");
        }

        // Allow MCP outbox redelivery after the failed bridge turn; bridge host reactivation forces
        // durable state reload so recovery cannot rely on in-memory staging alone.
        await brain.Clock.AdvanceAsync(TimeSpan.FromSeconds(2), cancellationToken);
        await brain.RestartHostAsync(bridgeId, cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);

        task = brain.Neuron<ITask>("auth-bridge-atomicity-task");
        auth = brain.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);

        var redelivery = await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback(requiredFact.State, "oauth-code-atomicity", Error: null, Iss: null),
            cancellationToken);
        Assert.True(redelivery.Accepted);
        Assert.True(redelivery.Completed);

        await brain.Clock.AdvanceAsync(TimeSpan.FromSeconds(2), cancellationToken);

        var resumed = await WaitForStateAsync(task, TaskState.Running, cancellationToken);
        Assert.Equal(attempt, resumed.ActiveAttempt);
        Assert.Null(resumed.Blocker);
        Assert.Equal(1, resumed.AttemptCount);
        Assert.Equal(waiting.Revision + 1, resumed.Revision);

        var stillResumed = await task.Reference.Read();
        Assert.Equal(TaskState.Running, stillResumed.State);
        Assert.Equal(resumed.Revision, stillResumed.Revision);
        Assert.Equal(0, AuthRequiringDispatchProbe.CountFor(probeText));
    }

    [Fact(
        Timeout = 180_000,
        DisplayName =
            "COMPLETED callback before park still resumes once after park even when ordinary outbox retry horizon is exhausted")]
    public async Task CompletedCallbackBeforeParkResumesAfterRetryHorizonExhausted()
    {
        // Delayed park beyond DeliveryPolicy.RetryHorizon (30m): callback first queues
        // CompleteUserAction while Task is still Running; durable rendezvous must resume after park
        // even when the ordinary outbox retry window is exhausted.
        var cancellationToken = TestContext.Current.CancellationToken;
        AuthRequiringDispatchProbe.Reset();
        var probeText = $"auth-retry-horizon-{Guid.NewGuid():N}";
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        CatalogGmail(brain);

        var worker = brain.Neuron<IWorker>("auth-retry-horizon-worker");
        var task = brain.Neuron<ITask>("auth-retry-horizon-task");
        var probe = brain.Neuron<IAuthRequiringProbe>("auth-retry-horizon-probe");
        var auth = brain.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);

        var (goal, attempt, edge, requestRef, requirement, commandId) = await StageDispatchParkAsync(
            brain,
            worker,
            task,
            probe,
            probeText,
            caseId: "auth-retry-horizon",
            protectedPayload: Guid.Parse("99999999-9999-9999-9999-999999999999"),
            cancellationToken);

        Assert.Equal(TaskState.Running, (await task.Reference.Read()).State);

        var requiredFacts = await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken);
        var requiredFact = Assert.Single(requiredFacts).Synapse;
        Assert.Equal(commandId, requiredFact.CommandId);

        var callback = await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback(requiredFact.State, "oauth-code-horizon", Error: null, Iss: null),
            cancellationToken);
        Assert.True(callback.Accepted);
        Assert.True(callback.Completed);
        Assert.False(callback.Denied);

        // Let CompleteUserAction outbox attempts accumulate while still Running.
        await WaitWhileStillRunningAsync(task, settle: TimeSpan.FromSeconds(2), cancellationToken);
        Assert.Equal(TaskState.Running, (await task.Reference.Read()).State);

        // Past ordinary outbox RetryHorizon (30 minutes) — abandoned under current policy.
        await brain.Clock.AdvanceAsync(TimeSpan.FromMinutes(31), cancellationToken);
        await WaitWhileStillRunningAsync(task, settle: TimeSpan.FromSeconds(2), cancellationToken);
        Assert.Equal(TaskState.Running, (await task.Reference.Read()).State);

        await ApplyHostParkAsync(brain, worker, task, attempt, goal, requirement, cancellationToken);

        // Give any post-park redrive/rendezvous a chance to fire (clock + outbox timer).
        await brain.Clock.AdvanceAsync(TimeSpan.FromSeconds(5), cancellationToken);

        var resumed = await WaitForStateAsync(task, TaskState.Running, cancellationToken);
        Assert.Equal(attempt, resumed.ActiveAttempt);
        Assert.Null(resumed.Blocker);
        Assert.Equal(1, resumed.AttemptCount);

        var catalog = brain.Cluster.ClientServices.GetRequiredService<ActiveCapabilityCatalog>();
        var payloads = new GrainBehaviorProtectedPayloadAccess(brain.Cluster.Client);
        var dispatch = new GrainBehaviorCapabilityDispatchAccess(brain.Cluster.Client, catalog, payloads);
        var responseRef = await dispatch.DispatchAsync(
            task.Id.Owner,
            task.Id,
            attempt,
            edge,
            requestRef,
            cancellationToken);
        Assert.NotEqual(Guid.Empty, responseRef.Id);
        Assert.Equal(1, AuthRequiringDispatchProbe.CountFor(probeText));
    }

    [Fact(
        Timeout = 90_000,
        DisplayName = "Denied MCP callback produces stable UserActionDenied Task result without provider secrets")]
    public async Task DeniedCallbackProducesStableSafeTaskFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        AuthRequiringDispatchProbe.Reset();
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        CatalogGmail(brain);

        var worker = brain.Neuron<IWorker>("auth-deny-worker");
        var task = brain.Neuron<ITask>("auth-deny-task");
        var probe = brain.Neuron<IAuthRequiringProbe>("auth-deny-probe");
        var auth = brain.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);

        var activation = new BehaviorTaskActivation(
            new BehaviorId("com.digitalbrain.sample"),
            new BehaviorRevisionId("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
            contractVersion: "1",
            caseId: "auth-deny",
            protectedPayload: new ProtectedPayloadReference(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            triggerTypeName: "SampleTrigger",
            capabilities: []);
        var goal = new BehaviorActivationGoal(
            activation.BehaviorId,
            activation.Revision,
            activation.ContractVersion,
            activation.CaseId,
            activation.ProtectedPayload,
            activation.TriggerTypeName,
            activation.Capabilities);

        _ = await task.Reference.Start(new StartTask(
            CommandId.New(),
            goal,
            worker.Id,
            new TaskPolicy(1, TimeSpan.Zero, null),
            Activation: activation));
        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        var running = await WaitForStateAsync(task, TaskState.Running, cancellationToken);
        var attempt = running.ActiveAttempt!.Value;

        var catalog = brain.Cluster.ClientServices.GetRequiredService<ActiveCapabilityCatalog>();
        var payloads = new GrainBehaviorProtectedPayloadAccess(brain.Cluster.Client);
        var dispatch = new GrainBehaviorCapabilityDispatchAccess(brain.Cluster.Client, catalog, payloads);

        var edge = new BehaviorCapabilityEdge(
            new NeuronId(AuthRequiringDispatchProbe.NeuronContractId, task.Id.Owner, probe.Id.Name),
            AuthRequiringDispatchProbe.RequestContractId,
            1,
            AuthRequiringDispatchProbe.ResponseContractId,
            1);
        var commandId = CommandId.New();
        var requestRef = await payloads.StoreAsync(
            task.Id.Owner,
            task.Id,
            attempt,
            BehaviorPayloadJson.Serialize(
                new AuthRequiringProbeRequest(commandId.Value, "deny-path"),
                typeof(AuthRequiringProbeRequest)),
            cancellationToken);

        var parkException = await Assert.ThrowsAsync<BehaviorUserActionRequiredException>(() =>
            dispatch.DispatchAsync(
                task.Id.Owner,
                task.Id,
                attempt,
                edge,
                requestRef,
                cancellationToken).AsTask());
        var requirement = parkException.Requirement!;

        await brain.Client.SendAsync(
            worker.Id,
            new CompleteHostedBehaviorExecution(
                new AttemptRequest(task.Id, worker.Id, attempt, requirement.ParkRevision, goal),
                Succeeded: false,
                BehaviorExecutionCodes.UserActionRequired,
                Cancelled: false,
                requirement),
            cancellationToken);

        _ = await WaitForStateAsync(task, TaskState.Waiting, cancellationToken);
        var requiredFacts = await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken);
        var requiredFact = Assert.Single(requiredFacts).Synapse;

        var deniedCallback = await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback(requiredFact.State, Code: null, Error: "access_denied", Iss: null),
            cancellationToken);
        Assert.True(deniedCallback.Accepted);
        Assert.True(deniedCallback.Denied);

        var failed = await WaitForStateAsync(task, TaskState.Failed, cancellationToken);
        Assert.Null(failed.ActiveAttempt);
        Assert.Null(failed.Blocker);
        var failure = Assert.IsType<UserActionDenied>(failed.Failure);
        Assert.Equal("google.gmail", failure.ModuleId);

        var payload = JsonSerializer.Serialize(failed);
        Assert.DoesNotContain("oauth-code", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(requiredFact.State, payload, StringComparison.Ordinal);

        var second = await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback(requiredFact.State, Code: null, Error: "access_denied", Iss: null),
            cancellationToken);
        Assert.True(second.Denied);
        Assert.Equal(TaskState.Failed, (await task.Reference.Read()).State);
    }

    private static void CatalogGmail(TestBrain test)
    {
        ArgumentNullException.ThrowIfNull(test);
        GmailHelpers.CatalogSampleMessage(test);
    }

    private static async Task<(
        BehaviorActivationGoal Goal,
        AttemptId Attempt,
        BehaviorCapabilityEdge Edge,
        ProtectedPayloadReference RequestRef,
        UserActionRequired Requirement,
        CommandId CommandId)> StageDispatchParkAsync(
        TestBrain brain,
        TestNeuron<IWorker> worker,
        TestNeuron<ITask> task,
        TestNeuron<IAuthRequiringProbe> probe,
        string probeText,
        string caseId,
        Guid protectedPayload,
        CancellationToken cancellationToken)
    {
        var activation = new BehaviorTaskActivation(
            new BehaviorId("com.digitalbrain.sample"),
            new BehaviorRevisionId("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
            contractVersion: "1",
            caseId: caseId,
            protectedPayload: new ProtectedPayloadReference(protectedPayload),
            triggerTypeName: "SampleTrigger",
            capabilities: []);
        var goal = new BehaviorActivationGoal(
            activation.BehaviorId,
            activation.Revision,
            activation.ContractVersion,
            activation.CaseId,
            activation.ProtectedPayload,
            activation.TriggerTypeName,
            activation.Capabilities);

        _ = await task.Reference.Start(new StartTask(
            CommandId.New(),
            goal,
            worker.Id,
            new TaskPolicy(1, TimeSpan.Zero, null),
            Activation: activation));
        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        var running = await WaitForStateAsync(task, TaskState.Running, cancellationToken);
        var attempt = running.ActiveAttempt!.Value;

        var catalog = brain.Cluster.ClientServices.GetRequiredService<ActiveCapabilityCatalog>();
        var payloads = new GrainBehaviorProtectedPayloadAccess(brain.Cluster.Client);
        var dispatch = new GrainBehaviorCapabilityDispatchAccess(brain.Cluster.Client, catalog, payloads);
        var edge = new BehaviorCapabilityEdge(
            new NeuronId(AuthRequiringDispatchProbe.NeuronContractId, task.Id.Owner, probe.Id.Name),
            AuthRequiringDispatchProbe.RequestContractId,
            1,
            AuthRequiringDispatchProbe.ResponseContractId,
            1);
        var commandId = CommandId.New();
        var requestRef = await payloads.StoreAsync(
            task.Id.Owner,
            task.Id,
            attempt,
            BehaviorPayloadJson.Serialize(
                new AuthRequiringProbeRequest(commandId.Value, probeText),
                typeof(AuthRequiringProbeRequest)),
            cancellationToken);

        var parkException = await Assert.ThrowsAsync<BehaviorUserActionRequiredException>(() =>
            dispatch.DispatchAsync(
                task.Id.Owner,
                task.Id,
                attempt,
                edge,
                requestRef,
                cancellationToken).AsTask());
        Assert.NotNull(parkException.Requirement);
        var requirement = parkException.Requirement!;
        Assert.Equal(task.Id, requirement.Task);
        Assert.Equal(attempt, requirement.Attempt);
        Assert.Equal("google.gmail", requirement.ModuleId);
        Assert.Equal(UserActionCompletionBridge.For(task.Id.Owner, requirement.ActionEpoch), requirement.Completer);
        return (goal, attempt, edge, requestRef, requirement, commandId);
    }

    private static async Task ApplyHostParkAsync(
        TestBrain brain,
        TestNeuron<IWorker> worker,
        TestNeuron<ITask> task,
        AttemptId attempt,
        BehaviorActivationGoal goal,
        UserActionRequired requirement,
        CancellationToken cancellationToken)
    {
        await brain.Client.SendAsync(
            worker.Id,
            new CompleteHostedBehaviorExecution(
                new AttemptRequest(task.Id, worker.Id, attempt, requirement.ParkRevision, goal),
                Succeeded: false,
                BehaviorExecutionCodes.UserActionRequired,
                Cancelled: false,
                requirement),
            cancellationToken);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await task.Reference.Read();
            if (snapshot.State == TaskState.Waiting
                && snapshot.ActiveAttempt == attempt
                && snapshot.Blocker is UserActionPending blocker
                && blocker.ActionReference == requirement.ActionReference
                && blocker.ActionEpoch == requirement.ActionEpoch
                && blocker.Completer == requirement.Completer)
            {
                return;
            }

            if (snapshot.State == TaskState.Running
                && snapshot.ActiveAttempt == attempt
                && snapshot.Blocker is null
                && snapshot.Revision > requirement.ParkRevision)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }

        var final = await task.Reference.Read();
        throw new TimeoutException(
            $"Timed out waiting for park (Waiting) or park-then-resume (Running, rev>{requirement.ParkRevision}). Final: {final.State} rev={final.Revision}.");
    }

    private static async Task WaitWhileStillRunningAsync(
        TestNeuron<ITask> task,
        TimeSpan settle,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + settle;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await task.Reference.Read();
            if (snapshot.State != TaskState.Running)
            {
                throw new InvalidOperationException(
                    $"Expected Task to remain Running while pre-park callback settled; observed {snapshot.State}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }
    }

    private static async Task<string> WaitForDispositionAsync(Guid probeId, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (UserActionBridgeDispositionProbe.TryRead(probeId, out var disposition)
                && !string.IsNullOrWhiteSpace(disposition))
            {
                return disposition!;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }

        throw new TimeoutException($"Bridge disposition probe '{probeId}' did not record a result.");
    }

    private static async Task<TaskSnapshot> WaitForStateAsync(
        TestNeuron<ITask> task,
        TaskState expected,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await task.Reference.Read();
            if (snapshot.State == expected)
            {
                return snapshot;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }

        var final = await task.Reference.Read();
        throw new TimeoutException($"Timed out waiting for {expected}. Final state: {final.State}");
    }

    private static async Task<RunningHost> StartHostAsync(
        IBehaviorCapabilityDispatchAccess dispatch,
        IBehaviorProtectedPayloadAccess payloads,
        CancellationToken cancellationToken)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [BehaviorBrokerContract.CredentialConfigurationKey] = BrokerCredential,
            })
            .Build();

        var port = GetFreeTcpPort();
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(UserActionProductionRail).Assembly.FullName,
        });
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, port));
        builder.Services.AddRouting();
        builder.Services.AddSingleton(configuration);
        builder.Services.AddSingleton<IConfiguration>(configuration);
        builder.Services.AddBehaviorBrokerAuthentication(configuration);
        builder.Services.AddSingleton(dispatch);
        builder.Services.AddSingleton(payloads);
        builder.Services.AddSingleton<IBehaviorTaskOperationAccess, NoOpTaskOperations>();
        var app = builder.Build();
        app.UseRouting();
        app.UseBehaviorBrokerAuthentication();
        app.MapBehaviorProtectedPayloadBroker();
        app.MapBehaviorDispatchBroker();
        await app.StartAsync(cancellationToken);
        return new RunningHost(app, new Uri($"http://127.0.0.1:{port}"));

        static int GetFreeTcpPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }

    private sealed class NoOpTaskOperations : IBehaviorTaskOperationAccess
    {
        public ValueTask<TaskOperationSnapshot> PrepareAsync(
            OwnerId owner,
            NeuronId task,
            AttemptId attempt,
            int sequence,
            TaskOperationEdge edge,
            ProtectedPayloadReference requestPayload,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ValueTask<ReadTaskOperationResult> ReadAsync(
            OwnerId owner,
            NeuronId task,
            AttemptId attempt,
            int sequence,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ValueTask<TaskOperationSnapshot> TransitionAsync(
            OwnerId owner,
            NeuronId task,
            AttemptId attempt,
            int sequence,
            TaskOperationPhase expectedPhase,
            TaskOperationPhase phase,
            ProtectedPayloadReference? responsePayload,
            string? redactedSummary,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class RunningHost(WebApplication app, Uri baseAddress) : IAsyncDisposable
    {
        public HttpClient CreateClient() => new() { BaseAddress = baseAddress };

        public ValueTask DisposeAsync() => app.DisposeAsync();
    }
}

public sealed class UserActionProductionRailFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<BehaviorsModule>();
        brain.AddModule<TasksModule>();
        brain.AddModule<GoogleModule>();
        brain.AddModule<AuthRequiringDispatchModule>();
        brain.AddModule<UnauthorizedUserActionBinderModule>();
        brain.AddModule<UserActionBridgeDispositionProbeModule>();
        brain.AddModule<IntegrationsHarnessModule>();
        brain.ConfigureMcpEdge();
        brain.Configure(McpRuntimeHosting.AuthorizationModeKey, McpRuntimeHosting.EdgeMode);
        brain.Configure(McpRuntimeHosting.PublicSignInBaseKey, AuthorizationRailFixture.PublicSignInBase);
        // In-process executor leaves Accept Running (InProcessClosed) so StageDispatch can park.
        brain.Configure(BehaviorsModule.ExecutorConfigurationKey, BehaviorsModule.InProcessExecutorName);
    }
}
