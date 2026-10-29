using System.Globalization;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Operations;
using SortKey = DigitalBrain.Capabilities.Storage.SortKey;

namespace DigitalBrain.Testing.Conformance.Storage;

/// <summary>
/// The storage@1 reference implementation, held in memory: one root folder, opaque ids (<c>m:&lt;guid&gt;</c>), real cursor
/// paging, a trash folder outside the roots, conflict fail by default, dry runs, and streaming read/write handles whose
/// writes become visible only on commit. Writes run synchronously through <see cref="Operations"/>. Path-shaped ids
/// (containing <c>/</c>, <c>\</c> or <c>..</c>) are outside its confinement and fail with <see cref="Denied"/>.
/// It passes <see cref="StorageConformance{TStorage}"/>.
/// </summary>
public class InMemoryStorage : IStorage
{
    /// <summary>Every action flag a writable in-memory storage offers.</summary>
    public const ItemFlags AllFlags = ItemFlags.Read | ItemFlags.Write | ItemFlags.Trash | ItemFlags.Delete | ItemFlags.Move
        | ItemFlags.Copy | ItemFlags.Rename | ItemFlags.Search | ItemFlags.CreateFolder;

    private readonly Lock _gate = new();
    private readonly Dictionary<ItemId, Node> _nodes = [];
    private readonly Node _root;
    private readonly Node _trash;
    private readonly TimeProvider _time;
    private readonly string _kind;

