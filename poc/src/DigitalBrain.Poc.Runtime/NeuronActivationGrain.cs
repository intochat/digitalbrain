using System.Reflection;
using System.Runtime.ExceptionServices;
using DigitalBrain.Poc.Abstractions;

namespace DigitalBrain.Poc.Runtime;

public sealed class NeuronActivationGrain
{
    private readonly DurableTurn _turns;
    private readonly LoadedCandidate _candidate;
    private readonly CandidatePayloadCodec _payloadCodec;

    internal NeuronActivationGrain(
        DurableTurn turns,
        LoadedCandidate candidate,
        CandidatePayloadCodec payloadCodec)
    {
        _turns = turns;
        _candidate = candidate;
        _payloadCodec = payloadCodec;
    }

    public async Task<IReadOnlyList<SynapseEnvelope>> InvokeAsync(
        SynapseEnvelope envelope,
        bool journalInput,
        CancellationToken cancellationToken = default)
    {
        var result = await InvokeWithCommitAsync(envelope, journalInput, cancellationToken);
        return result.Outputs;
    }

    internal async Task<CandidateInvocationResult> InvokeWithCommitAsync(
        SynapseEnvelope envelope,
        bool journalInput,
        CancellationToken cancellationToken = default)
    {
        var handlers = _candidate.Catalog.Resolve(envelope.ContractAlias);
        var handler = string.IsNullOrEmpty(envelope.TargetNeuronType)
            ? handlers.Single()
            : handlers.Single(candidate =>
                (candidate.NeuronType.FullName ?? candidate.NeuronType.Name) ==
                envelope.TargetNeuronType);
        var constructor = SelectConstructor(handler.NeuronType);
        var stateParameter = constructor.GetParameters()
            .SingleOrDefault(parameter =>
                parameter.ParameterType.IsGenericType &&
                parameter.ParameterType.GetGenericTypeDefinition() == typeof(IDurableState<>));
        var method = typeof(NeuronActivationGrain)
            .GetMethod(nameof(InvokeTypedAsync), BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new MissingMethodException(nameof(InvokeTypedAsync));
        var stateType = stateParameter?.ParameterType.GetGenericArguments()[0] ?? typeof(StatelessMarker);

        try
        {
            var task = (Task<CandidateInvocationResult>)method
                .MakeGenericMethod(stateType)
                .Invoke(this, [envelope, handler, constructor, stateParameter is not null, journalInput, cancellationToken])!;
            return await task;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private async Task<CandidateInvocationResult> InvokeTypedAsync<TState>(
        SynapseEnvelope envelope,
        ExactHandler handler,
        ConstructorInfo constructor,
        bool hasState,
        bool journalInput,
        CancellationToken cancellationToken)
    {
        var captured = new List<SynapseEnvelope>();
        var scope = new CandidateInvocationScope(
            envelope.OwnerId,
            envelope.CandidateFamily ?? throw new InvalidOperationException("Candidate family is missing."),
            envelope.TargetRevision ?? throw new InvalidOperationException("Candidate revision is missing."),
            envelope.TargetModuleIdentity ?? throw new InvalidOperationException(
                "Candidate immutable module identity is missing."),
            envelope.DeliveryId);
        var outputTypes = _candidate.GrantedCandidateOutputTypes
            .Concat(_candidate.GrantedTrustedOutputTypes)
            .ToArray();
        var initialState = InitialState<TState>();
        var stateKey = $"state|{scope.OwnerId}|{scope.Family.Value}|{handler.NeuronType.FullName}";
        var handledAliasKey = $"handled|{handler.ContractAlias}";
        var handledFamilyKey = $"family|{scope.Family.Value}|{scope.Revision}|{handler.ContractAlias}";

        var committed = await _turns.ExecuteAsync(
            envelope.DeliveryId,
            envelope.Synapse.GetType().Name,
            stateKey,
            initialState,
            envelope.OwnerId,
            scope.Family,
            envelope.ProducingRevision,
            envelope.ProducingModuleIdentity,
            envelope.TargetRevision,
            envelope.TargetModuleIdentity,
            handledAliasKey,
            handledFamilyKey,
            journalInput,
            ordinal => captured[ordinal],
            _payloadCodec.Serialize,
            async (durableState, stagedBrain) =>
            {
                var candidateBrain = new BrainFacade(async local =>
                {
                    captured.Add(local);
                    await stagedBrain.FireSynapse(local.Synapse, cancellationToken);
                }).ForCandidate(scope, outputTypes, _candidate.GrantedTargetScopes);
                var instance = hasState
                    ? constructor.Invoke([candidateBrain, durableState])
                    : constructor.Invoke([candidateBrain]);
                var method = handler.HandlerInterface.GetMethod(nameof(IHandle<Synapse>.HandleAsync)) ??
                    throw new MissingMethodException(handler.HandlerInterface.FullName, "HandleAsync");
                try
                {
                    await (Task)method.Invoke(instance, [envelope.Synapse, cancellationToken])!;
                }
                catch (TargetInvocationException exception) when (exception.InnerException is not null)
                {
                    ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                }
            },
            cancellationToken);

        if (!committed)
        {
            captured.Clear();
        }

        return new CandidateInvocationResult(committed, captured);
    }

    private static ConstructorInfo SelectConstructor(Type neuronType)
    {
        var constructors = neuronType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        var constructor = constructors.SingleOrDefault(candidate =>
        {
            var parameters = candidate.GetParameters();
            return parameters.Length is 1 or 2 &&
                parameters[0].ParameterType == typeof(IDigitalBrain) &&
                (parameters.Length == 1 ||
                    parameters[1].ParameterType.IsGenericType &&
                    parameters[1].ParameterType.GetGenericTypeDefinition() == typeof(IDurableState<>));
        });
        return constructor ?? throw new InvalidOperationException(
            $"Neuron '{neuronType.FullName}' does not have an exact scoped ABI constructor.");
    }

    private static TState InitialState<TState>()
    {
        if (typeof(TState) == typeof(string))
        {
            return (TState)(object)string.Empty;
        }

        var parameterless = typeof(TState).GetConstructor(Type.EmptyTypes);
        if (parameterless is not null)
        {
            return (TState)parameterless.Invoke(null);
        }

        var constructor = typeof(TState).GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SingleOrDefault(candidate => candidate.GetParameters().All(parameter =>
                parameter.ParameterType.IsValueType));
        if (constructor is null)
        {
            throw new InvalidOperationException(
                $"Durable state '{typeof(TState).FullName}' has no deterministic zero-value constructor.");
        }

        return (TState)constructor.Invoke(constructor.GetParameters()
            .Select(parameter => Activator.CreateInstance(parameter.ParameterType))
            .ToArray());
    }

    private sealed record StatelessMarker;
}
