namespace DigitalBrain.Units;

/// <summary>The state of the installs neuron: one record per installed alias and the asks waiting for the person.</summary>
/// <param name="Records">The installed apps.</param>
/// <param name="Pending">The installs asked and not yet resolved; at most one per alias.</param>
public sealed record InstallsState(IReadOnlyList<InstallRecord> Records, IReadOnlyList<PendingInstall> Pending)
{
    /// <summary>No installs.</summary>
    public InstallsState() : this([], []) { }

    internal InstallRecord? FindRecord(string alias)
        => Records.FirstOrDefault(r => r.Alias == alias);

    internal PendingInstall? FindPendingByPlan(string planId)
        => Pending.FirstOrDefault(p => p.PlanId == planId);

    internal static bool HasSameBindings(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
        => left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);

    internal InstallsState Rebind(InstallRecord existing, IReadOnlyDictionary<string, string> bindings)
    {
        var rebound = existing with { Bindings = new Dictionary<string, string>(bindings, StringComparer.Ordinal) };
        return this with { Records = [.. Records.Where(r => r.Alias != existing.Alias), rebound] };
    }

    internal InstallsState StagePending(PendingInstall pending)
        => this with { Pending = [.. Pending.Where(p => p.Alias != pending.Alias), pending] };

    internal InstallsState RemovePendingPlan(string planId)
        => this with { Pending = [.. Pending.Where(p => p.PlanId != planId)] };

    internal InstallsState CompleteInstall(PendingInstall pending, SynapseNeeds needs, DateTimeOffset installedAt)
    {
        var record = new InstallRecord(
            pending.Alias,
            pending.NeedsHash,
            pending.Bindings,
            [.. needs.Schedules.Select(s => s.Text)],
            installedAt);
        return new InstallsState(
            [.. Records.Where(r => r.Alias != pending.Alias), record],
            [.. Pending.Where(p => p.PlanId != pending.PlanId)]);
    }

    internal (InstallsState Next, Signal[] Signals) CancelPendingForAlias(string alias, string reason)
    {
        var cancelled = Pending.Where(p => p.Alias == alias).ToList();
        if (cancelled.Count == 0)
        {
            return (this, []);
        }

        var next = this with { Pending = [.. Pending.Where(p => p.Alias != alias)] };
        var signals = cancelled.Select(p => (Signal)new InstallDenied(alias, p.PlanId, reason)).ToArray();
        return (next, signals);
    }

    internal InstallsState RemoveRecord(string alias)
        => this with { Records = [.. Records.Where(r => r.Alias != alias)] };
}