    /// <summary>Creates an empty storage with one root folder named <paramref name="label"/>.</summary>
    /// <param name="label">The display label, also the root folder's name.</param>
    /// <param name="quota">The quota <see cref="Describe"/> reports; <see cref="Quota.Unknown"/> when null.</param>
    /// <param name="flags">The actions offered; <see cref="AllFlags"/> by default. Without <see cref="ItemFlags.Write"/> the storage is read-only.</param>
    /// <param name="operations">The operations writes run through; a new one when null.</param>
    /// <param name="time">The clock for modification times; the system clock when null.</param>
    /// <param name="kind">The storage kind <see cref="Describe"/> reports.</param>
    public InMemoryStorage(string label = "Memory", Quota? quota = null, ItemFlags flags = AllFlags, InMemoryOperations? operations = null, TimeProvider? time = null, string kind = "memory")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Label = label;
        Quota = quota ?? Quota.Unknown;
        Flags = flags;
        Operations = operations ?? new InMemoryOperations();
        _time = time ?? TimeProvider.System;
        _kind = kind;
        _root = NewNode(label, ItemKind.Folder, null, null);
        _trash = NewNode("Trash", ItemKind.Folder, null, null);
    }

    /// <summary>The display label.</summary>
    public string Label { get; }

    /// <summary>The quota <see cref="Describe"/> reports.</summary>
    public Quota Quota { get; set; }

    /// <summary>The actions this storage offers.</summary>
    public ItemFlags Flags { get; }

    /// <summary>The operations writes run through; also the kit's <see cref="IOperationsProbe"/>.</summary>
    public InMemoryOperations Operations { get; }

    /// <summary>The id of the root folder.</summary>
    public ItemId Root => _root.Id;

    /// <summary>The id of the trash folder (not a root; deleted items land here).</summary>
    public ItemId Trash => _trash.Id;

    /// <summary>Creates <paramref name="seed"/> under the root folder.</summary>
    public void Seed(StorageSeed seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        lock (_gate)
        {
            foreach (var entry in seed.Entries)
            {
                var parts = entry.Path.Split('/');
                var parent = _root;
                foreach (var part in parts[..^1])
                {
                    parent = parent.Children.Single(child => child.Name == part);
                }
                _ = NewNode(parts[^1], entry.Kind, parent, entry.Kind == ItemKind.File ? entry.Bytes.ToArray() : null);
            }
        }
    }

    /// <summary>Adds a folder without an operation (test setup).</summary>
    public ItemId AddFolder(ItemId parent, string name)
    {
        lock (_gate) { return NewNode(name, ItemKind.Folder, Folder(parent), null).Id; }
    }

    /// <summary>Adds a file without an operation (test setup).</summary>
    public ItemId AddFile(ItemId parent, string name, ReadOnlySpan<byte> bytes)
    {
        var copy = bytes.ToArray();
        lock (_gate) { return NewNode(name, ItemKind.File, Folder(parent), copy).Id; }
    }

    /// <summary>Called on entry of every <see cref="IStorage"/> member with its name; fakes record calls here.</summary>
    protected virtual void OnCall(string member)
    {
    }

    /// <inheritdoc />
    public Task<StorageDescriptor> Describe(CancellationToken ct = default)
    {
        OnCall(nameof(Describe));
        return Task.FromResult(new StorageDescriptor(Label, _kind, Quota, Flags));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Item>> Roots(CancellationToken ct = default)
    {
        OnCall(nameof(Roots));
        lock (_gate) { return Task.FromResult<IReadOnlyList<Item>>([ToItem(_root)]); }
    }

    /// <inheritdoc />
    public Task<Page<Item>> List(ItemId folder, ListQuery query, CancellationToken ct = default)
    {
        OnCall(nameof(List));
        ArgumentNullException.ThrowIfNull(query);
        lock (_gate)
        {
            var items = Sort(Folder(folder).Children.Select(ToItem), query.Sort, query.Descending);
            return Task.FromResult(Paged(items, query.Cursor, query.PageSize));
        }
    }

    /// <inheritdoc />
    public Task<Item> Stat(ItemId id, CancellationToken ct = default)
    {
        OnCall(nameof(Stat));
        lock (_gate) { return Task.FromResult(ToItem(Find(id))); }
    }

    /// <inheritdoc />
    public Task<Content> Read(ItemId id, ByteRange? range, CancellationToken ct = default)
    {
        OnCall(nameof(Read));
        RequireFlag(ItemFlags.Read, id);
        lock (_gate)
        {
            var file = File(id);
            return Task.FromResult(new Content(Slice(file.Bytes!, range), MediaType(file.Name), file.Bytes!.Length));
        }
    }

    /// <inheritdoc />
    public async Task<OperationId> Write(WritePlan plan, CancellationToken ct = default)
    {
        OnCall(nameof(Write));
        ArgumentNullException.ThrowIfNull(plan);
        RequireFlag(Required(plan), plan.Source);
        var operation = await Operations.Start(plan, ct).ConfigureAwait(false);
        OperationResult result;
        try
        {
            lock (_gate)
            {
                result = plan.DryRun ? new OperationResult.Planned(Plan(plan)) : Apply(plan);
            }
        }
        catch (StorageException error)
        {
            result = new OperationResult.Failed(error.Code, error.Message);
        }
        await operation.Report(1).ConfigureAwait(false);
        await operation.Complete(result).ConfigureAwait(false);
        return operation.Id;
    }

    /// <inheritdoc />
    public Task<Page<Item>> Search(SearchQuery query, CancellationToken ct = default)
    {
        OnCall(nameof(Search));
        ArgumentNullException.ThrowIfNull(query);
        RequireFlag(ItemFlags.Search, query.Within);
        lock (_gate)
        {
            var start = query.Within is { } within ? Folder(within) : _root;
            var found = Descendants(start).Where(node => node.Name.Contains(query.Text, StringComparison.OrdinalIgnoreCase)).Select(ToItem);
            return Task.FromResult(Paged(Sort(found, SortKey.FolderThenName, false), query.Cursor, query.PageSize));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Item>> Ancestors(ItemId id, CancellationToken ct = default)
    {
        OnCall(nameof(Ancestors));
        lock (_gate)
        {
            var chain = new List<Item>();
            for (var node = Find(id).Parent; node is not null; node = node.Parent)
            {
                chain.Insert(0, ToItem(node));
            }
            return Task.FromResult<IReadOnlyList<Item>>(chain);
        }
    }

    /// <inheritdoc />
    public Task<StorageReadStream> OpenRead(ItemId item, ByteRange? range, CancellationToken ct = default)
    {
        OnCall(nameof(OpenRead));
        RequireFlag(ItemFlags.Read, item);
        lock (_gate)
        {
            var file = File(item);
            var bytes = Slice(file.Bytes!, range).ToArray();
            return Task.FromResult(new StorageReadStream(new MemoryStream(bytes, writable: false), bytes.Length, MediaType(file.Name),
                $"\"{file.Version.ToString(CultureInfo.InvariantCulture)}\"", canSeek: true, file.Modified));
        }
    }

    /// <inheritdoc />
    public Task<StorageWriteStream> OpenWrite(WritePlan plan, CancellationToken ct = default)
    {
        OnCall(nameof(OpenWrite));
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Kind != WriteKind.WriteFile || plan.TargetFolder is not { } folder || plan.Name is null)
        {
            throw new ArgumentException("OpenWrite takes a WriteFile plan.", nameof(plan));
        }
        RequireFlag(ItemFlags.Write, folder);
        lock (_gate)
        {
            var target = Folder(folder);
            if (plan.Conflict == ConflictPolicy.Fail && Child(target, plan.Name) is { } existing)
            {
                throw new Conflict(existing.Id);
            }
        }
        return Task.FromResult<StorageWriteStream>(new WriteHandle(this, plan));
    }

    private OperationResult.Succeeded Apply(WritePlan plan)
    {
        switch (plan.Kind)
        {
            case WriteKind.CreateFolder:
                Place(Folder(plan.TargetFolder!.Value), plan.Name!, plan.Conflict, name => NewNode(name, ItemKind.Folder, null, null));
                break;
            case WriteKind.WriteFile:
                PutFile(plan.TargetFolder!.Value, plan.Name!, plan.Conflict, (plan.Inline ?? ReadOnlyMemory<byte>.Empty).ToArray());
                break;
            case WriteKind.Rename:
                {
                    var node = Movable(plan.Source!.Value);
                    var name = Free(node.Parent!, plan.Name!, plan.Conflict, node);
                    RemoveReplaced(node.Parent!, name, plan.Conflict, node);
                    node.Name = name;
                    node.Touch(_time);
                    break;
                }
            case WriteKind.Move:
                {
                    var node = Movable(plan.Source!.Value);
                    var target = Folder(plan.TargetFolder!.Value);
                    for (var up = target; up is not null; up = up.Parent)
                    {
                        if (up == node) { throw new Denied(target.Id); }
                    }
                    var name = Free(target, node.Name, plan.Conflict, node);
                    RemoveReplaced(target, name, plan.Conflict, node);
                    Detach(node);
                    node.Name = name;
                    Attach(target, node);
                    break;
                }
            case WriteKind.Copy:
                {
                    var node = Find(plan.Source!.Value);
                    var target = Folder(plan.TargetFolder!.Value);
                    var name = Free(target, node.Name, plan.Conflict, null);
                    if (plan.Conflict == ConflictPolicy.Overwrite && Child(target, name) == node) { break; }
                    RemoveReplaced(target, name, plan.Conflict, null);
                    CopyInto(node, target, name);
                    break;
                }
            case WriteKind.Delete:
                {
                    var node = Movable(plan.Source!.Value);
                    Detach(node);
                    if (plan.Permanent)
                    {
                        foreach (var gone in Descendants(node).Prepend(node)) { _nodes.Remove(gone.Id); }
                    }
                    else
                    {
                        Attach(_trash, node);
                    }
                    break;
                }
            default:
                throw new Unsupported(plan.Source);
        }
        return new OperationResult.Succeeded();
    }

    private ItemId PutFile(ItemId folder, string name, ConflictPolicy conflict, byte[] bytes)
    {
        var target = Folder(folder);
        if (conflict == ConflictPolicy.Overwrite && Child(target, name) is { Kind: ItemKind.File } existing)
        {
            existing.Bytes = bytes;
            existing.Touch(_time);
            return existing.Id;
        }
        return Place(target, name, conflict, free => NewNode(free, ItemKind.File, null, bytes)).Id;
    }

    private Node Place(Node folder, string name, ConflictPolicy conflict, Func<string, Node> create)
    {
        var free = Free(folder, name, conflict, null);
        RemoveReplaced(folder, free, conflict, null);
        var node = create(free);
        Attach(folder, node);
        return node;
    }

    private void RemoveReplaced(Node folder, string name, ConflictPolicy conflict, Node? self)
    {
        if (conflict == ConflictPolicy.Overwrite && Child(folder, name) is { } existing && existing != self)
        {
            Detach(existing);
            foreach (var gone in Descendants(existing).Prepend(existing)) { _nodes.Remove(gone.Id); }
        }
    }

    // The name to use in folder: the name itself when free; per policy otherwise.
    private static string Free(Node folder, string name, ConflictPolicy conflict, Node? self)
    {
        var existing = Child(folder, name);
        if (existing is null || existing == self) { return name; }
        switch (conflict)
        {
            case ConflictPolicy.Overwrite:
                return name;
            case ConflictPolicy.Rename:
                var stem = Path.GetFileNameWithoutExtension(name);
                var extension = Path.GetExtension(name);
                for (var n = 2; ; n++)
                {
                    var candidate = $"{stem} ({n.ToString(CultureInfo.InvariantCulture)}){extension}";
                    if (Child(folder, candidate) is null) { return candidate; }
                }
            default:
                throw new Conflict(existing.Id);
        }
    }

    private void CopyInto(Node source, Node folder, string name)
    {
        var copy = NewNode(name, source.Kind, null, source.Bytes?.ToArray());
        Attach(folder, copy);
        foreach (var child in source.Children.ToList())
        {
            CopyInto(child, copy, child.Name);
        }
    }

    private static List<string> Plan(WritePlan plan) =>
        [$"{plan.Kind.ToString().ToLowerInvariant()} {plan.Source?.Value ?? plan.TargetFolder?.Value} {plan.Name}".TrimEnd()];

    private static ItemFlags Required(WritePlan plan) => plan.Kind switch
    {
        WriteKind.CreateFolder => ItemFlags.CreateFolder,
        WriteKind.WriteFile => ItemFlags.Write,
        WriteKind.Move => ItemFlags.Move,
        WriteKind.Copy => ItemFlags.Copy,
        WriteKind.Rename => ItemFlags.Rename,
        WriteKind.Delete => plan.Permanent ? ItemFlags.Delete : ItemFlags.Trash,
        _ => ItemFlags.Write,
    };

    private void RequireFlag(ItemFlags flag, ItemId? item)
    {
        if (!Flags.HasFlag(flag)) { throw new Unsupported(item); }
    }

    private Node Find(ItemId id)
    {
        var value = id.Value ?? string.Empty;
        if (value.Contains('/', StringComparison.Ordinal) || value.Contains('\\', StringComparison.Ordinal) || value.Contains("..", StringComparison.Ordinal))
        {
            throw new Denied(id);
        }
        return _nodes.TryGetValue(id, out var node) ? node : throw new NotFound(id);
    }

    private Node Folder(ItemId id) => Find(id) is { Kind: ItemKind.Folder } folder ? folder : throw new Unsupported(id);

    private Node File(ItemId id) => Find(id) is { Kind: ItemKind.File } file ? file : throw new Unsupported(id);

    private Node Movable(ItemId id)
    {
        var node = Find(id);
        return node == _root || node == _trash ? throw new Denied(id) : node;
    }

    private static Node? Child(Node folder, string name) =>
        folder.Children.FirstOrDefault(child => string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.Children)
        {
            yield return child;
            foreach (var below in Descendants(child)) { yield return below; }
        }
    }

    private Node NewNode(string name, ItemKind kind, Node? parent, byte[]? bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var node = new Node(new ItemId($"m:{Guid.NewGuid():N}"), name, kind, kind == ItemKind.File ? bytes ?? [] : null, _time.GetUtcNow());
        _nodes[node.Id] = node;
        if (parent is not null) { Attach(parent, node); }
        return node;
    }

    private static void Attach(Node folder, Node node)
    {
        node.Parent = folder;
        folder.Children.Add(node);
    }

    private static void Detach(Node node)
    {
        node.Parent?.Children.Remove(node);
        node.Parent = null;
    }

    private Item ToItem(Node node)
    {
        var actions = Flags & ~(ItemFlags.Shared | ItemFlags.CloudOnly);
        if (node == _root) { actions &= ItemFlags.Read | ItemFlags.Write | ItemFlags.Search | ItemFlags.CreateFolder; }
        if (node.Kind == ItemKind.File) { actions &= ~(ItemFlags.Search | ItemFlags.CreateFolder); }
        return new Item(node.Id, node.Name, node.Kind, node.Bytes?.LongLength, node.Modified, actions,
            node.Bytes is { } bytes ? $"sha256:{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))}" : null);
    }

    private static List<Item> Sort(IEnumerable<Item> items, SortKey sort, bool descending)
    {
        var ordered = sort switch
        {
            SortKey.Name => items.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase),
            SortKey.Modified => items.OrderBy(item => item.Modified),
            SortKey.Size => items.OrderBy(item => item.Size ?? 0),
            _ => items.OrderBy(item => item.Kind == ItemKind.Folder ? 0 : 1).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase),
        };
        var list = ordered.ThenBy(item => item.Name, StringComparer.Ordinal).ThenBy(item => item.Id.Value, StringComparer.Ordinal).ToList();
        if (descending) { list.Reverse(); }
        return list;
    }

    private static Page<Item> Paged(List<Item> items, string? cursor, int pageSize)
    {
        var offset = 0;
        if (cursor is not null && (!cursor.StartsWith("o:", StringComparison.Ordinal)
            || !int.TryParse(cursor.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out offset) || offset > items.Count))
        {
            throw new ArgumentException("Unknown cursor.", nameof(cursor));
        }
        var page = items.Skip(offset).Take(pageSize).ToList();
        var next = offset + page.Count;
        return new Page<Item>(page, next < items.Count ? $"o:{next.ToString(CultureInfo.InvariantCulture)}" : null);
    }

    private static ReadOnlyMemory<byte> Slice(byte[] bytes, ByteRange? range)
    {
        if (range is not { } r) { return bytes; }
        var start = (int)Math.Min(r.Offset, bytes.Length);
        var length = (int)Math.Min(r.Length ?? long.MaxValue, bytes.Length - start);
        return bytes.AsMemory(start, length);
    }

    private static string MediaType(string name) => Path.GetExtension(name).ToUpperInvariant() switch
    {
        ".TXT" => "text/plain",
        ".JSON" => "application/json",
        _ => "application/octet-stream",
    };

    private ItemId CommitWrite(WritePlan plan, byte[] bytes)
    {
        lock (_gate) { return PutFile(plan.TargetFolder!.Value, plan.Name!, plan.Conflict, bytes); }
    }

    private sealed class Node(ItemId id, string name, ItemKind kind, byte[]? bytes, DateTimeOffset modified)
    {
        public ItemId Id { get; } = id;

        public string Name { get; set; } = name;

        public ItemKind Kind { get; } = kind;

        public byte[]? Bytes { get; set; } = bytes;

        public DateTimeOffset Modified { get; private set; } = modified;

        public long Version { get; private set; }

        public Node? Parent { get; set; }

        public List<Node> Children { get; } = [];

        public void Touch(TimeProvider time)
        {
            Modified = time.GetUtcNow();
            Version++;
        }
    }

    // Buffers the bytes; nothing is visible until Commit. Abort and dispose discard the buffer.
    private sealed class WriteHandle(InMemoryStorage owner, WritePlan plan) : StorageWriteStream(new MemoryStream())
    {
        protected override Task<ItemId> CommitCore(CancellationToken ct) =>
            Task.FromResult(owner.CommitWrite(plan, ((MemoryStream)Content).ToArray()));

        protected override Task AbortCore(CancellationToken ct) => Task.CompletedTask;
    }
}
