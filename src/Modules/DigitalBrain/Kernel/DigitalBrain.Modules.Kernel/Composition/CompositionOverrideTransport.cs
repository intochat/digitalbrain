using System.Collections.Concurrent;

namespace DigitalBrain.Core;

// The AppHost under test runs inside the test process, so an override travels as an opaque token
// that resolves to the test's option edits, which are applied to the AppHost's own baseline options.
public static class CompositionOverrideTransport
{
    public const string ConfigurationKey = "DigitalBrain:Testing:Overrides";
    private static readonly ConcurrentDictionary<string, ModuleEdit[]> Published = new(StringComparer.Ordinal);
    internal sealed record ModuleEdit(Type ModuleType, IReadOnlyList<Delegate> OptionEdits, bool Omit = false);

    internal static string Publish(ModuleEdit[] edits)
    {
        var token = Guid.NewGuid().ToString("N");
        Published[token] = edits;
        return token;
    }

    internal static ModuleEdit[] Take(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return Published.TryGetValue(token, out var edits) ? edits
            : throw new ArgumentException("Unknown composition override token; overrides only reach an AppHost running in the test process.", nameof(token));
    }
}
