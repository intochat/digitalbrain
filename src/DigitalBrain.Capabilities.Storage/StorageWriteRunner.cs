using System.Globalization;
using DigitalBrain.Capabilities;
using DigitalBrain.Operations;
using DigitalBrain.Spi;

namespace DigitalBrain.Capabilities.Storage;

/// <summary>The storage connection that owns <paramref name="item"/>.</summary>
/// <exception cref="StorageException">No connection owns the item (for example <see cref="NotFound"/> or <see cref="Unavailable"/>).</exception>
public delegate Task<CapabilityConnection<IStorage>> StorageOwner(ItemId item, CancellationToken ct);

/// <summary>Waits for the result of the operation a storage connection's <see cref="IStorage.Write"/> started.</summary>
public delegate Task<OperationResult> StorageWriteResult(IStorage storage, OperationId operation, CancellationToken ct);

/// <summary>
/// Carries out a <see cref="WritePlan"/> as an operation (D-04), the same way on the runtime and in TestOS. A dry run lists one
/// step per primitive action and changes nothing. The conflict policy is checked before anything is written:
/// <see cref="ConflictPolicy.Fail"/> (the default) ends <see cref="OperationResult.Failed"/> with code <c>conflict</c>,
/// <see cref="ConflictPolicy.Rename"/> picks the first free <c>name (n)</c>, <see cref="ConflictPolicy.Overwrite"/> replaces only
/// when the plan says so. A move or copy between two connections copies, verifies, and (for a move) only then deletes the source
/// to the trash; see <see cref="Run"/>. Storage errors end the operation <see cref="OperationResult.Failed"/> with their code.
/// </summary>
/// <param name="owner">Finds the connection owning an item (the broker, for the operation's own brain).</param>
/// <param name="result">Waits for the result of a connection's own write operation.</param>
public sealed class StorageWriteRunner(StorageOwner owner, StorageWriteResult result)
{
    private readonly StorageOwner _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    private readonly StorageWriteResult _result = result ?? throw new ArgumentNullException(nameof(result));

