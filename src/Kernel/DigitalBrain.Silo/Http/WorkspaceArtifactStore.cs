using System.Text.Json;

namespace DigitalBrain.Kernel;

internal sealed record WorkspaceArtifact(string Id, string Kind, string Title, long Revision, JsonElement Content);
internal sealed record CreateWorkspaceArtifact(string Kind, string Title, JsonElement Content);
internal sealed record UpdateWorkspaceArtifact(long ExpectedRevision, string Title, JsonElement Content);
internal sealed class ArtifactConflictException() : InvalidOperationException("The artifact changed. Read its current revision before applying your edit.");

/// <summary>Single-owner durable documents. Tables continue to use authoritative UI neurons.</summary>
internal sealed class WorkspaceArtifactStore(IConfiguration configuration, IHostEnvironment environment)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _directory = Path.GetFullPath(configuration["DigitalBrain:Workspace:StoragePath"]
        ?? Path.Combine(environment.ContentRootPath, ".digitalbrain", "workspace"));

    public async Task<WorkspaceArtifact> CreateAsync(CreateWorkspaceArtifact input, CancellationToken cancellationToken)
    {
        Validate(input.Kind, input.Title, input.Content);
        var artifact = new WorkspaceArtifact($"artifact-{Guid.NewGuid():N}", input.Kind, input.Title.Trim(), 1, input.Content.Clone());
        await _gate.WaitAsync(cancellationToken);
        try { await SaveAsync(artifact, cancellationToken); return artifact; }
        finally { _gate.Release(); }
    }

    public async Task<WorkspaceArtifact> ReadAsync(string id, CancellationToken cancellationToken)
    {
        var path = PathFor(id);
        await _gate.WaitAsync(cancellationToken);
        try { return await ReadFileAsync(path, cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task<WorkspaceArtifact> UpdateAsync(string id, UpdateWorkspaceArtifact input, CancellationToken cancellationToken)
    {
        var path = PathFor(id);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = await ReadFileAsync(path, cancellationToken);
            if (current.Revision != input.ExpectedRevision) { throw new ArtifactConflictException(); }
            Validate(current.Kind, input.Title, input.Content);
            var updated = current with { Title = input.Title.Trim(), Revision = current.Revision + 1, Content = input.Content.Clone() };
            await SaveAsync(updated, cancellationToken);
            return updated;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<object>> ListAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!Directory.Exists(_directory)) { return []; }
            var result = new List<object>();
            foreach (var path in Directory.EnumerateFiles(_directory, "artifact-*.json").Order(StringComparer.Ordinal))
            {
                var artifact = await ReadFileAsync(path, cancellationToken);
                result.Add(new { artifact.Id, artifact.Kind, artifact.Title, artifact.Revision });
            }
            return result;
        }
        finally { _gate.Release(); }
    }

    private string PathFor(string id)
    {
        if (id is null || id.Length != 41 || !id.StartsWith("artifact-", StringComparison.Ordinal)
            || !Guid.TryParseExact(id[9..], "N", out _)) { throw new ArgumentException("Invalid artifact ID."); }
        return Path.Combine(_directory, id + ".json");
    }

    private static async Task<WorkspaceArtifact> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) { throw new KeyNotFoundException("Artifact not found."); }
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<WorkspaceArtifact>(stream, Json, cancellationToken)
            ?? throw new InvalidDataException("Saved artifact is unreadable.");
    }

    private async Task SaveAsync(WorkspaceArtifact artifact, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        var destination = PathFor(artifact.Id);
        var temporary = destination + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(artifact, Json), cancellationToken);
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }

    private static void Validate(string kind, string title, JsonElement content)
    {
        if (kind is not ("diagram" or "brain" or "image")) { throw new ArgumentException("Kind must be diagram, brain or image."); }
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200) { throw new ArgumentException("Title must contain 1–200 characters."); }
        if (content.ValueKind != JsonValueKind.Object || content.GetRawText().Length > 8_000_000)
        { throw new ArgumentException("Content must be a JSON object of at most 8 MB."); }
        if (kind == "diagram" && (!content.TryGetProperty("source", out var source) || source.ValueKind != JsonValueKind.String))
        { throw new ArgumentException("Diagram content needs Markdraw source text."); }
        if (kind == "brain")
        {
            if (!content.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array || nodes.GetArrayLength() > 100
                || !content.TryGetProperty("synapses", out var links) || links.ValueKind != JsonValueKind.Array || links.GetArrayLength() > 300)
            { throw new ArgumentException("Brain content needs up to 100 nodes and 300 synapses."); }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in nodes.EnumerateArray())
            {
                foreach (var key in new[] { "id", "type", "name", "label" })
                { if (!node.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) { throw new ArgumentException($"Brain nodes need {key}."); } }
                if (!ids.Add(node.GetProperty("id").GetString()!)) { throw new ArgumentException("Brain node IDs must be unique."); }
            }
            foreach (var link in links.EnumerateArray())
            {
                foreach (var key in new[] { "id", "sourceId", "targetId", "signalType", "kind" })
                { if (!link.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String) { throw new ArgumentException($"Brain synapses need {key}."); } }
                if (!ids.Contains(link.GetProperty("sourceId").GetString()!) || !ids.Contains(link.GetProperty("targetId").GetString()!))
                { throw new ArgumentException("Brain synapses must reference existing nodes."); }
            }
        }
    }
}
