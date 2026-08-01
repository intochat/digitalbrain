using System.Text;

namespace DigitalBrain.Memory;

public static class BehaviorProjection
{
    public static bool IsSearchable(BehaviorProjectionVisibility visibility)
        => visibility == BehaviorProjectionVisibility.Published;

    public static IReadOnlyList<VectorProjectionEntry> FromSources(IEnumerable<BehaviorProjectionSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var entries = new List<VectorProjectionEntry>();
        foreach (var source in sources)
        {
            if (!IsSearchable(source.Visibility))
            {
                continue;
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(source.BehaviorId);
            ArgumentException.ThrowIfNullOrWhiteSpace(source.DisplayName);
            ArgumentException.ThrowIfNullOrWhiteSpace(source.Description);
            ArgumentNullException.ThrowIfNull(source.ScenarioTitles);

            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [VectorProjectionMetadataKeys.Kind] = VectorProjectionKinds.Behavior,
                [VectorProjectionMetadataKeys.BehaviorId] = source.BehaviorId,
                [VectorProjectionMetadataKeys.ContractId] = source.BehaviorId,
                [VectorProjectionMetadataKeys.Visibility] = nameof(BehaviorProjectionVisibility.Published),
            };

            if (!string.IsNullOrWhiteSpace(source.ArtifactHash))
            {
                metadata[VectorProjectionMetadataKeys.ArtifactHash] = source.ArtifactHash;
            }

            var text = new StringBuilder()
                .Append(source.DisplayName)
                .Append(' ')
                .Append(source.Description);
            foreach (var scenario in source.ScenarioTitles)
            {
                if (string.IsNullOrWhiteSpace(scenario))
                {
                    continue;
                }

                text.Append(' ').Append(scenario);
            }

            entries.Add(new VectorProjectionEntry(source.BehaviorId, text.ToString(), metadata));
        }

        return entries
            .GroupBy(static entry => entry.Key, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
            .ToArray();
    }
}
