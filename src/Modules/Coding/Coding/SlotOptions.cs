using System.Globalization;
using DigitalBrain.Abstractions.Slots;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Coding;

// Everything the slot neuron needs to address the two kernels and the gateway. Two slots is the shape
// phase 2 proves (D2); a third would only need three configuration entries and a longer Names list.
// Every value is parsed and refused here, at configuration time, so a promotion never discovers a typo
// from inside an HTTP probe or waits a duration nobody configured.
public sealed record SlotOptions(
    string? Slot,
    string? ArtifactsRoot,
    Uri GatewayUrl,
    TimeSpan Grace,
    TimeSpan LeaseSettle,
    TimeSpan PromoteWait,
    string SmokePath,
    int HealthAttempts,
    IReadOnlyDictionary<string, Uri> Urls,
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
            Address(slots["Gateway"], "Gateway", "http://localhost:5080"),
            Duration(slots["Grace"], "Grace", TimeSpan.FromSeconds(10)),
            // Five refresher intervals: the standby learns of a lease flip through its own poll.
            Duration(slots["LeaseSettle"], "LeaseSettle", ActiveSlotNames.RefreshInterval * 5),
            Duration(slots["PromoteWait"], "PromoteWait", TimeSpan.FromMinutes(20)),
            Text(slots["SmokePath"], "/chats/slot-smoke/brain"),
            Count(slots["HealthAttempts"], "HealthAttempts", 120),
            Names.ToDictionary(name => name, name => Address(slots[$"{name}:Url"], $"{name}:Url", DefaultUrl(name)), StringComparer.OrdinalIgnoreCase),
            Names.ToDictionary(name => name, name => Text(slots[$"{name}:Resource"], "kernel-" + name), StringComparer.OrdinalIgnoreCase));
    }

    public Uri UrlFor(string slot)
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

    // "localhost:5081" parses as an absolute URI whose scheme is "localhost", so the scheme is checked as
    // well: without this the probe, not the configuration, is where the typo would surface.
    private static Uri Address(string? configured, string key, string fallback)
    {
        var value = Text(configured, fallback);
        return Uri.TryCreate(value, UriKind.Absolute, out var address) && address.Scheme is "http" or "https"
            ? address
            : throw new InvalidOperationException($"'{Section}:{key}' is '{value}', which is not an http or https address like '{fallback}'.");
    }

    private static string Text(string? configured, string fallback)
        => configured is { Length: > 0 } value ? value : fallback;

    // A value that does not parse is a refusal, not the default: a promotion that waited ten seconds
    // because "30s" is not a TimeSpan would never explain itself.
    private static TimeSpan Duration(string? configured, string key, TimeSpan fallback)
    {
        if (configured is not { Length: > 0 } value)
        {
            return fallback;
        }

        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed) && parsed >= TimeSpan.Zero
            ? parsed
            : throw new InvalidOperationException($"'{Section}:{key}' is '{value}', which is not a duration like '00:00:10'.");
    }

    private static int Count(string? configured, string key, int fallback)
    {
        if (configured is not { Length: > 0 } value)
        {
            return fallback;
        }

        return int.TryParse(value, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : throw new InvalidOperationException($"'{Section}:{key}' is '{value}', which is not a count of one or more.");
    }
}
