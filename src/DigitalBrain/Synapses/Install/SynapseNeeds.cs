using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Capabilities;
using DigitalBrain.Time;

namespace DigitalBrain;

/// <summary>One capability slot a synapse app needs connected.</summary>
/// <param name="Name">The slot (parameter) name.</param>
/// <param name="Capability">The capability the slot needs.</param>
/// <param name="ContractDisplayName">What the capability lets the app do, in plain words.</param>
public sealed record SlotNeed(string Name, CapabilityId Capability, string ContractDisplayName);

/// <summary>Everything a synapse app asks for at install time.</summary>
/// <param name="Alias">The state alias.</param>
/// <param name="Slots">The capability slots.</param>
/// <param name="Schedules">The schedules it runs on.</param>
/// <param name="Gates">The signal aliases it can stop.</param>
/// <param name="Reactions">The reactions it runs: a typed reaction by its function name, an alias reaction as <c>alias:{signal alias}</c>.</param>
/// <param name="HasView">True when it shows a view.</param>
public sealed record SynapseNeeds(
    string Alias,
    IReadOnlyList<SlotNeed> Slots,
    IReadOnlyList<Schedule> Schedules,
    IReadOnlyList<string> Gates,
    IReadOnlyList<string> Reactions,
    bool HasView)
{
    /// <summary>The one sentence a person approves, for example <c>stats wants to: read-only SQL through Db; show a view.</c></summary>
    public string Sentence()
    {
        var parts = new List<string>();
        parts.AddRange(Slots.Select(s => $"{s.ContractDisplayName} through {s.Name}"));
        parts.AddRange(Schedules.Select(Describe));
        parts.AddRange(Gates.Select(g => $"stop {g} before it happens"));
        var typed = Reactions.Count(r => !r.StartsWith(AliasPrefix, StringComparison.Ordinal));
        if (typed > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"react to {typed} signal{(typed == 1 ? "" : "s")}"));
        }

        parts.AddRange(Reactions.Where(r => r.StartsWith(AliasPrefix, StringComparison.Ordinal)).Select(r => $"react to {r[AliasPrefix.Length..]}"));

        if (HasView)
        {
            parts.Add("show a view");
        }

        return $"{Alias} wants to: {string.Join("; ", parts)}.";
    }

    /// <summary>SHA-256 of the needs, lowercase hex; the order of items within each list does not matter.</summary>
    public string Hash()
    {
        var lines = new List<string> { $"alias:{Alias}" };
        lines.AddRange(Sorted(Slots.Select(s => $"slot:{s.Name}|{s.Capability}|{s.ContractDisplayName}")));
        lines.AddRange(Sorted(Schedules.Select(s => $"schedule:{s.Text}")));
        lines.AddRange(Sorted(Gates.Select(g => $"gate:{g}")));
        lines.AddRange(Sorted(Reactions.Select(r => $"reaction:{r}")));
        lines.Add(HasView ? "view:true" : "view:false");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))));
    }

    /// <summary>The prefix of an alias reaction's entry in <see cref="Reactions"/>.</summary>
    public const string AliasPrefix = "alias:";

    private static string Describe(Schedule s) => s.Kind switch
    {
        ScheduleKind.Daily => $"run daily at {Clock(s)} UTC",
        ScheduleKind.Weekly => $"run weekly on {DayNames[(int)s.Day!.Value]} at {Clock(s)} UTC",
        _ => $"run {s.Text}",
    };

    private static readonly string[] DayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    private static string Clock(Schedule s) => s.At!.Value.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static IEnumerable<string> Sorted(IEnumerable<string> items) => items.OrderBy(i => i, StringComparer.Ordinal);
}
