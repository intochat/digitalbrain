using DigitalBrain.Confirmation;

namespace DigitalBrain.Capabilities.Storage;

/// <summary>
/// The confirmation question for a storage <see cref="WritePlan"/> (07 D-03, D-22, UI-SPEC "Confirmation"). Destructive: delete,
/// move between two connections, and any plan with <see cref="ConflictPolicy.Overwrite"/>. Not destructive: create folder, write
/// without overwrite, rename, copy, move within one connection; those are only asked under <see cref="ConfirmationPolicy.Always"/>.
/// Texts name items and storages by display name only, never by path or id (T-07-08).
/// </summary>
public static class StorageConfirmation
{
    /// <summary>True when <paramref name="plan"/> is destructive; <paramref name="crossConnection"/> says whether source and target folder live on different connections.</summary>
    public static bool IsDestructive(WritePlan plan, bool crossConnection)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Kind == WriteKind.Delete
            || plan.Conflict == ConflictPolicy.Overwrite
            || (plan.Kind == WriteKind.Move && crossConnection);
    }

    /// <summary>The question for <paramref name="plan"/>.</summary>
    /// <param name="planId">The confirmation plan id.</param>
    /// <param name="plan">The storage plan.</param>
    /// <param name="crossConnection">True when source and target folder live on different connections.</param>
    /// <param name="name">The display name of the item acted on (or created).</param>
    /// <param name="storage">The display name of the target storage (the source storage for delete).</param>
    /// <param name="sourceStorage">The display name of the source storage.</param>
    /// <param name="folder">The display name of the target folder.</param>
    public static ConfirmationPlan For(string planId, WritePlan plan, bool crossConnection, string name, string storage, string sourceStorage, string folder)
        => For(planId, plan, crossConnection, name, storage, sourceStorage, folder, string.Empty);

    internal static ConfirmationPlan For(string planId, WritePlan plan, bool crossConnection, string name, string storage, string sourceStorage, string folder, string sourceFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(sourceStorage);
        ArgumentNullException.ThrowIfNull(folder);
        var source = Location(sourceFolder, sourceStorage);
        var destination = Location(folder, storage);
        if (plan.Kind == WriteKind.Delete)
        {
            return plan.Permanent
                ? new(planId, ConfirmationAction.Delete, true, $"Permanently delete {name}?",
                    $"This permanently removes {name} from {source}. You cannot undo this.", "Delete permanently", "Keep", name)
                : new(planId, ConfirmationAction.Delete, true, $"Move {name} to trash?",
                    $"This moves {name} from {source} to trash.", "Move to trash", "Keep", name);
        }
        var targetName = plan.Kind == WriteKind.Rename ? plan.Name! : name;
        var (action, verb) = plan.Kind switch
        {
            WriteKind.Move => (ConfirmationAction.Move, "Move"),
            WriteKind.Copy => (ConfirmationAction.Copy, "Copy"),
            WriteKind.Rename => (ConfirmationAction.Rename, "Rename"),
            WriteKind.CreateFolder => (ConfirmationAction.Other, "Create"),
            _ => (ConfirmationAction.Other, "Save"),
        };
        var title = plan.Kind == WriteKind.Rename ? $"Rename {name} to {targetName}?" : $"{verb} {name} in {destination}?";
        var body = plan.Kind switch
        {
            WriteKind.Move when crossConnection => $"{name} in {source} is copied to {targetName} in {destination} and verified, then the source is moved to trash.",
            WriteKind.Move or WriteKind.Copy or WriteKind.Rename => $"{verb} {name} in {source} to {targetName} in {destination}.",
            _ => $"{verb} {targetName} in {destination}.",
        };
        if (plan.Conflict == ConflictPolicy.Overwrite)
        {
            body += $" If {targetName} already exists in {destination}, it will be replaced. Replacing an existing item cannot be undone.";
            if (plan.Kind is WriteKind.CreateFolder or WriteKind.WriteFile) { action = ConfirmationAction.Overwrite; }
        }
        return new(planId, action, IsDestructive(plan, crossConnection), title, body, verb, "Cancel", targetName);
    }

    private static string Location(string folder, string storage)
        => string.IsNullOrWhiteSpace(folder) ? storage : $"{folder} on {storage}";
}
