using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Core;
using Orleans;

namespace IntoChat;

internal sealed record CSharpDescription(string Id, string Name, string Purpose, DateTimeOffset CreatedAt);

// The workspace owns the human-facing name and the list of files; the ICSharpFile neuron owns source and execution.
internal sealed class CSharpCatalogStore
{
    private const int MaxFilesPerWorkspace = 500;
    private readonly IDocumentStore<CSharpCatalogIndex> _indexes;
    private readonly IDocumentStore<CSharpCatalogItem> _items;

    public CSharpCatalogStore(IGrainFactory grains)
        : this(new GrainDocumentStore<CSharpCatalogIndex>(grains, "csharp-catalog-index"),
            new GrainDocumentStore<CSharpCatalogItem>(grains, "csharp-catalog-items")) { }

    internal CSharpCatalogStore(IDocumentStore<CSharpCatalogIndex> indexes, IDocumentStore<CSharpCatalogItem> items)
    {
        _indexes = indexes;
        _items = items;
    }

    public static string FileKey(string scope, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
        { throw new ArgumentException("Use a C# file ID containing 1–100 ASCII letters, digits, hyphens or underscores.", nameof(id)); }
        return "csharp-" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] { scope, id })));
    }

    public async Task<CSharpDescription> Describe(string scope, string id, string? name, string? purpose, CancellationToken ct)
    {
        if (name is { Length: > 120 } || purpose is { Length: > 4000 })
        { throw new ArgumentException("A name is at most 120 characters and a purpose at most 4000."); }
        var described = await _items.UpdateAsync(FileKey(scope, id), item =>
        {
            var current = item.Description ?? new(id, id, "", DateTimeOffset.UtcNow);
            return item.Description = current with
            {
                Name = string.IsNullOrWhiteSpace(name) ? current.Name : name.Trim(),
                Purpose = purpose?.Trim() ?? current.Purpose,
            };
        }, ct);
        await _indexes.UpdateAsync(scope, index =>
        {
            if (!index.Ids.Contains(id) && index.Ids.Count >= MaxFilesPerWorkspace)
            { throw new InvalidOperationException($"This workspace has reached its {MaxFilesPerWorkspace}-file limit."); }
            index.Id = scope;
            return index.Ids.Add(id);
        }, ct);
        return described;
    }

    public async Task<IReadOnlyList<CSharpDescription>> List(string scope, CancellationToken ct)
    {
        var ids = await _indexes.ReadAsync(scope, index => index.Ids.ToArray(), ct);
        var items = new List<CSharpDescription>();
        foreach (var id in ids) { items.Add(await Read(scope, id, ct)); }
        return items.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public Task<CSharpDescription> Read(string scope, string id, CancellationToken ct)
        => _items.ReadAsync(FileKey(scope, id), item => item.Description ?? throw new KeyNotFoundException($"{id} is not a C# file in this workspace."), ct);

    public async Task Remove(string scope, string id, CancellationToken ct)
    {
        await _items.UpdateAsync(FileKey(scope, id), item => item.Description = null, ct);
        await _indexes.UpdateAsync(scope, index =>
        {
            index.Id = scope;
            return index.Ids.Remove(id);
        }, ct);
    }
}

internal sealed class CSharpCatalogIndex
{
    public string Id { get; set; } = "";
    public HashSet<string> Ids { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class CSharpCatalogItem
{
    public CSharpDescription? Description { get; set; }
}
