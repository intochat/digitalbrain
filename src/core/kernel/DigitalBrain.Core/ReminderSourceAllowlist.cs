using System.Collections.Frozen;

namespace DigitalBrain.Core;

internal sealed class ReminderSourceAllowlist(IEnumerable<string> sourceTypes)
{
    private readonly FrozenSet<string> _sourceTypes =
        sourceTypes.ToFrozenSet(StringComparer.Ordinal);

    internal bool Contains(GrainId source)
        => source.Type.ToString() is { } sourceType
            && _sourceTypes.Contains(sourceType);
}
