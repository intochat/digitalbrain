using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using DigitalBrain.Contracts;
using DigitalBrain.Core.Enforcement;
using Orleans;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.Workspace;

// Immutable, content-addressed records are written before publishing the root pointer.
// A failed child write leaves the previous revision readable. Retries reuse the same parts.
internal static class ShellSnapshotParts
{
    internal static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal static async Task<string> Write(JsonNode snapshot, Func<string, string, Task> write)
    {
        ValidateFieldNames(snapshot);
        var written = new Dictionary<string, string>(StringComparer.Ordinal);
        Task Store(string id, string value)
        {
            if (Encoding.UTF8.GetByteCount(value) > 256 * 1024) { throw new ArgumentException("Workspace record exceeds its size limit."); }
            written.TryAdd(id, value);
            return Task.CompletedTask;
        }
        async Task<string> Index(string kind, List<string> children)
        {
            while (children.Count > 64)
            {
                var next = new List<string>();
                foreach (var group in children.Chunk(64))
                {
                    var json = new JsonObject { [kind] = new JsonArray(group.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()) }.ToJsonString();
                    var id = Digest(json);
                    await Store(id, json);
                    next.Add(id);
                }
                children = next;
            }
            var text = new JsonObject { [kind] = new JsonArray(children.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()) }.ToJsonString();
            var root = Digest(text);
            await Store(root, text);
            return root;
        }
        async Task<JsonNode?> Split(JsonNode? node, bool root = false)
        {
            // Keep ordinary records and transcript pages together. Splitting every
            // nested object creates thousands of Blob round trips on first import.
            if (!root && node is JsonObject or JsonArray)
            {
                var packed = new JsonObject { ["value"] = node.DeepClone() }.ToJsonString();
                if (Encoding.UTF8.GetByteCount(packed) <= 64 * 1024)
                {
                    var id = Digest(packed);
                    await Store(id, packed);
                    return JsonValue.Create(id);
                }
            }
            if (node is JsonObject obj)
            {
                var fragments = new List<string>();
                foreach (var group in obj.OrderBy(p => p.Key, StringComparer.Ordinal).Chunk(64))
                {
                    var fields = new JsonObject();
                    foreach (var property in group)
                    {
                        if (property.Key.Length > 512) { throw new ArgumentException("Workspace field name is too long."); }
                        fields[property.Key] = await Split(property.Value);
                    }
                    var value = new JsonObject { ["object"] = fields }.ToJsonString();
                    var key = Digest(value);
                    await Store(key, value);
                    fragments.Add(key);
                }
                return JsonValue.Create(await Index("objectPages", fragments));
            }
            if (node is JsonArray array)
            {
                // Bounded pages keep long transcripts and artifact collections out of parent state.
                var pages = new JsonArray();
                foreach (var page in array.Chunk(32))
                {
                    var packed = new JsonObject { ["value"] = new JsonArray(page.Select(item => item?.DeepClone()).ToArray()) }.ToJsonString();
                    if (Encoding.UTF8.GetByteCount(packed) <= 64 * 1024)
                    {
                        var id = Digest(packed);
                        await Store(id, packed);
                        pages.Add(id);
                        continue;
                    }
                    var items = new JsonArray();
                    foreach (var item in page) { items.Add(await Split(item)); }
                    var value = new JsonObject { ["page"] = items }.ToJsonString();
                    var key = Digest(value);
                    await Store(key, value);
                    pages.Add(key);
                }
                return JsonValue.Create(await Index("array", pages.Select(page => page!.GetValue<string>()).ToList()));
            }
            var scalar = new JsonObject { ["value"] = node?.DeepClone() }.ToJsonString();
            if (scalar.Length <= 1024) { return JsonNode.Parse(scalar); }
            // Large artifact text is chunked as well; JSON strings can contain embedded image bytes.
            if (scalar.Length > 32 * 1024)
            {
                var chunks = new JsonArray();
                for (var offset = 0; offset < scalar.Length; offset += 32 * 1024)
                {
                    var chunk = scalar.Substring(offset, Math.Min(32 * 1024, scalar.Length - offset));
                    var part = new JsonObject { ["chunk"] = chunk }.ToJsonString();
                    var id = Digest(part);
                    await Store(id, part);
                    chunks.Add(id);
                }
                scalar = new JsonObject { ["chunks"] = chunks }.ToJsonString();
            }
            var scalarKey = Digest(scalar);
            await Store(scalarKey, scalar);
            return JsonValue.Create(scalarKey);
        }
        var root = (await Split(snapshot, root: true))!.GetValue<string>();
        // No root is published until all immutable children have been acknowledged.
        // Task.WhenAll stays on the Orleans scheduler; cap outstanding storage calls.
        foreach (var batch in written.Chunk(16))
        { await Task.WhenAll(batch.Select(part => write(part.Key, part.Value))); }
        return root;
    }

