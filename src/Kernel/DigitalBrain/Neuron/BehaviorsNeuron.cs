using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Core;

[GrainType("behaviors")]
internal sealed class BehaviorsNeuron : Neuron, IBehaviors, IBehaviorsKernel
{
    private readonly IDurableDictionary<string, BehaviorDefinition> _definitions;

    public BehaviorsNeuron(NeuronRuntime runtime) : base(runtime)
        => _definitions = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<string, BehaviorDefinition>>(
            "behaviors.current");

    public async Task HandleAsync(AdmitBehavior signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        var definition = new BehaviorDefinition(
            signal.Name,
            signal.Source,
            Guid.NewGuid(),
            BehaviorStatus.Admitted,
            "Waiting for the scripting host.",
            [],
            VerifiedActor.Current?.PrincipalId);
        _definitions[definition.Name] = definition;
        await RecordOutgoingAsync(new BehaviorAdmitted(definition.Name, definition.Source, definition.Revision))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(RemoveBehavior signal, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signal.Name);
        cancellationToken.ThrowIfCancellationRequested();
        if (_definitions.Remove(signal.Name.Trim()))
        {
            await RecordOutgoingAsync(new BehaviorRemoved(signal.Name.Trim()))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    public async Task HandleAsync(ReadBehaviors signal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ReplyAsync(new BehaviorsRead(Current()))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public Task<IReadOnlyList<BehaviorDefinition>> ReadCurrent() => Task.FromResult(Current());

    public async Task HandleAsync(ReportBehaviorStatus signal, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signal.Name);
        ArgumentNullException.ThrowIfNull(signal.Diagnostics);
        cancellationToken.ThrowIfCancellationRequested();
        if (signal.Status is not (BehaviorStatus.Running or BehaviorStatus.Completed or BehaviorStatus.Failed))
        {
            throw new ArgumentOutOfRangeException(nameof(signal.Status));
        }

        // Completion of a replaced or removed revision cannot overwrite the owner's program.
        if (!_definitions.TryGetValue(signal.Name, out var current) || current.Revision != signal.Revision)
        {
            return;
        }

        _definitions[signal.Name] = current with
        {
            Status = signal.Status,
            Summary = signal.Summary,
            Diagnostics = signal.Diagnostics.ToArray(),
        };
        await RecordOutgoingAsync(signal)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private IReadOnlyList<BehaviorDefinition> Current()
        => [.. _definitions.Select(entry => entry.Value).OrderBy(definition => definition.Name, StringComparer.Ordinal)];
}
