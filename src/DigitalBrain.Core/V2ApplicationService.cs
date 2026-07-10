using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DigitalBrain.Core.V2;

/// <summary>Application-owned V2 command/query boundary. It deliberately has no Orleans, transport, or provider dependency.</summary>
public sealed class V2ApplicationService : IV2QueryPort, IV2CommandPort
{
    private readonly CapabilityIsolationGate _gate = new();
    private readonly ConcurrentDictionary<string, V2OperationStatus> _operations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (TenantId Tenant, WorkspaceId Workspace)> _operationOwners = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<IdempotencyScope, string> _idempotency = new();
    private readonly ConcurrentDictionary<string, string> _operationIdempotency = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, V2CommandEnvelope> _commands = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _claimed = new(StringComparer.Ordinal);
    private readonly byte[] _cursorKey;
    private readonly IReadOnlyList<V2Capability> _capabilities;
    private readonly string? _storagePath;
    private readonly object _storageLock = new();
    private readonly object _stateLock = new();
    private readonly Action<string>? _appendLine;

    public V2ApplicationService(
        byte[]? cursorKey = null,
        IEnumerable<V2Capability>? capabilities = null,
        string? storagePath = null,
        Action<string>? appendLine = null)
    {
        _cursorKey = cursorKey is { Length: >= 32 } ? cursorKey.ToArray() : SHA256.HashData(Encoding.UTF8.GetBytes("digitalbrain-v2-test-cursor-key"));
        _capabilities = (capabilities ?? [new("brain.read", 2, true, false), new("brain.act", 2, true, true), new("brain.approve", 2, true, true)]).ToArray();
        _storagePath = string.IsNullOrWhiteSpace(storagePath) ? null : Path.GetFullPath(storagePath);
        _appendLine = appendLine ?? (_storagePath is null
            ? null
            : line =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_storagePath)!);
                File.AppendAllText(_storagePath, line + Environment.NewLine);
            });
        Load();
    }

    public Task<V2Page<V2Capability>> GetCapabilitiesAsync(RequestContext context, string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        _gate.Demand(context, context.TenantId, context.WorkspaceId, "brain.read");
        var offset = DecodeCursor(cursor);
        var take = Math.Clamp(limit, 1, 100);
        var items = _capabilities.Skip(offset).Take(take).ToArray();
        return Task.FromResult(new V2Page<V2Capability>(items, offset + items.Length < _capabilities.Count ? EncodeCursor(offset + items.Length) : null, offset + items.Length < _capabilities.Count));
    }

    public Task<V2Page<V2OperationStatus>> GetOperationsAsync(RequestContext context, string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        _gate.Demand(context, context.TenantId, context.WorkspaceId, "brain.read");
        var owned = _operations.Values.Where(x => _operationOwners.TryGetValue(x.OperationId, out var owner) && owner.Tenant == context.TenantId && owner.Workspace == context.WorkspaceId).OrderBy(x => x.OperationId, StringComparer.Ordinal).ToArray();
        var offset = DecodeCursor(cursor);
        var take = Math.Clamp(limit, 1, 100);
        var items = owned.Skip(offset).Take(take).ToArray();
        return Task.FromResult(new V2Page<V2OperationStatus>(items, offset + items.Length < owned.Length ? EncodeCursor(offset + items.Length) : null, offset + items.Length < owned.Length));
    }

    public Task<V2OperationStatus?> GetOperationAsync(RequestContext context, string operationId, CancellationToken cancellationToken = default)
    {
        _gate.Demand(context, context.TenantId, context.WorkspaceId, "brain.read");
        if (_operations.TryGetValue(operationId, out var operation) && _operationOwners.TryGetValue(operationId, out var owner) && owner.Tenant == context.TenantId && owner.Workspace == context.WorkspaceId)
            return Task.FromResult<V2OperationStatus?>(operation);
        return Task.FromResult<V2OperationStatus?>(null);
    }

    public Task<V2OperationStatus> SubmitAsync(RequestContext context, V2CommandEnvelope command, CancellationToken cancellationToken = default)
    {
        var requiredCapability = command.Type.Contains("admin", StringComparison.OrdinalIgnoreCase)
            ? "brain.admin"
            : command.Type.Contains("approv", StringComparison.OrdinalIgnoreCase)
                ? "brain.approve"
                : "brain.act";
        _gate.Demand(context, context.TenantId, context.WorkspaceId, requiredCapability);
        cancellationToken.ThrowIfCancellationRequested();
        var idempotency = context.IdempotencyKey ?? command.CommandId;
        var key = new IdempotencyScope(context.TenantId, context.WorkspaceId, idempotency);
        lock (_stateLock)
        {
            if (_idempotency.TryGetValue(key, out var existing))
            {
                if (_operations.TryGetValue(existing, out var prior) && _operationOwners.TryGetValue(existing, out var owner) &&
                    owner.Tenant == context.TenantId && owner.Workspace == context.WorkspaceId)
                    return Task.FromResult(prior);
                throw new InvalidOperationException("The V2 idempotency journal is internally inconsistent.");
            }
            var operationId = "v2-op-" + Guid.NewGuid().ToString("N");
            var operation = new V2OperationStatus(operationId, WorkflowState.ApplyQueued, null, DateTimeOffset.UtcNow);
            // The operation and command envelope are appended together before any in-memory
            // observer can see them. This is the durable handoff and idempotency linearization point.
            Persist(idempotency, context.TenantId, context.WorkspaceId, command, operation);
            _operations[operationId] = operation;
            _operationOwners[operationId] = (context.TenantId, context.WorkspaceId);
            _commands[operationId] = command;
            _idempotency[key] = operationId;
            _operationIdempotency[operationId] = idempotency;
            return Task.FromResult(operation);
        }
    }

    private void Load()
    {
        if (string.IsNullOrWhiteSpace(_storagePath) || !File.Exists(_storagePath)) return;
        var lineNumber = 0;
        foreach (var line in File.ReadLines(_storagePath))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            PersistedOperation? item;
            try
            {
                item = JsonSerializer.Deserialize<PersistedOperation>(line);
            }
            catch (JsonException)
            {
                // A torn/poison record must not prevent recovery of earlier operations.
                // Quarantine only a digest and location: the raw record may contain a
                // caller payload and must never be copied to another durable surface.
                Quarantine(lineNumber, line);
                throw new InvalidDataException($"The V2 operation journal contains invalid JSON at line {lineNumber}.");
            }
            if (item is null || string.IsNullOrWhiteSpace(item.Idempotency) || item.Idempotency.Length > 1024 ||
                string.IsNullOrWhiteSpace(item.Tenant) || item.Tenant.Length > 256 ||
                string.IsNullOrWhiteSpace(item.Workspace) || item.Workspace.Length > 256 ||
                item.Operation is null || string.IsNullOrWhiteSpace(item.Operation.OperationId) || item.Operation.OperationId.Length > 256 ||
                item.Command is null || item.Command.Context.TenantId.Value != item.Tenant ||
                item.Command.Context.WorkspaceId.Value != item.Workspace)
            {
                Quarantine(lineNumber, line);
                throw new InvalidDataException($"The V2 operation journal contains an invalid record at line {lineNumber}.");
            }
            var idempotencyScope = new IdempotencyScope(new(item.Tenant), new(item.Workspace), item.Idempotency);
            if (_idempotency.TryGetValue(idempotencyScope, out var mappedOperation) &&
                !string.Equals(mappedOperation, item.Operation.OperationId, StringComparison.Ordinal))
            {
                Quarantine(lineNumber, line);
                throw new InvalidDataException($"The V2 operation journal contains an idempotency conflict at line {lineNumber}.");
            }
            _operations[item.Operation.OperationId] = item.Operation;
            _operationOwners[item.Operation.OperationId] = (new(item.Tenant), new(item.Workspace));
            _idempotency[idempotencyScope] = item.Operation.OperationId;
            _operationIdempotency[item.Operation.OperationId] = item.Idempotency;
            if (item.Command is not null) _commands[item.Operation.OperationId] = item.Command;
        }
    }

    private void Quarantine(int lineNumber, string raw)
    {
        if (string.IsNullOrWhiteSpace(_storagePath)) return;
        try
        {
            var quarantinePath = _storagePath + ".quarantine";
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
            lock (_storageLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(quarantinePath))!);
                var entry = JsonSerializer.Serialize(new { line = lineNumber, sha256 = digest, reason = "invalid-json" });
                File.AppendAllText(quarantinePath, entry + Environment.NewLine);
            }
        }
        catch
        {
            // Recovery remains fail-closed for the poisoned record while preserving
            // availability of valid records; telemetry can report quarantine failure.
        }
    }

    private void Persist(string idempotency, TenantId tenant, WorkspaceId workspace, V2CommandEnvelope command, V2OperationStatus operation)
    {
        if (_appendLine is null) return;
        lock (_storageLock)
            _appendLine(JsonSerializer.Serialize(new PersistedOperation(idempotency, tenant.Value, workspace.Value, operation, command)));
    }

    private sealed record PersistedOperation(string Idempotency, string Tenant, string Workspace, V2OperationStatus Operation, V2CommandEnvelope? Command = null);
    private readonly record struct IdempotencyScope(TenantId Tenant, WorkspaceId Workspace, string Idempotency);

    /// <summary>Claims one durable command for execution. Claiming is idempotent and never trusts transport state.</summary>
    public bool TryClaimPending(string operationId, out V2CommandEnvelope? command)
    {
        lock (_stateLock)
        {
            command = null;
            if (!_operations.TryGetValue(operationId, out var current) || current.State != WorkflowState.ApplyQueued || !_commands.TryGetValue(operationId, out command)) return false;
            if (!_claimed.TryAdd(operationId, 0)) return false;
            var applying = current with { State = WorkflowState.Applying, UpdatedAt = DateTimeOffset.UtcNow };
            try { PersistStatus(operationId, applying); }
            catch
            {
                _claimed.TryRemove(operationId, out _);
                throw;
            }
            _operations[operationId] = applying;
            return true;
        }
    }

    public IReadOnlyList<string> GetPendingOperationIds()
        => _operations.Values.Where(x => x.State == WorkflowState.ApplyQueued)
            .Select(x => x.OperationId).OrderBy(x => x, StringComparer.Ordinal).ToArray();

    /// <summary>Records a worker outcome durably; ambiguous outcomes must be reported explicitly.</summary>
    public bool RecordOutcome(string operationId, WorkflowState state, string? safeReason = null)
    {
        if (state is not (WorkflowState.Succeeded or WorkflowState.Failed or WorkflowState.OutcomeUnknown or WorkflowState.Compensated or WorkflowState.ManualIntervention)) throw new ArgumentOutOfRangeException(nameof(state));
        lock (_stateLock)
        {
            if (!_operations.TryGetValue(operationId, out var current) || current.State != WorkflowState.Applying) return false;
            var updated = current with { State = state, SafeReason = safeReason, UpdatedAt = DateTimeOffset.UtcNow };
            PersistStatus(operationId, updated);
            _operations[operationId] = updated;
            return true;
        }
    }

    private void PersistStatus(string operationId, V2OperationStatus status)
    {
        if (_appendLine is null || !_operationOwners.TryGetValue(operationId, out var owner) ||
            !_commands.TryGetValue(operationId, out var command) || !_operationIdempotency.TryGetValue(operationId, out var idempotency)) return;
        lock (_storageLock)
            _appendLine(JsonSerializer.Serialize(new PersistedOperation(idempotency, owner.Tenant.Value, owner.Workspace.Value, status, command)));
    }

    private string EncodeCursor(int offset)
    {
        var body = offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var signature = Convert.ToHexString(HMACSHA256.HashData(_cursorKey, Encoding.UTF8.GetBytes(body)));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(body + "." + signature)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private int DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return 0;
        try
        {
            var value = cursor.Replace('-', '+').Replace('_', '/');
            value += (value.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value)).Split('.');
            if (decoded.Length != 2 || !int.TryParse(decoded[0], out var offset) || offset < 0) throw new FormatException();
            var expected = Convert.ToHexString(HMACSHA256.HashData(_cursorKey, Encoding.UTF8.GetBytes(decoded[0])));
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(decoded[1]))) throw new FormatException();
            return offset;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            throw new ArgumentException("Invalid V2 cursor.", nameof(cursor));
        }
    }
}
