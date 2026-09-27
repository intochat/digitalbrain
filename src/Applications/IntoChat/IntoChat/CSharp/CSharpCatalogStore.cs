using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Core;
using Orleans;

namespace IntoChat;

internal sealed record CSharpDescription(string Id, string Name, string Purpose, DateTimeOffset CreatedAt);

// The workspace owns the human-facing names of its files; the ICSharpFile neuron owns source and execution.
internal sealed class CSharpCatalogStore
{
    private const int MaxFilesPerWorkspace = 500;
    private readonly IDocumentStore<CSharpCatalog> _workspaces;

    public CSharpCatalogStore(IGrainFactory grains) : this(new GrainDocumentStore<CSharpCatalog>(grains, "csharp-catalog")) { }

    internal CSharpCatalogStore(IDocumentStore<CSharpCatalog> workspaces) => _workspaces = workspaces;

    public static string FileKey(string scope, string id)
    {
        ValidateFileId(scope, id);
        return "csharp-" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] { scope, id })));
    }

    private static void ValidateFileId(string scope, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
        { throw new ArgumentException("Use a C# file ID containing 1–100 ASCII letters, digits, hyphens or underscores.", nameof(id)); }
    }

    public Task<CSharpDescription> Describe(string scope, string id, string? name, string? purpose, CancellationToken ct)
    {
        ValidateFileId(scope, id);
        if (name is { Length: > 120 } || purpose is { Length: > 4000 })
        { throw new ArgumentException("A name is at most 120 characters and a purpose at most 4000."); }
        return _workspaces.UpdateAsync(scope, catalog =>
        {
            if (!catalog.Files.TryGetValue(id, out var current))
            {
                if (catalog.Files.Count >= MaxFilesPerWorkspace)
                { throw new InvalidOperationException($"This workspace has reached its {MaxFilesPerWorkspace}-file limit."); }
                current = new(id, id, "", DateTimeOffset.UtcNow);
            }
            return catalog.Files[id] = current with
            {
                Name = string.IsNullOrWhiteSpace(name) ? current.Name : name.Trim(),
                Purpose = purpose?.Trim() ?? current.Purpose,
            };
        }, ct);
    }

    public Task<CSharpDescription[]> List(string scope, CancellationToken ct)
        => _workspaces.ReadAsync(scope, catalog => catalog.Files.Values.OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase).ToArray(), ct);

    public Task<CSharpDescription> Read(string scope, string id, CancellationToken ct)
        => _workspaces.ReadAsync(scope, catalog => catalog.Files.TryGetValue(id, out var file)
            ? file
            : throw new KeyNotFoundException($"{id} is not a C# file in this workspace."), ct);

    public Task Remove(string scope, string id, CancellationToken ct) => _workspaces.UpdateAsync(scope, catalog => catalog.Files.Remove(id), ct);
}

internal sealed class CSharpCatalog
{
    public Dictionary<string, CSharpDescription> Files { get; set; } = new(StringComparer.Ordinal);
}
