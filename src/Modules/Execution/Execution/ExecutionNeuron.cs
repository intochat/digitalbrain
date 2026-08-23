using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Execution;

[GrainType("execution")]
public sealed class ExecutionNeuron : Neuron, IExecution
{
    private const string StateName = "execution.state";

    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<ExecutionState> _states;
    private readonly EffectBroker _broker;
    private readonly IScriptDriver _scriptDriver;
    private readonly IExecutionContextProvider[] _providers;

    public ExecutionNeuron()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _states = ServiceProvider.GetRequiredService<Serializer<ExecutionState>>();
        _broker = ServiceProvider.GetRequiredService<EffectBroker>();
        _scriptDriver = ServiceProvider.GetRequiredService<IScriptDriver>();
        _providers = [.. ServiceProvider.GetServices<IExecutionContextProvider>()];
    }

    public Task<ExecutionProjection> Read()
    {
        var data = LoadRecorded()
            ?? throw new InvalidOperationException($"Execution '{Id}' has not been started.");
        return Task.FromResult(
            new ExecutionProjection(
                data.ExecutionId,
                data.Status,
                data.Driver,
                data.Workload,
                data.PromptBlocks));
    }

    public async Task HandleAsync(StartExecution synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);
        RequireMatchingExecution(synapse.ExecutionId);

        var current = LoadRecorded();
        if (current is { Status: ExecutionStatus.Running or ExecutionStatus.AwaitingApproval })
        {
            throw new NeuronAuthorizationException(
                $"Execution '{Id}' is already active with status '{current.Status}'.");
        }

        var grants = synapse.Grants is null ? Array.Empty<CapabilityId>() : synapse.Grants.ToArray();
        Stage(new ExecutionState(
            synapse.ExecutionId,
            ExecutionStatus.Running,
            synapse.Driver,
            synapse.Workload,
            grants));

        var session = new ExecutionSession(synapse.ExecutionId, Id.Owner, GrainFactory, _broker, grants);
        var context = GrainFactory.GetGrain<IExecutionContext>(
            EntityId.For<IExecutionContext>(Id.Owner, synapse.ExecutionId.ToString()).ToGrainId());

        try
        {
            await context.Ensure(synapse.ExecutionId)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            await AdmitRelatedContextAsync(context, synapse.RelatedExecutions, cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            var seed = new ExecutionSeedBuilder(
                synapse.ExecutionId,
                Id.Owner,
                synapse.Workload,
                synapse.RelatedExecutions ?? []);
            for (var providerIndex = 0; providerIndex < _providers.Length; providerIndex++)
            {
                await _providers[providerIndex]
                    .ContributeAsync(seed, cancellationToken)
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }

            for (var deltaIndex = 0; deltaIndex < seed.SeedDeltas.Count; deltaIndex++)
            {
                await session.ApplyDeltaAsync(seed.SeedDeltas[deltaIndex])
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }

            var promptBlocks = seed.PromptBlocks.Count == 0
                ? null
                : (IReadOnlyList<string>)seed.PromptBlocks.ToArray();
            Stage(LoadRecorded()! with { PromptBlocks = promptBlocks });

            // Script never runs the in-neuron agent/team loop — IScriptDriver owns that path.
            // Production AppHost will start DigitalBrain.Scripting (out of process); Testing/Fakes
            // use InProcessAllowListedScriptDriver to prove the seam without loading generated C#.
            if (synapse.Driver == ExecutionDriverKind.Script)
            {
                await _scriptDriver.RunAsync(session, synapse.Workload, grants, cancellationToken)
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }
            else if (synapse.Driver == ExecutionDriverKind.Team || synapse.Workload is TeamWorkload)
            {
                // MAF Workflows can wrap this later; sequential phases keep one shared ExecutionSession.
                var requestJson = $$"""{"workload":"{{synapse.Workload.GetType().Name}}"}""";
                await RunTeamPhasesAsync(session, synapse.Workload, grants, requestJson, cancellationToken)
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }
            else
            {
                var requestJson = $$"""{"workload":"{{synapse.Workload.GetType().Name}}"}""";
                await RunGrantedCapabilitiesAsync(session, grants, requestJson, cancellationToken)
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }

            Stage(LoadRecorded()! with { Status = ExecutionStatus.Completed });
            await EmitAsync(new ExecutionLifecycle(synapse.ExecutionId, ExecutionStatus.Completed))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not NeuronAuthorizationException)
        {
            Stage(LoadRecorded()! with { Status = ExecutionStatus.Failed });
            await EmitAsync(new ExecutionLifecycle(synapse.ExecutionId, ExecutionStatus.Failed, ex.Message))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            throw new NeuronAuthorizationException($"Execution '{Id}' failed: {ex.Message}", ex);
        }
    }

    public async Task HandleAsync(CancelExecution synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);
        RequireMatchingExecution(synapse.ExecutionId);

        var current = LoadRecorded();
        if (current is null)
        {
            throw new NeuronAuthorizationException($"Execution '{Id}' has not been started.");
        }

        if (current.Status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled)
        {
            throw new NeuronAuthorizationException(
                $"Execution '{Id}' cannot be cancelled from status '{current.Status}'.");
        }

        Stage(current with { Status = ExecutionStatus.Cancelled });
        await EmitAsync(new ExecutionLifecycle(synapse.ExecutionId, ExecutionStatus.Cancelled))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private ExecutionState? LoadRecorded()
        => _state.Value is { Length: > 0 } serialized
            ? _states.Deserialize(serialized)
            : null;

    private void Stage(ExecutionState data) => _state.Value = _states.SerializeToArray(data);

    private async Task RunTeamPhasesAsync(
        ExecutionSession session,
        WorkloadDescriptor workload,
        CapabilityId[] grants,
        string requestJson,
        CancellationToken cancellationToken)
    {
        var participantNames = workload is TeamWorkload team && team.ParticipantNames is { Count: > 0 }
            ? team.ParticipantNames
            : ["researcher", "closer"];
        var researcherName = ResolveParticipantName(participantNames, "researcher");
        var closerName = ResolveParticipantName(participantNames, "closer");

        var researcherCapabilities = await RunMatchingGrantsAsync(
                session,
                grants,
                IsResearcherGrant,
                requestJson,
                cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        var closerCapabilities = await RunMatchingGrantsAsync(
                session,
                grants,
                IsCloserGrant,
                requestJson,
                cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var traceJson = JsonSerializer.Serialize(new
        {
            participants = new[]
            {
                new { name = researcherName, capabilities = researcherCapabilities },
                new { name = closerName, capabilities = closerCapabilities },
            },
        });
        await session.ApplyDeltaAsync(new ContextDelta(
                new ContextPath("team.trace"),
                SchemaHash: "team.trace.v1",
                PayloadJson: traceJson,
                BlobRef: null))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task RunGrantedCapabilitiesAsync(
        ExecutionSession session,
        CapabilityId[] grants,
        string requestJson,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < grants.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var grant = grants[i];
            if (!_broker.IsRegistered(grant))
            {
                continue;
            }

            var delta = await session.CallAsync(grant, requestJson, cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await session.ApplyDeltaAsync(delta)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    private async Task<List<string>> RunMatchingGrantsAsync(
        ExecutionSession session,
        CapabilityId[] grants,
        Func<CapabilityId, bool> matches,
        string requestJson,
        CancellationToken cancellationToken)
    {
        var ranCapabilities = new List<string>();
        for (var i = 0; i < grants.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var grant = grants[i];
            if (!matches(grant) || !_broker.IsRegistered(grant))
            {
                continue;
            }

            var delta = await session.CallAsync(grant, requestJson, cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await session.ApplyDeltaAsync(delta)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            ranCapabilities.Add(grant.Value);
        }

        return ranCapabilities;
    }

    private static string ResolveParticipantName(IReadOnlyList<string> names, string role)
    {
        for (var i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], role, StringComparison.OrdinalIgnoreCase))
            {
                return names[i];
            }
        }

        return role;
    }

    private static bool IsResearcherGrant(CapabilityId grant)
        => grant.Value.StartsWith("gmail.", StringComparison.Ordinal)
            || grant.Value.StartsWith("websearch.", StringComparison.Ordinal);

    private static bool IsCloserGrant(CapabilityId grant)
        => grant.Value.StartsWith("salesforce.", StringComparison.Ordinal);

    private async Task AdmitRelatedContextAsync(
        IExecutionContext context,
        IReadOnlyList<ExecutionId>? relatedExecutions,
        CancellationToken cancellationToken)
    {
        if (relatedExecutions is not { Count: > 0 })
        {
            return;
        }

        for (var i = 0; i < relatedExecutions.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relatedId = relatedExecutions[i];
            var related = GrainFactory.GetGrain<IExecutionContext>(
                EntityId.For<IExecutionContext>(Id.Owner, relatedId.ToString()).ToGrainId());
            var state = await related.Read()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            if (state?.Slots is not { Count: > 0 } slots)
            {
                continue;
            }

            for (var slotIndex = 0; slotIndex < slots.Count; slotIndex++)
            {
                var slot = slots[slotIndex];
                await context.ApplyDelta(new ContextDelta(
                        slot.Path,
                        slot.Entry.SchemaHash,
                        slot.Entry.PayloadJson,
                        slot.Entry.BlobRef))
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }
        }
    }

    private void RequireMatchingExecution(ExecutionId executionId)
    {
        if (!string.Equals(executionId.ToString(), Id.Name, StringComparison.Ordinal))
        {
            throw new NeuronAuthorizationException(
                $"Execution neuron '{Id}' refuses command for execution '{executionId}'.");
        }
    }

    private static void RequireCommand(CommandId commandId)
    {
        if (commandId.Value == Guid.Empty)
        {
            throw new NeuronAuthorizationException("An execution command requires a command id.");
        }
    }
}
