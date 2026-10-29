using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Confirmation;
using DigitalBrain.Operations;
using DigitalBrain.Runtime.Orleans.Capabilities;

namespace DigitalBrain.Runtime.Orleans.Operations;

/// <summary>The copy failed verification. The source is unchanged and the partial copy has been removed. Code <c>verify-failed</c>.</summary>
internal sealed class VerifyFailed(ItemId item) : StorageException("verify-failed", item, null);

/// <summary>
/// The storage operation gate (07 D-03, CAP-06): asks <see cref="IConfirmations"/> before a destructive write, re-checks the
/// item, then runs the write through the storage broker. Delete, a move between connections, and overwrite ask under
/// <see cref="ConfirmationPolicy.DestructiveOnly"/>. A refused plan does not change storage.
/// </summary>
internal sealed class StorageOperationGate(CapabilityBroker broker, IConfirmations confirmations, BrainId brain)
{
    /// <summary>The question for <paramref name="plan"/>. Texts use display names only.</summary>
    public static ConfirmationPlan Question(WritePlan plan, bool crossConnection, string name, string storage)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(storage);
        var destructive = StorageConfirmation.IsDestructive(plan, crossConnection);
        if (plan.Kind == WriteKind.Delete)
        {
            return plan.Permanent
                ? new ConfirmationPlan(Id(), ConfirmationAction.Delete, true, $"Delete {name}?",
                    $"This removes {name} from {storage}. You cannot undo this.", "Delete", "Keep", name)
                : new ConfirmationPlan(Id(), ConfirmationAction.Delete, true, $"Move {name} to trash?",
                    $"This moves {name} from {storage} to trash.", "Move to trash", "Keep", name);
        }
        if (plan.Conflict == ConflictPolicy.Overwrite)
        {
            return new ConfirmationPlan(Id(), ConfirmationAction.Overwrite, true, $"Replace {name}?",
                $"Replace {name} on {storage}.", "Replace", "Keep existing", name);
        }
        if (plan.Kind == WriteKind.Move && crossConnection)
        {
            return new ConfirmationPlan(Id(), ConfirmationAction.Move, true, $"Move {name} to {storage}?",
                $"Move {name} to {storage}.", "Move", "Cancel", name);
        }
        var action = plan.Kind switch
        {
            WriteKind.Move => ConfirmationAction.Move,
            WriteKind.Copy => ConfirmationAction.Copy,
            WriteKind.Rename => ConfirmationAction.Rename,
            _ => ConfirmationAction.Other,
        };
        return new ConfirmationPlan(Id(), action, destructive, $"{plan.Kind} {name}?", $"{plan.Kind} {name} on {storage}.", "Continue", "Cancel", name);
    }

    /// <summary>Asks, re-checks, and runs <paramref name="plan"/>. A refusal is <see cref="OperationResult.Failed"/> and changes nothing.</summary>
    public async Task<OperationResult> Run(WritePlan plan, CancellationToken ct)
    {
        var refusal = await Ask(plan, ct).ConfigureAwait(false);
        if (refusal is not null) { return new OperationResult.Failed(refusal.Value.Code, refusal.Value.Reason); }
        try { return await Execute(plan, ct).ConfigureAwait(false); }
        catch (StorageException error) when (error is not VerifyFailed) { return new OperationResult.Failed(error.Code, error.Message); }
    }

    /// <summary>Null when <paramref name="plan"/> may run.</summary>
    public async Task<(string Code, string Reason)?> Ask(WritePlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.DryRun) { return null; }
        var source = plan.Source is { } from ? await Owner(from, ct).ConfigureAwait(false) : null;
        var target = plan.TargetFolder is { } folder ? await Owner(folder, ct).ConfigureAwait(false) : null;
        var cross = source is not null && target is not null
            && !string.Equals(source.ConnectionKey, target.ConnectionKey, StringComparison.Ordinal);
        var acted = plan.Kind is WriteKind.CreateFolder or WriteKind.WriteFile ? target ?? source : source ?? target;
        var name = plan.Kind is WriteKind.CreateFolder or WriteKind.WriteFile ? plan.Name! : await DisplayName(acted, plan.Source, ct).ConfigureAwait(false);
        var label = (plan.Kind == WriteKind.Delete ? source : target ?? source)?.Label ?? "storage";
        var question = Question(plan, cross, name, label);
        var stamp = plan.Source is { } item && source is not null ? await Version(source.Capability, item, ct).ConfigureAwait(false) : null;
        if (ConfirmationRules.Asks(confirmations.Policy, question))
        {
            var answer = await confirmations.Confirm(question, ct).ConfigureAwait(false);
            if (!answer.Approved)
            {
                var code = answer.Outcome == ConfirmationOutcome.Expired ? "expired" : "declined";
                var reason = answer.Reason == ConfirmationDenyReason.SelfApproval ? "Only a person can approve this."
                    : answer.Outcome == ConfirmationOutcome.Expired ? "The confirmation expired."
                    : "The confirmation was denied.";
                return (code, reason);
            }
        }
        if (plan.Source is { } again && source is not null)
        {
            var now = await Version(source.Capability, again, ct).ConfigureAwait(false);
            if (!string.Equals(stamp, now, StringComparison.Ordinal))
            {
                return ("plan-invalid", "The item changed.");
            }
        }
        return null;
    }

    private async Task<OperationResult> Execute(WritePlan plan, CancellationToken ct)
    {
        var source = plan.Source is { } from ? await Owner(from, ct).ConfigureAwait(false) : null;
        var target = plan.TargetFolder is { } folder ? await Owner(folder, ct).ConfigureAwait(false) : null;
        var cross = source is not null && target is not null
            && !string.Equals(source.ConnectionKey, target.ConnectionKey, StringComparison.Ordinal);
        if (cross && plan.Kind is WriteKind.Move or WriteKind.Copy)
        {
            return await CopyAcross(source!, target!, plan, ct).ConfigureAwait(false);
        }
        var connection = source ?? target ?? throw new ArgumentException("The plan names neither a source nor a target folder.", nameof(plan));
        await connection.Capability.Write(plan, ct).ConfigureAwait(false);
        return new OperationResult.Succeeded();
    }

    private static async Task<OperationResult> CopyAcross(CapabilityConnection<IStorage> source, CapabilityConnection<IStorage> target, WritePlan plan, CancellationToken ct)
    {
        var original = await source.Capability.Stat(plan.Source!.Value, ct).ConfigureAwait(false);
        var folder = plan.TargetFolder!.Value;
        ItemId created;
        await using (var read = await source.Capability.OpenRead(original.Id, null, ct).ConfigureAwait(false))
        {
            await using var write = await target.Capability.OpenWrite(WritePlan.WriteFile(folder, original.Name, null, plan.Conflict), ct).ConfigureAwait(false);
            await read.Content.CopyToAsync(write.Content, ct).ConfigureAwait(false);
            created = await write.Commit(ct).ConfigureAwait(false);
        }
        var copied = await target.Capability.Stat(created, ct).ConfigureAwait(false);
        if (!Same(original, copied))
        {
            await target.Capability.Write(WritePlan.Delete(created, permanent: true), ct).ConfigureAwait(false);
            throw new VerifyFailed(created);
        }
        if (plan.Kind == WriteKind.Move)
        {
            await source.Capability.Write(WritePlan.Delete(original.Id), ct).ConfigureAwait(false);
        }
        return new OperationResult.Succeeded();
    }

    private static bool Same(Item original, Item copied)
    {
        if (original.Size is { } expected && copied.Size is { } actual && expected != actual) { return false; }
        if (original.Hash is { } left && copied.Hash is { } right && !string.Equals(left, right, StringComparison.OrdinalIgnoreCase)) { return false; }
        return true;
    }

    private async Task<CapabilityConnection<IStorage>?> Owner(ItemId item, CancellationToken ct)
    {
        var candidates = await broker.ResolveFor(brain, item, ct).ConfigureAwait(false);
        CapabilityConnection<IStorage>? owner = null;
        foreach (var candidate in candidates)
        {
            try
            {
                _ = await candidate.Capability.Stat(item, ct).ConfigureAwait(false);
                if (owner is not null) { throw new Conflict(item); }
                owner = candidate;
            }
            catch (StorageException error) when (error is NotFound or Denied or Unsupported)
            {
            }
        }
        return owner ?? throw new NotFound(item);
    }

    private static async Task<string> DisplayName(CapabilityConnection<IStorage>? connection, ItemId? item, CancellationToken ct)
    {
        if (connection is null || item is not { } id) { return "item"; }
        try { return (await connection.Capability.Stat(id, ct).ConfigureAwait(false)).Name; }
        catch (StorageException) { return "item"; }
    }

    private static async Task<string?> Version(IStorage storage, ItemId item, CancellationToken ct)
    {
        var stat = await storage.Stat(item, ct).ConfigureAwait(false);
        return stat.Hash ?? stat.Modified?.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Id() => "cf-" + Guid.NewGuid().ToString("N");
}
