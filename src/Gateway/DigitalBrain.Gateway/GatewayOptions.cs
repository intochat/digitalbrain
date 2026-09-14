using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Gateway;

// The two kernels the gateway may route to, which of them starts out active and how often the route table
// may be rebuilt. Every value is parsed and refused here, before the server listens, so a typo surfaces as
// a named refusal at startup rather than as traffic sent to an address nobody meant.
internal sealed record GatewayOptions(
    IReadOnlyDictionary<string, Uri> Slots,
    string Active,
    TimeSpan MinSwitchInterval)
{
    private const string Section = "DigitalBrain:Gateway";

    private const string DefaultSlotA = "http://localhost:5081";
    private const string DefaultSlotB = "http://localhost:5082";

    internal static GatewayOptions From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var gateway = configuration.GetSection(Section);
        var slots = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = Address(gateway["Slots:a"], "Slots:a", DefaultSlotA),
            ["b"] = Address(gateway["Slots:b"], "Slots:b", DefaultSlotB),
        };
        var active = Text(gateway["Active"], "a");
        if (!slots.ContainsKey(active))
        {
            throw new InvalidOperationException(
                $"'{Section}:Active' is '{active}', which is not one of the configured slots ({string.Join(", ", slots.Keys)}).");
        }

        return new GatewayOptions(slots, active, Duration(gateway["MinSwitchInterval"], "MinSwitchInterval", TimeSpan.FromSeconds(15)));
    }

    // "localhost:5081" parses as an absolute URI whose scheme is "localhost", so the scheme is checked as
    // well: without this the first proxied request, not the configuration, is where the typo would surface.
    private static Uri Address(string? configured, string key, string fallback)
    {
        var value = Text(configured, fallback);
        return Uri.TryCreate(value, UriKind.Absolute, out var address) && address.Scheme is "http" or "https"
            ? address
            : throw new InvalidOperationException($"'{Section}:{key}' is '{value}', which is not an http or https address like '{fallback}'.");
    }

    private static string Text(string? configured, string fallback)
        => configured is { Length: > 0 } value ? value : fallback;

    // A value that does not parse is a refusal, not the default: a gateway that debounced for fifteen
    // seconds because "15s" is not a TimeSpan would never explain itself.
    private static TimeSpan Duration(string? configured, string key, TimeSpan fallback)
    {
        if (configured is not { Length: > 0 } value)
        {
            return fallback;
        }

        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed) && parsed >= TimeSpan.Zero
            ? parsed
            : throw new InvalidOperationException($"'{Section}:{key}' is '{value}', which is not a duration like '00:00:15'.");
    }
}
