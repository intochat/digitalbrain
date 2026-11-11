using DigitalBrain.Time;

namespace DigitalBrain.Kernel;

/// <summary>Represents the deterministic diff between registered and desired schedule declarations.</summary>
/// <param name="Unchanged">Schedules that remain in place.</param>
/// <param name="Added">Schedules that must be registered.</param>
/// <param name="Removed">Schedules that must be unregistered.</param>
internal readonly record struct ScheduleDelta(
    IReadOnlySet<string> Unchanged,
    IReadOnlySet<string> Added,
    IReadOnlySet<string> Removed)
{
    /// <summary>Whether any schedule was added or removed.</summary>
    public bool HasChanges => Added.Count > 0 || Removed.Count > 0;
}

/// <summary>
/// Pure deterministic comparison and diffing of neuron schedule declarations.
/// Zero dependencies on Orleans or test frameworks.
/// </summary>
internal static class ScheduleDiff
{
    /// <summary>Computes the delta between held and wanted schedules using ordinal string comparison.</summary>
    public static ScheduleDelta Diff(IEnumerable<string> heldSchedules, IEnumerable<string> wantedSchedules)
    {
        ArgumentNullException.ThrowIfNull(heldSchedules);
        ArgumentNullException.ThrowIfNull(wantedSchedules);

        var held = heldSchedules.ToHashSet(StringComparer.Ordinal);
        var wanted = wantedSchedules.ToHashSet(StringComparer.Ordinal);

        var unchanged = held.Intersect(wanted).ToHashSet(StringComparer.Ordinal);
        var added = wanted.Except(held).ToHashSet(StringComparer.Ordinal);
        var removed = held.Except(wanted).ToHashSet(StringComparer.Ordinal);

        return new ScheduleDelta(unchanged, added, removed);
    }

    /// <summary>Whether the set of held schedules differs from the set of wanted schedules.</summary>
    public static bool NeedsUpdate(IEnumerable<string> heldSchedules, IEnumerable<string> wantedSchedules)
    {
        ArgumentNullException.ThrowIfNull(heldSchedules);
        ArgumentNullException.ThrowIfNull(wantedSchedules);

        var held = heldSchedules.ToHashSet(StringComparer.Ordinal);
        var wanted = wantedSchedules.ToHashSet(StringComparer.Ordinal);
        return !held.SetEquals(wanted);
    }

    /// <summary>Replaces the owner's registrations only when they differ, so an unchanged install leaves its reminders running.</summary>
    public static async Task Apply(ITime time, string owner, IReadOnlyList<Schedule> schedules, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(schedules);

        var held = (await time.Registered(ct).ConfigureAwait(true))
            .Where(r => r.Owner == owner)
            .Select(r => r.Schedule)
            .ToHashSet(StringComparer.Ordinal);

        var wanted = schedules.Select(s => s.Text).ToHashSet(StringComparer.Ordinal);
        if (held.SetEquals(wanted)) { return; }

        if (held.Count > 0)
        {
            await time.Unregister(owner, ct).ConfigureAwait(true);
        }

        foreach (var text in wanted)
        {
            await time.Register(owner, text, ct).ConfigureAwait(true);
        }
    }
}
