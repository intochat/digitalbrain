using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using Orleans.Runtime;

namespace DigitalBrain.SmartPrompt;

[GrainType("behavior-runner")]
internal sealed class BehaviorRunner(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<BehaviorRunnerState> state,
    IBehaviorActionExecutor actions) : Grain, IBehaviorRunner
{
    private const int RetainedEvents = 1024;

    public async Task Deliver(BehaviorSubscription subscription, BehaviorEvent behaviorEvent)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(behaviorEvent);
        var current = state.RecordExists ? state.State : new BehaviorRunnerState([]);
        if (current.CompletedEventIds.Contains(behaviorEvent.EventId, StringComparer.Ordinal))
        {
            return;
        }

        var owner = new OwnerId(subscription.Owner);
        var definition = GrainFactory.GetGrain<IBehaviorDefinition>(
            EntityId.For<IBehaviorDefinition>(owner, subscription.BehaviorName).ToGrainId());
        var stored = await definition.Read();
        if (stored is not { Active: true, Compilation.Plan: { } plan }
            || !string.Equals(plan.SourceHash, subscription.RevisionHash, StringComparison.Ordinal))
        {
            return;
        }
        var scenario = plan.Behaviors.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, subscription.ScenarioName, StringComparison.Ordinal));
        if (scenario is null || !Matches(scenario, behaviorEvent))
        {
            return;
        }

        await actions.Execute(owner, scenario, behaviorEvent);
        current.CompletedEventIds.Add(behaviorEvent.EventId);
        while (current.CompletedEventIds.Count > RetainedEvents)
        {
            current.CompletedEventIds.RemoveAt(0);
        }
        state.State = current;
        await state.WriteStateAsync();
    }

    private static bool Matches(BehaviorScenarioPlan scenario, BehaviorEvent behaviorEvent)
    {
        foreach (var filter in scenario.Steps.Where(static step => step.Role == BehaviorStepRole.Filter))
        {
            if (filter.Binding is nameof(BuiltInBehaviorSteps.PostMentions) or nameof(BuiltInBehaviorSteps.EventTextContains)
                && !behaviorEvent.Text.Contains(filter.Arguments[0], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (filter.Binding == nameof(BuiltInBehaviorSteps.EventValueAbove)
                && (!double.TryParse(filter.Arguments[0], System.Globalization.CultureInfo.InvariantCulture, out var threshold)
                    || behaviorEvent.Value <= threshold))
            {
                return false;
            }
        }
        return true;
    }
}
