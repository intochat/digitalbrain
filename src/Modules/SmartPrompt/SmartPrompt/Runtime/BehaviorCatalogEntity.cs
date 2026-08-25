using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.SmartPrompt;

[GrainType("behaviorcatalog")]
internal sealed class BehaviorCatalogEntity(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<BehaviorCatalogState> state)
    : Entity<BehaviorCatalogState>(state), IBehaviorCatalog
{
    public async Task Add(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var names = State?.Names ?? [];
        if (names.Contains(name, StringComparer.Ordinal))
        {
            return;
        }

        await SaveAsync(new BehaviorCatalogState(
            names.Append(name).Order(StringComparer.Ordinal).ToArray()));
    }
}
