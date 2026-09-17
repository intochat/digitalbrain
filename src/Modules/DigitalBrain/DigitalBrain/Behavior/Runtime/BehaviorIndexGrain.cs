using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Behavior;
using Orleans.Runtime;

namespace DigitalBrain.Core.Behavior;

[GrainType("behavior-index")]
internal sealed class BehaviorIndexGrain(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<BehaviorIndexState> state)
    : Grain, IBehaviorIndex
{
    public Task<IReadOnlyList<string>> List() => Task.FromResult(state.RecordExists ? state.State.Names : []);

    public async Task Remember(string id)
    {
        var names = state.RecordExists ? state.State.Names : [];
        if (names.Contains(id, StringComparer.Ordinal))
        {
            return;
        }

        state.State = new([.. names, id]);
        await state.WriteStateAsync().ConfigureAwait(true);
    }
}

[GenerateSerializer]
internal sealed record BehaviorIndexState([property: Id(0)] IReadOnlyList<string> Names);
