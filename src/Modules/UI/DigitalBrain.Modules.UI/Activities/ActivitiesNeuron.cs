using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.UI;

[GrainType(UIVocabulary.ActivitiesType)]
internal sealed class ActivitiesNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ActivitiesState> state)
    : Neuron<ActivitiesState>(runtime, state), IActivities
{
    [ReadOnly]
    public Task<ActivitiesSnapshot> Read(ReadActivities query)
        => Task.FromResult(new ActivitiesSnapshot(TimeProvider.GetUtcNow(),
            [.. (State?.Activities.Values.AsEnumerable() ?? []).Select(activity => activity.View())
                .OrderByDescending(view => view.UpdatedAt).Take(Math.Clamp(query.Limit, 1, 500))]));

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.Signal.Type != UIVocabulary.ActivityExecutionChanged)
        {
            return;
        }

        var fact = UIBodies.Read(delivery, UIJson.Default.ActivityExecutionChanged);
        // An unknown phase is vocabulary this module does not speak: journalled and ignored,
        // because throwing would retry the same fact forever.
        if (fact.Phase is not ("running" or "waiting" or "completed" or "failed" or "cancelled" or "observed"))
        {
            return;
        }

        var current = State ?? new ActivitiesState(new Dictionary<string, ActivityState>(StringComparer.Ordinal));
        var key = fact.CorrelationId.ToString();
        var activity = current.Activities.TryGetValue(key, out var existing) ? existing : new ActivityState();
        if (activity.Apply(fact))
        {
            current.Activities[key] = activity;
            await SaveAsync(current, cancellationToken).ConfigureAwait(true);
        }
        // An earlier delivery can have committed state before an observer failed.
        // Retrying republishes the same version without applying the fact twice.
        await FireAsync(UIBodies.Signal(UIVocabulary.ActivityChanged, new ActivityChanged(activity.View()), UIJson.Default.ActivityChanged),
            cancellationToken: cancellationToken).ConfigureAwait(true);
    }
}