    private static void ValidateFieldNames(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var field in obj)
            {
                if (field.Key.Length > 512) { throw new ArgumentException("Workspace field name is too long."); }
                ValidateFieldNames(field.Value);
            }
        }
        else if (node is JsonArray array) { foreach (var item in array) { ValidateFieldNames(item); } }
    }

    internal static async Task<JsonNode> Read(string root, Func<string, Task<string>> read)
    {
        using var slots = new SemaphoreSlim(16);
        var fetched = new System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<string>>>();
        async Task<string> Fetch(string id)
        {
            await slots.WaitAsync();
            try
            {
                var text = await read(id);
                if (Digest(text) != id) { throw new InvalidDataException("Workspace record checksum mismatch."); }
                return text;
            }
            finally { slots.Release(); }
        }
        Task<JsonNode?> Resolve(JsonNode? link) => link is JsonObject inline
            ? Task.FromResult(inline["value"]?.DeepClone()) : Join(link!.GetValue<string>());
        async Task<JsonNode?> Join(string id)
        {
            var text = await fetched.GetOrAdd(id, key => new Lazy<Task<string>>(() => Fetch(key))).Value;
            var part = JsonNode.Parse(text)!.AsObject();
            if (part["objectPages"] is JsonArray fragments)
            {
                var result = new JsonObject();
                foreach (var fragment in await Task.WhenAll(fragments.Select(fragment => Join(fragment!.GetValue<string>()))))
                { foreach (var field in fragment!.AsObject()) { result.Add(field.Key, field.Value?.DeepClone()); } }
                return result;
            }
            if (part["object"] is JsonObject fields)
            {
                var result = new JsonObject();
                var values = await Task.WhenAll(fields.Select(field => Resolve(field.Value)));
                var index = 0;
                foreach (var field in fields) { result[field.Key] = values[index++]; }
                return result;
            }
            if (part["array"] is JsonArray pages)
            {
                var result = new JsonArray();
                foreach (var page in await Task.WhenAll(pages.Select(page => Join(page!.GetValue<string>()))))
                { foreach (var item in page!.AsArray()) { result.Add(item?.DeepClone()); } }
                return result;
            }
            if (part["page"] is JsonArray items)
            {
                var result = new JsonArray();
                foreach (var item in await Task.WhenAll(items.Select(Resolve))) { result.Add(item); }
                return result;
            }
            if (part["chunks"] is JsonArray chunks)
            {
                var result = new StringBuilder();
                foreach (var chunk in await Task.WhenAll(chunks.Select(chunk => Join(chunk!.GetValue<string>()))))
                { result.Append(chunk!.GetValue<string>()); }
                return JsonNode.Parse(result.ToString())!["value"]?.DeepClone();
            }
            return (part.ContainsKey("chunk") ? part["chunk"] : part["value"])?.DeepClone();
        }
        return await Join(root) ?? throw new InvalidDataException("Missing workspace snapshot.");
    }

    internal static void Validate(JsonNode snapshot)
    {
        if (snapshot["version"]?.GetValue<int>() != 1 || snapshot["projects"] is not JsonArray projects)
        { throw new ArgumentException("Unsupported workspace snapshot."); }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in projects)
        {
            var id = project?["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(id) || id.Length > 200 || !ids.Add(id))
            { throw new ArgumentException("Project IDs must be unique and nonempty."); }
            ValidateRecords(project!, "conversations", true);
            ValidateRecords(project!, "artifacts", false);
        }
    }

    private static void ValidateRecords(JsonNode project, string property, bool conversations)
    {
        if (project[property] is null) { return; }
        if (project[property] is not JsonArray records) { throw new ArgumentException($"Invalid {property} collection."); }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            if (record is not JsonObject obj || obj["id"] is not JsonValue value || !value.TryGetValue<string>(out var id)
                || string.IsNullOrWhiteSpace(id) || !ids.Add(id))
            { throw new ArgumentException($"Every {property} record needs a unique ID."); }
            if (conversations && obj["messages"] is { } messages && (messages is not JsonArray array || array.Any(message => message is not JsonObject)))
            { throw new ArgumentException("Invalid conversation messages; the source has been retained."); }
        }
    }
}

