using System.Text.Json;
using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Execution;

public sealed class PreferenceContextProvider(IGrainFactory grains) : IExecutionContextProvider
{
    public async Task ContributeAsync(ExecutionSeedBuilder seed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seed);
        cancellationToken.ThrowIfCancellationRequested();

        var store = grains.GetGrain<IPreferenceStore>(
            EntityId.For<IPreferenceStore>(seed.Owner, IPreferenceStore.DefaultInstanceName).ToGrainId());
        var rules = await store.ListRules()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        if (rules.Count == 0)
        {
            return;
        }

        var lines = new List<string>(rules.Count);
        for (var i = 0; i < rules.Count; i++)
        {
            lines.Add($"- [{rules[i].Category}] {rules[i].RuleText}");
        }

        seed.PromptBlocks.Add("Owner preferences:\n" + string.Join('\n', lines));
        seed.SeedDeltas.Add(new ContextDelta(
            new ContextPath("preferences.rules"),
            SchemaHash: "preferences.rules.v1",
            PayloadJson: JsonSerializer.Serialize(rules),
            BlobRef: null));
    }
}
