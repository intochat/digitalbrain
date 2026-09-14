using System.Globalization;
using DigitalBrain.Abstractions.Slots;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Coding;

// Everything the slot neuron needs to address the two kernels and the gateway. Two slots is the shape
// phase 2 proves (D2); a third would only need three configuration entries and a longer Names list.
public sealed record SlotOptions(
    string? Slot,
    string? ArtifactsRoot,
    string GatewayUrl,
    TimeSpan Grace,
    TimeSpan LeaseSettle,
    TimeSpan PromoteWait,
    string SmokePath,
    int HealthAttempts,
    IReadOnlyDictionary<string, string> Urls,
    IReadOnlyDictionary<string, string> Resources)
{
    public const string Section = "DigitalBrain:Slots";

    private const string ArtifactsDirectoryName = "artifacts";

    public static IReadOnlyList<string> Names { get; } = ["a", "b"];

    public static SlotOptions From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var slots = configuration.GetSection(Section);
        return new SlotOptions(
            configuration[ActiveSlotNames.SlotKey],
            slots["ArtifactsRoot"],
            Text(slots["Gateway"], "http://localhost:5080"),
            Duration(slots["Grace"], TimeSpan.FromSeconds(10)),
            // Five refresher intervals: the standby learns of a lease flip through its own poll.
            Duration(slots["LeaseSettle"], ActiveSlotNames.RefreshInterval * 5),
            Duration(slots["PromoteWait"], TimeSpan.FromMinutes(20)),
            Text(slots["SmokePath"], "/chats/slot-smoke/brain"),
            Count(slots["HealthAttempts"], 120),
            Names.ToDictionary(name => name, name => Text(slots[$"{name}:Url"], DefaultUrl(name)), StringComparer.OrdinalIgnoreCase),
            Names.ToDictionary(name => name, name => Text(slots[$"{name}:Resource"], "kernel-" + name), StringComparer.OrdinalIgnoreCase));
    }

    public string UrlFor(string slot)
        => Urls.TryGetValue(slot, out var url)
            ? url
            : throw new InvalidOperationException($"Slot '{slot}' has no address. Set '{Section}:{slot}:Url'.");

    public string ResourceFor(string slot)
        => Resources.TryGetValue(slot, out var resource)
            ? resource
            : throw new InvalidOperationException($"Slot '{slot}' has no Aspire resource. Set '{Section}:{slot}:Resource'.");

    // Always absolute: MSBuild resolves a relative ArtifactsPath against each project's own directory,
    // which scatters outputs under every project folder and poisons the next build (phase 1 live run).
    public string ArtifactsFor(string slot, string solutionDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionDirectory);
        var root = string.IsNullOrWhiteSpace(ArtifactsRoot)
            ? Path.Combine(solutionDirectory, ArtifactsDirectoryName)
            : Path.GetFullPath(ArtifactsRoot, solutionDirectory);
        return Path.Combine(root, "slot-" + slot);
    }

    private static string DefaultUrl(string slot) => slot switch
    {
        "a" => "http://localhost:5081",
        "b" => "http://localhost:5082",
        _ => throw new InvalidOperationException($"Slot '{slot}' has no default address. Set '{Section}:{slot}:Url'."),
    };

    private static string Text(string? configured, string fallback)
        => configured is { Length: > 0 } value ? value : fallback;

    private static TimeSpan Duration(string? configured, TimeSpan fallback)
        => TimeSpan.TryParse(configured, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static int Count(string? configured, int fallback)
        => int.TryParse(configured, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : fallback;
}
