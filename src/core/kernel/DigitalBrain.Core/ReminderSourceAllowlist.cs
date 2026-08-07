using System.Collections.Frozen;

namespace DigitalBrain.Core;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Orleans or DI activated; never constructed in-process.")]
internal sealed class ReminderSourceAllowlist(IEnumerable<string> sourceTypes)
{
    private readonly FrozenSet<string> _sourceTypes =
        sourceTypes.ToFrozenSet(StringComparer.Ordinal);

    internal bool Contains(GrainId source)
        => source.Type.ToString() is { } sourceType
            && _sourceTypes.Contains(sourceType);
}
