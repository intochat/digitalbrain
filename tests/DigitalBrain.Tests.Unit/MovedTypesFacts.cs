using System.Reflection;
using System.Text.Json;
using DigitalBrain.Contracts;

namespace DigitalBrain.Tests.Unit;

// The OS inventory of types that move into this core (MovedTypes.json, copied as content): which of them reach storage or the
// signal journal, and the [NeuronAlias]/[Alias] each carries. A type that moved keeps its alias; a persisted one stays in the OS.
public sealed class MovedTypesFacts
{
    // The inventory entries this assembly now holds, by the OS full name they had, and the full name they have here.
    private static readonly Dictionary<string, string> Moved = new(StringComparer.Ordinal)
    {
        ["IntoChat.OS.IInitialSignalWait"] = "DigitalBrain.Signals.Watch.IInitialSignalWait",
        ["IntoChat.OS.ISelectiveWatch"] = "DigitalBrain.Signals.Watch.ISelectiveWatch",
        ["IntoChat.OS.ISignalDelivery"] = "DigitalBrain.Signals.Watch.ISignalDelivery",
        ["IntoChat.OS.ISignalWatch"] = "DigitalBrain.Signals.Watch.ISignalWatch",
        ["IntoChat.OS.Reaction"] = "DigitalBrain.Signals.Watch.Reaction",
        ["IntoChat.OS.SignalDeliveries"] = "DigitalBrain.Signals.Watch.SignalDeliveries",
        ["IntoChat.OS.SourceSet"] = "DigitalBrain.Signals.Watch.SourceSet",
        ["IntoChat.OS.Sources"] = "DigitalBrain.Signals.Watch.Sources",
        ["IntoChat.OS.WatchStream`1"] = "DigitalBrain.Signals.Watch.WatchStream`1",
        ["IntoChat.OS.Runtime.NeuronAddress"] = "DigitalBrain.NeuronAddress",
        ["IntoChat.OS.Runtime.INeuronResolver"] = "DigitalBrain.INeuronResolver",
        ["IntoChat.OS.Framework.IWorkContinuation"] = "DigitalBrain.Invocation.IWorkContinuation",
    };

    private sealed record Inventory(string Method, Entry[] Types);

    private sealed record Entry(string Old, string? Alias, bool Persisted);

    private static readonly JsonSerializerOptions Format = new() { PropertyNameCaseInsensitive = true };

    private static Entry[] Load()
        => JsonSerializer.Deserialize<Inventory>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MovedTypes.json")),
            Format)!.Types;

    private static Type[] Core => typeof(Signal).Assembly.GetTypes();

    [Fact]
    public void MovedTypesKeepTheirAliases()
    {
        foreach (var entry in Load().Where(e => e.Alias is not null && Moved.ContainsKey(e.Old)))
        {
            Assert.Contains(Core, t => t.GetCustomAttribute<NeuronAliasAttribute>(inherit: false)?.Alias == entry.Alias);
        }
    }

    [Fact]
    public void EveryMovedTypeResolvesUnderItsNewNamespace()
    {
        var inventory = Load();
        foreach (var (old, now) in Moved)
        {
            Assert.Contains(inventory, e => e.Old == old);
            var type = typeof(Signal).Assembly.GetType(now);
            Assert.NotNull(type);
            Assert.Equal(now, type.FullName);
        }
    }

    [Fact]
    public void PersistedTypesStayOutOfTheCore()
    {
        foreach (var entry in Load().Where(e => e.Persisted))
        {
            var simple = entry.Old[(entry.Old.LastIndexOf('.') + 1)..];
            Assert.DoesNotContain(Core, t => t.Name == simple);
        }
    }
}