[GenerateSerializer]
internal sealed class ShellPartState { [Id(0)] public string? Json { get; set; } }
internal interface IShellPart : IGrainWithStringKey
{
    Task Put(string json);
    Task<string> Read();
}
[GrainType("intochat.shell-part")]
internal sealed class ShellPartNeuron([PersistentState("part", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ShellPartState> state) : Grain, IShellPart
{
    public async Task Put(string json)
    {
        if (state.State.Json == json) { return; }
        if (state.State.Json is not null) { throw new InvalidOperationException("Immutable workspace record cannot be replaced."); }
        state.State = new() { Json = json };
        try { await state.WriteStateAsync(); } catch { state.State = new(); throw; }
    }
    public Task<string> Read() => Task.FromResult(state.State.Json ?? throw new InvalidDataException("Workspace record is missing."));
}

[GenerateSerializer]
internal sealed class ShellHeadState
{
    [Id(0)] public long Revision { get; set; }
    [Id(1)] public string? Root { get; set; }
    // Id(2) retired; never reuse
    [Id(3)] public string? LastOperation { get; set; }
    [Id(4)] public string? LastDigest { get; set; }
}
[GenerateSerializer]
internal sealed record ShellRead([property: Id(0)] long Revision, [property: Id(1)] string? Json);
internal interface IShellState : IGrainWithStringKey
{
    Task<ShellRead> Read();
    Task<ShellRead> Save(long expectedRevision, string operationId, string json);
}
[GrainType("intochat.shell-state")]
internal sealed class ShellStateNeuron([PersistentState("head", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ShellHeadState> state) : Grain, IShellState
{
    private IShellPart Part(string hash) => GrainFactory.GetGrain<IShellPart>(this.GetPrimaryKeyString() + "/" + hash);
    public async Task<ShellRead> Read() => new(state.State.Revision, state.State.Root is { } root
        ? (await ShellSnapshotParts.Read(root, hash => Part(hash).Read())).ToJsonString() : null);

    public async Task<ShellRead> Save(long expectedRevision, string operationId, string json)
    {
        if (!Guid.TryParse(operationId, out _)) { throw new ArgumentException("Use a unique operation ID."); }
        var digest = ShellSnapshotParts.Digest(json);
        if (state.State.LastOperation == operationId)
        {
            if (state.State.LastDigest != digest) { throw new InvalidOperationException("Operation ID was reused with different data."); }
            return await Read();
        }
        if (state.State.Revision != expectedRevision) { throw new InvalidOperationException("Workspace changed on another client. Reload before saving."); }
        var snapshot = JsonNode.Parse(json) ?? throw new ArgumentException("Missing snapshot.");
        ShellSnapshotParts.Validate(snapshot);
        var root = await ShellSnapshotParts.Write(snapshot, (hash, value) => Part(hash).Put(value));
        var old = state.State;
        state.State = new()
        {
            Root = root, Revision = old.Revision + 1, LastOperation = operationId, LastDigest = digest
        };
        try { await state.WriteStateAsync(); } catch { state.State = old; throw; }
        return await Read();
    }
}

internal static class ShellPersistenceEndpoints
{
    internal static string Scope(string account, string principal) => "shell-" + ShellSnapshotParts.Digest(account + "\0" + principal);
    public static void MapShellPersistence(this IEndpointRouteBuilder routes)
    {
        static IShellState State(IGrainFactory grains)
        {
            var caller = CallerContextStamper.Require();
            return grains.GetGrain<IShellState>(Scope(caller.AccountId, caller.PrincipalId));
        }
        static IResult View(ShellRead value) => Results.Ok(new { value.Revision, Snapshot = value.Json is null ? null : JsonNode.Parse(value.Json) });
        routes.MapGet("/shell/state", async (IGrainFactory grains) => View(await State(grains).Read()));
        static async Task<IResult> Save(ShellWrite input, IGrainFactory grains)
        {
            try
            {
                var json = input.Snapshot.ToJsonString();
                if (Encoding.UTF8.GetByteCount(json) > 16 * 1024 * 1024) { return Results.StatusCode(413); }
                return View(await State(grains).Save(input.ExpectedRevision, input.OperationId, json));
            }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
            catch (InvalidOperationException e) { return Results.Conflict(new { error = e.Message }); }
        }
        routes.MapPut("/shell/state", (ShellWrite input, IGrainFactory grains) => Save(input, grains));
    }
    internal sealed record ShellWrite(long ExpectedRevision, string OperationId, JsonNode Snapshot);
}
