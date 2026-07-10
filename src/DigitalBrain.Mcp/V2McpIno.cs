using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Core.V2;
using V2RequestContext = DigitalBrain.Core.V2.RequestContext;

namespace DigitalBrain.Mcp;

public sealed record V2InoEffectRecord(string EffectId, string CommandId, TenantId TenantId, WorkspaceId WorkspaceId, PrincipalRef Principal, string CorrelationId, string State, string? SafeResult, DateTimeOffset UpdatedAt);

/// <summary>Durable V2-only INO effect journal; no legacy neuron or gateway identifier is retained.</summary>
public sealed class V2InoEffectStore
{
    private readonly ConcurrentDictionary<string, V2InoEffectRecord> _effects = new(StringComparer.Ordinal);
    private readonly string? _path;
    private readonly object _gate = new();
    public V2InoEffectStore(string? path = null)
    {
        _path = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        if (_path is null || !File.Exists(_path)) return;
        foreach (var line in File.ReadLines(_path).Where(static value => !string.IsNullOrWhiteSpace(value)))
            try { var effect = JsonSerializer.Deserialize<V2InoEffectRecord>(line); if (effect is not null && effect.EffectId.Length <= 256 && effect.CommandId.Length <= 256) _effects[effect.EffectId] = effect; } catch (JsonException) { }
    }
    public V2InoEffectRecord Begin(V2RequestContext context, string commandId)
    {
        var id = "v2-ino-effect-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{context.TenantId.Value}\n{context.WorkspaceId.Value}\n{V2PrincipalScope.Id(context.Principal)}\n{commandId}"))).ToLowerInvariant()[..32];
        return _effects.GetOrAdd(id, _ => Write(new(id, commandId, context.TenantId, context.WorkspaceId, context.Principal, context.CorrelationId, "Applying", null, DateTimeOffset.UtcNow)));
    }
    public V2InoEffectRecord Complete(V2InoEffectRecord effect, string safeResult) => Write(effect with { State = "Succeeded", SafeResult = V2Redaction.SafeSummary(safeResult), UpdatedAt = DateTimeOffset.UtcNow });
    public IReadOnlyList<V2InoEffectRecord> Read(V2RequestContext context) => _effects.Values.Where(effect => effect.TenantId == context.TenantId && effect.WorkspaceId == context.WorkspaceId && effect.Principal == context.Principal).OrderByDescending(effect => effect.UpdatedAt).ToArray();
    private V2InoEffectRecord Write(V2InoEffectRecord record) { lock (_gate) { if (_path is not null) { Directory.CreateDirectory(Path.GetDirectoryName(_path)!); File.AppendAllText(_path, JsonSerializer.Serialize(record) + Environment.NewLine); } _effects[record.EffectId] = record; return record; } }
}

/// <summary>Session-derived V2 INO command handler with a workspace feed projection.</summary>
public sealed class V2McpInoCommandHandler(V2InoEffectStore effects, V2WorkspaceSurfaceProducer surfaces) : IV2CommandHandler
{
    public const string CommandType = "ino.interact";
    public bool CanHandle(string commandType) => string.Equals(commandType, CommandType, StringComparison.Ordinal);
    public Task<V2CommandExecutionResult> ExecuteAsync(V2CommandEnvelope command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetPrompt(command.Payload, out _)) return Task.FromResult(new V2CommandExecutionResult(WorkflowState.Failed, "ino-request-invalid"));
        var effect = effects.Begin(command.Context, command.CommandId);
        if (effect.State == "Succeeded") return Task.FromResult(V2CommandExecutionResult.Success());
        const string result = "INO completed the authenticated workspace request.";
        effects.Complete(effect, result);
        surfaces.PublishInoResult(command.Context, command.CommandId, result);
        return Task.FromResult(V2CommandExecutionResult.Success());
    }
    public static bool TryGetPrompt(JsonElement payload, out string prompt)
    {
        prompt = string.Empty;
        if (payload.ValueKind != JsonValueKind.Object || payload.EnumerateObject().Count() != 1 || !payload.TryGetProperty("prompt", out var value) || value.ValueKind != JsonValueKind.String) return false;
        prompt = value.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(prompt) && prompt.Length <= 4096;
    }
}