    /// <summary>
    /// Runs <paramref name="plan"/>, reporting progress to and observing cancellation of <paramref name="operation"/>, and returns
    /// how it ended; the caller completes the operation with it. A move between connections that copied and verified but could not
    /// delete the source ends <see cref="OperationResult.Failed"/> with code <c>source-delete-failed</c>: both copies exist.
    /// </summary>
    public async Task<OperationResult> Run(WritePlan plan, IOperation operation)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(operation);
        var ct = operation.Cancellation;
        try
        {
            ct.ThrowIfCancellationRequested();
            var source = plan.Source is { } from ? await _owner(from, ct).ConfigureAwait(false) : null;
            var target = plan.TargetFolder is { } folder ? await _owner(folder, ct).ConfigureAwait(false) : null;
            if (plan.Kind is WriteKind.Move or WriteKind.Copy && source is not null && target is not null
                && !string.Equals(source.ConnectionKey, target.ConnectionKey, StringComparison.Ordinal))
            {
                return await new CrossConnectionMove(source, target, plan, operation, this).Run().ConfigureAwait(false);
            }
            var connection = source ?? target ?? throw new ArgumentException("The plan names neither a source nor a target folder.", nameof(plan));
            if (plan.DryRun) { return new OperationResult.Planned([Step(plan, connection.ConnectionKey)]); }
            var effective = await ApplyConflict(connection.Capability, plan, ct).ConfigureAwait(false);
            if (effective is null)
            {
                return new OperationResult.Failed("conflict", $"storage conflict: {plan.Name ?? plan.Source?.Value}");
            }
            await operation.Report(0).ConfigureAwait(false);
            var done = await Write(connection.Capability, effective, ct).ConfigureAwait(false);
            if (done is OperationResult.Succeeded) { await operation.Report(1).ConfigureAwait(false); }
            return done;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new OperationResult.Cancelled();
        }
        catch (StorageException error)
        {
            return new OperationResult.Failed(error.Code, error.Message);
        }
    }

    /// <summary>
    /// The confirmation question for <paramref name="plan"/> (07 D-03): resolves the owning connections to tell a move between
    /// connections from one within a connection, and names the item, storages and folder by display name. An item the connection
    /// cannot stat is named "this item".
    /// </summary>
    public async Task<DigitalBrain.Confirmation.ConfirmationPlan> Confirmation(WritePlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var source = plan.Source is { } from ? await _owner(from, ct).ConfigureAwait(false) : null;
        var target = plan.TargetFolder is { } into ? await _owner(into, ct).ConfigureAwait(false) : null;
        var cross = source is not null && target is not null && !string.Equals(source.ConnectionKey, target.ConnectionKey, StringComparison.Ordinal);
        var name = plan.Kind is WriteKind.CreateFolder or WriteKind.WriteFile ? plan.Name! : await NameOf(source, plan.Source, ct).ConfigureAwait(false);
        var sourceFolder = await ParentNameOf(source, plan.Source, ct).ConfigureAwait(false);
        var folder = target is null ? sourceFolder : await NameOf(target, plan.TargetFolder, ct).ConfigureAwait(false);
        var storage = (plan.Kind == WriteKind.Delete ? source : target ?? source)?.Label ?? "this storage";
        return StorageConfirmation.For("cf-" + Guid.NewGuid().ToString("N"), plan, cross, name, storage, source?.Label ?? storage, folder, sourceFolder);
    }

    private static async Task<string> ParentNameOf(CapabilityConnection<IStorage>? connection, ItemId? item, CancellationToken ct)
    {
        if (connection is null || item is not { } id) { return string.Empty; }
        try
        {
            var ancestors = await connection.Capability.Ancestors(id, ct).ConfigureAwait(false);
            return ancestors.Count > 0 ? ancestors[^1].Name : string.Empty;
        }
        catch (StorageException) { return string.Empty; }
    }

    private static async Task<string> NameOf(CapabilityConnection<IStorage>? connection, ItemId? item, CancellationToken ct)
    {
        if (connection is null || item is not { } id) { return "this item"; }
        try
        {
            var name = (await connection.Capability.Stat(id, ct).ConfigureAwait(false)).Name;
            return string.IsNullOrWhiteSpace(name) ? connection.Label : name;
        }
        catch (StorageException)
        {
            return "this item";
        }
    }

    /// <summary>The first name of <c>stem (n).ext</c>, n from 1, that is not in <paramref name="taken"/> (case-insensitive).</summary>
    public static string FreeName(string name, IEnumerable<string> taken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(taken);
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(name)) { return name; }
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var n = 1; ; n++)
        {
            var candidate = $"{stem} ({n.ToString(CultureInfo.InvariantCulture)}){extension}";
            if (!used.Contains(candidate)) { return candidate; }
        }
    }

    // Starts a connection's own write and waits for how it ended.
    internal async Task<OperationResult> Write(IStorage storage, WritePlan plan, CancellationToken ct)
    {
        var id = await storage.Write(plan, ct).ConfigureAwait(false);
        return await _result(storage, id, ct).ConfigureAwait(false);
    }

    // Every item of a folder, all pages.
    internal static async Task<List<Item>> Children(IStorage storage, ItemId folder, CancellationToken ct)
    {
        var all = new List<Item>();
        string? cursor = null;
        do
        {
            var page = await storage.List(folder, new ListQuery(cursor, ListQuery.MaxPageSize), ct).ConfigureAwait(false);
            all.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);
        return all;
    }

    // The plan to send after the conflict policy; null when it fails on conflict.
    private static async Task<WritePlan?> ApplyConflict(IStorage storage, WritePlan plan, CancellationToken ct)
    {
        ItemId folder;
        string name;
        ItemId? self = null;
        switch (plan.Kind)
        {
            case WriteKind.CreateFolder or WriteKind.WriteFile:
                folder = plan.TargetFolder!.Value;
                name = plan.Name!;
                break;
            case WriteKind.Rename:
                {
                    var chain = await storage.Ancestors(plan.Source!.Value, ct).ConfigureAwait(false);
                    if (chain.Count == 0) { return plan; }
                    folder = chain[^1].Id;
                    name = plan.Name!;
                    self = plan.Source;
                    break;
                }
            case WriteKind.Move or WriteKind.Copy:
                folder = plan.TargetFolder!.Value;
                name = (await storage.Stat(plan.Source!.Value, ct).ConfigureAwait(false)).Name;
                self = plan.Kind == WriteKind.Move ? plan.Source : null;
                break;
            default:
                return plan;
        }
        var taken = (await Children(storage, folder, ct).ConfigureAwait(false)).Where(item => item.Id != self).Select(item => item.Name).ToList();
        if (!taken.Contains(name, StringComparer.OrdinalIgnoreCase)) { return plan; }
        return plan.Conflict switch
        {
            ConflictPolicy.Fail => null,
            // Move and copy carry no name: the connection picks the free name under its own Rename rule.
            ConflictPolicy.Rename when plan.Kind is WriteKind.Move or WriteKind.Copy => plan,
            ConflictPolicy.Rename => plan with { Name = FreeName(name, taken), Conflict = ConflictPolicy.Fail },
            _ => plan,
        };
    }

    internal static string Step(WritePlan plan, string connection)
    {
        var kind = plan.Kind switch
        {
            WriteKind.CreateFolder => "create-folder",
            WriteKind.WriteFile => "write-file",
            _ => plan.Kind.ToString().ToLowerInvariant(),
        };
        return plan.Kind switch
        {
            WriteKind.CreateFolder or WriteKind.WriteFile => $"{kind} {connection}:{plan.TargetFolder?.Value}/{plan.Name}",
            WriteKind.Rename => $"{kind} {connection}:{plan.Source?.Value} -> {plan.Name}",
            WriteKind.Delete => $"{kind} {connection}:{plan.Source?.Value}{(plan.Permanent ? " permanent" : " to-trash")}",
            _ => $"{kind} {connection}:{plan.Source?.Value} -> {connection}:{plan.TargetFolder?.Value}",
        };
    }
}
