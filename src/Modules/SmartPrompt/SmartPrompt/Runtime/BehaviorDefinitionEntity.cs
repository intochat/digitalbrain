using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.SmartPrompt;

[GrainType("behaviordefinition")]
internal sealed class BehaviorDefinitionEntity(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<BehaviorDefinitionState> state,
    IBehaviorCompiler compiler) : Entity<BehaviorDefinitionState>(state), IBehaviorDefinition
{
    public async Task<BehaviorCompilation> Save(string source)
    {
        var compilation = compiler.Compile(source);
        if (State is { Active: true } previous)
        {
            await ChangeSubscriptions(previous, subscribe: false);
        }

        await SaveAsync(new BehaviorDefinitionState(source, compilation, Active: false, LastTest: null));
        return compilation;
    }

    public async Task<BehaviorTestReport> Test()
    {
        var current = State ?? throw new InvalidOperationException("Save the feature before running its tests.");
        var report = BehaviorTestInterpreter.Validate(
            current.Compilation.Plan,
            current.Compilation.Diagnostics);
        await SaveAsync(current with { LastTest = report });
        return report;
    }

    public async Task Activate()
    {
        var current = State ?? throw new InvalidOperationException("Save the feature before activating it.");
        var tested = current.LastTest ?? await Test();
        current = State!;
        if (!tested.AllGreen || current.Compilation.Plan is null)
        {
            throw new InvalidOperationException("A behavior can activate only after all paired scenarios are green.");
        }
        if (current.Active)
        {
            return;
        }

        await ChangeSubscriptions(current, subscribe: true);
        await SaveAsync(current with { Active = true });
    }

    public async Task Disable()
    {
        if (State is not { Active: true } current)
        {
            return;
        }
        await ChangeSubscriptions(current, subscribe: false);
        await SaveAsync(current with { Active = false });
    }

    private async Task ChangeSubscriptions(BehaviorDefinitionState definition, bool subscribe)
    {
        if (definition.Compilation.Plan is not { } plan)
        {
            return;
        }

        var (owner, name) = Address();
        foreach (var scenario in plan.Behaviors)
        {
            var subscription = new BehaviorSubscription(owner, name, scenario.Name, plan.SourceHash);
            var directory = GrainFactory.GetGrain<IBehaviorTriggerDirectory>(scenario.TriggerKey);
            if (subscribe)
            {
                await directory.Subscribe(subscription);
            }
            else
            {
                await directory.Unsubscribe(subscription);
            }
        }
    }

    private (string Owner, string Name) Address()
    {
        var key = this.GetPrimaryKeyString();
        var separator = key.IndexOf('/', StringComparison.Ordinal);
        if (separator <= 0 || separator == key.Length - 1)
        {
            throw new InvalidOperationException($"Behavior definition key '{key}' is not owner-scoped.");
        }
        _ = new OwnerId(key[..separator]);
        return (key[..separator], key[(separator + 1)..]);
    }
}
