using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DigitalBrain.Core.V2;

/// <summary>Application-owned V2 command/query boundary. It deliberately has no Orleans, transport, or provider dependency.</summary>
public sealed class V2ApplicationService : IV2QueryPort, IV2CommandPort
{
    private const string ReplaySafeInoCommandType = "ino.interact";
    private readonly CapabilityIsolationGate _gate = new();
    private readonly ConcurrentDictionary<string, V2OperationStatus> _operations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<IdempotencyScope, SubmissionReceipt> _idempotency = new();
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
        var owned = _operations.Values
            .Where(operation => _commands.TryGetValue(operation.OperationId, out var command) && SameOwner(context, command.Context))
            .OrderBy(operation => operation.OperationId, StringComparer.Ordinal)
            .ToArray();
        var offset = DecodeCursor(cursor);
        var take = Math.Clamp(limit, 1, 100);
        var items = owned.Skip(offset).Take(take).ToArray();
        return Task.FromResult(new V2Page<V2OperationStatus>(items, offset + items.Length < owned.Length ? EncodeCursor(offset + items.Length) : null, offset + items.Length < owned.Length));
    }

    public Task<V2OperationStatus?> GetOperationAsync(RequestContext context, string operationId, CancellationToken cancellationToken = default)
    {
        _gate.Demand(context, context.TenantId, context.WorkspaceId, "brain.read");
        if (_operations.TryGetValue(operationId, out var operation) && _commands.TryGetValue(operationId, out var command) &&
            SameOwner(context, command.Context))
            return Task.FromResult<V2OperationStatus?>(operation);
        return Task.FromResult<V2OperationStatus?>(null);
    }

    public Task<V2OperationStatus> SubmitAsync(RequestContext context, V2CommandEnvelope command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Context);
        var requiredCapability = command.Type.Contains("admin", StringComparison.OrdinalIgnoreCase)
            ? "brain.admin"
            : command.Type.Contains("approv", StringComparison.OrdinalIgnoreCase)
                ? "brain.approve"
                : "brain.act";
        _gate.Demand(context, context.TenantId, context.WorkspaceId, requiredCapability);
        cancellationToken.ThrowIfCancellationRequested();
        DemandAuthenticatedAuthority(context, command);
        if (string.IsNullOrWhiteSpace(command.Type) || command.Type.Length > 256 || command.Version <= 0 ||
            string.IsNullOrWhiteSpace(command.CommandId) || command.CommandId.Length > 1024 ||
            command.Payload.ValueKind == JsonValueKind.Undefined)
            throw new ArgumentException("The V2 command envelope is invalid.", nameof(command));
        var idempotency = context.IdempotencyKey ?? command.CommandId;
        if (string.IsNullOrWhiteSpace(idempotency) || idempotency.Length > 1024)
            throw new ArgumentException("The V2 idempotency key is invalid.", nameof(context));
        var key = new IdempotencyScope(context.TenantId, context.WorkspaceId, context.Principal, idempotency);
        var inputFingerprint = InputFingerprint(command.Payload);
        lock (_stateLock)
        {
            if (_idempotency.TryGetValue(key, out var receipt))
            {
                if (!_operations.TryGetValue(receipt.OperationId, out var prior) ||
                    !_commands.TryGetValue(receipt.OperationId, out var priorCommand) || !SameOwner(context, priorCommand.Context))
                    throw new InvalidOperationException("The V2 idempotency journal is internally inconsistent.");
                if (!receipt.Matches(command, inputFingerprint)) throw new V2IdempotencyConflictException();
                return Task.FromResult(prior);
            }
            var operationId = "v2-op-" + Guid.NewGuid().ToString("N");
            var operation = new V2OperationStatus(operationId, WorkflowState.ApplyQueued, null, DateTimeOffset.UtcNow);
            // The operation and command envelope are appended together before any in-memory
            // observer can see them. This is the durable handoff and idempotency linearization point.
            Persist(idempotency, command, operation);
            _operations[operationId] = operation;
            _commands[operationId] = command;
            _idempotency[key] = new SubmissionReceipt(operationId, command.Type, command.Version, inputFingerprint);
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
                item.Command is null || item.Command.Context is null || item.Command.Context.TenantId.Value != item.Tenant ||
                item.Command.Context.WorkspaceId.Value != item.Workspace ||
                string.IsNullOrWhiteSpace(item.Command.Context.Principal.Value) || item.Command.Context.Principal.Value.Length > 256 ||
                !Enum.IsDefined(item.Command.Context.Principal.Kind) || !Enum.IsDefined(item.Command.Context.Assurance) ||
                string.IsNullOrWhiteSpace(item.Command.Context.SessionId) || item.Command.Context.SessionId.Length > 256 ||
                item.Command.Context.Grants is null || string.IsNullOrWhiteSpace(item.Command.Type) || item.Command.Type.Length > 256 ||
                item.Command.Version <= 0 || string.IsNullOrWhiteSpace(item.Command.CommandId) || item.Command.CommandId.Length > 1024 ||
                item.Command.Payload.ValueKind == JsonValueKind.Undefined)
            {
                Quarantine(lineNumber, line);
                throw new InvalidDataException($"The V2 operation journal contains an invalid record at line {lineNumber}.");
            }
            var inputFingerprint = InputFingerprint(item.Command.Payload);
            var idempotencyScope = new IdempotencyScope(new(item.Tenant), new(item.Workspace), item.Command.Context.Principal, item.Idempotency);
            var receipt = new SubmissionReceipt(item.Operation.OperationId, item.Command.Type, item.Command.Version, inputFingerprint);
            if ((_idempotency.TryGetValue(idempotencyScope, out var mappedReceipt) && mappedReceipt != receipt) ||
                (_operationIdempotency.TryGetValue(item.Operation.OperationId, out var mappedIdempotency) &&
                 !string.Equals(mappedIdempotency, item.Idempotency, StringComparison.Ordinal)) ||
                (_commands.TryGetValue(item.Operation.OperationId, out var mappedCommand) &&
                 (!SameOwner(mappedCommand.Context, item.Command.Context) ||
                  !string.Equals(mappedCommand.Type, item.Command.Type, StringComparison.Ordinal) ||
                  mappedCommand.Version != item.Command.Version ||
                  !FixedTimeEquals(InputFingerprint(mappedCommand.Payload), inputFingerprint))))
            {
                Quarantine(lineNumber, line);
                throw new InvalidDataException($"The V2 operation journal contains an idempotency conflict at line {lineNumber}.");
            }
            _operations[item.Operation.OperationId] = item.Operation;
            _idempotency[idempotencyScope] = receipt;
            _operationIdempotency[item.Operation.OperationId] = item.Idempotency;
            _commands[item.Operation.OperationId] = item.Command;
        }
        RecoverInterruptedApplications();
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

    private void Persist(string idempotency, V2CommandEnvelope command, V2OperationStatus operation)
    {
        if (_appendLine is null) return;
        lock (_storageLock)
            _appendLine(JsonSerializer.Serialize(new PersistedOperation(
                idempotency, command.Context.TenantId.Value, command.Context.WorkspaceId.Value, operation, command)));
    }

    private sealed record PersistedOperation(string Idempotency, string Tenant, string Workspace, V2OperationStatus Operation, V2CommandEnvelope? Command = null);
    private readonly record struct IdempotencyScope(TenantId Tenant, WorkspaceId Workspace, PrincipalRef Principal, string Idempotency);
    private sealed record SubmissionReceipt(string OperationId, string CommandType, int CommandVersion, string InputFingerprint)
    {
        public bool Matches(V2CommandEnvelope command, string inputFingerprint) =>
            string.Equals(CommandType, command.Type, StringComparison.Ordinal) && CommandVersion == command.Version &&
            FixedTimeEquals(InputFingerprint, inputFingerprint);
    }

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
        if (_appendLine is null || !_commands.TryGetValue(operationId, out var command) ||
            !_operationIdempotency.TryGetValue(operationId, out var idempotency)) return;
        lock (_storageLock)
            _appendLine(JsonSerializer.Serialize(new PersistedOperation(
                idempotency, command.Context.TenantId.Value, command.Context.WorkspaceId.Value, status, command)));
    }

    private void RecoverInterruptedApplications()
    {
        foreach (var pair in _operations.Where(static pair => pair.Value.State == WorkflowState.Applying)
                     .OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var replaySafe = _commands.TryGetValue(pair.Key, out var command) &&
                             string.Equals(command.Type, ReplaySafeInoCommandType, StringComparison.Ordinal);
            var recovered = pair.Value with
            {
                State = replaySafe ? WorkflowState.ApplyQueued : WorkflowState.OutcomeUnknown,
                SafeReason = replaySafe ? null : "The previous attempt ended before its outcome was confirmed.",
                UpdatedAt = DateTimeOffset.UtcNow
            };
            PersistStatus(pair.Key, recovered);
            _operations[pair.Key] = recovered;
        }
    }

    private static void DemandAuthenticatedAuthority(RequestContext context, V2CommandEnvelope command)
    {
        if (!SameOwner(context, command.Context) || context.Assurance != command.Context.Assurance ||
            !string.Equals(context.SessionId, command.Context.SessionId, StringComparison.Ordinal) ||
            !context.Grants.SetEquals(command.Context.Grants))
            throw new UnauthorizedAccessException("V2 command authority must match the authenticated context.");
    }

    private static bool SameOwner(RequestContext left, RequestContext right) =>
        left.TenantId == right.TenantId && left.WorkspaceId == right.WorkspaceId && left.Principal == right.Principal;

    private static string InputFingerprint(JsonElement input)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonical(writer, input);
            writer.Flush();
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(value.GetRawText(), skipInputValidation: true);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new ArgumentException("The V2 command input contains an unsupported JSON value.", nameof(value));
        }
    }

    private static bool FixedTimeEquals(string first, string second) =>
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(first), Convert.FromHexString(second));

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
