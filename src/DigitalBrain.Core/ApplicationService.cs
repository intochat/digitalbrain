using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DigitalBrain.Core.Runtime;

/// <summary>Application-owned command/query boundary. It deliberately has no Orleans, transport, or provider dependency.</summary>
public sealed class ApplicationService : IQueryPort, ICommandPort
{
    private const string ReplaySafeInoCommandType = "ino.interact";
    private readonly CapabilityIsolationGate _gate = new();
    private readonly ConcurrentDictionary<string, OperationStatus> _operations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<IdempotencyScope, SubmissionReceipt> _idempotency = new();
    private readonly ConcurrentDictionary<string, string> _operationIdempotency = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CommandEnvelope> _commands = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _claimed = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ExternalAuthorizationContinuation> _externalAuthorizations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ExternalAuthorizationResolution> _externalAuthorizationResolutions = new(StringComparer.Ordinal);
    private readonly byte[] _cursorKey;
    private readonly IReadOnlyList<Capability> _capabilities;
    private readonly object _stateLock = new();
    private readonly AuthenticatedJsonLinesJournal? _journal;

    public ApplicationService(
        byte[]? cursorKey = null,
        IEnumerable<Capability>? capabilities = null,
        string? storagePath = null,
        byte[]? journalIntegrityKey = null)
        : this(cursorKey, capabilities, storagePath, journalIntegrityKey, null)
    {
    }

    internal ApplicationService(
        AuthenticatedJournalFaultInjection journalFaultInjection,
        byte[]? cursorKey = null,
        IEnumerable<Capability>? capabilities = null,
        string? storagePath = null,
        byte[]? journalIntegrityKey = null)
        : this(cursorKey, capabilities, storagePath, journalIntegrityKey, journalFaultInjection)
    {
    }

    private ApplicationService(
        byte[]? cursorKey,
        IEnumerable<Capability>? capabilities,
        string? storagePath,
        byte[]? journalIntegrityKey,
        AuthenticatedJournalFaultInjection? journalFaultInjection)
    {
        _cursorKey = cursorKey is { Length: >= 32 } ? cursorKey.ToArray() : SHA256.HashData(Encoding.UTF8.GetBytes("digitalbrain-v2-test-cursor-key"));
        _capabilities = (capabilities ?? [new("brain.read", 2, true, false), new("brain.interact", 2, true, false), new("brain.act", 2, true, true), new("brain.approve", 2, true, true)]).ToArray();
        if (!string.IsNullOrWhiteSpace(storagePath) && journalIntegrityKey is not { Length: >= 32 })
            throw new ArgumentException("A stable journal integrity key of at least 256 bits is required for durable operations.", nameof(journalIntegrityKey));
        if (string.IsNullOrWhiteSpace(storagePath) && journalFaultInjection is not null)
            throw new ArgumentException("Journal fault injection requires a real durable path.", nameof(storagePath));
        if (!string.IsNullOrWhiteSpace(storagePath))
            _journal = new AuthenticatedJsonLinesJournal("digitalbrain.v2.operations", journalIntegrityKey!, storagePath, journalFaultInjection);
        Load();
    }

    public Task<Page<Capability>> GetCapabilitiesAsync(RequestContext context, string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        DemandCapability(context, "brain.read");
        var offset = DecodeCursor(cursor);
        var take = Math.Clamp(limit, 1, 100);
        var items = _capabilities.Skip(offset).Take(take).ToArray();
        return Task.FromResult(new Page<Capability>(items, offset + items.Length < _capabilities.Count ? EncodeCursor(offset + items.Length) : null, offset + items.Length < _capabilities.Count));
    }

    public Task<Page<OperationStatus>> GetOperationsAsync(RequestContext context, string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        DemandCapability(context, "brain.read");
        var owned = _operations.Values
            .Where(operation => _commands.TryGetValue(operation.OperationId, out var command) && SameOwner(context, command.Context))
            .OrderBy(operation => operation.OperationId, StringComparer.Ordinal)
            .ToArray();
        var offset = DecodeCursor(cursor);
        var take = Math.Clamp(limit, 1, 100);
        var items = owned.Skip(offset).Take(take).ToArray();
        return Task.FromResult(new Page<OperationStatus>(items, offset + items.Length < owned.Length ? EncodeCursor(offset + items.Length) : null, offset + items.Length < owned.Length));
    }

    public Task<OperationStatus?> GetOperationAsync(RequestContext context, string operationId, CancellationToken cancellationToken = default)
    {
        DemandCapability(context, "brain.read");
        if (_operations.TryGetValue(operationId, out var operation) && _commands.TryGetValue(operationId, out var command) &&
            SameOwner(context, command.Context))
            return Task.FromResult<OperationStatus?>(operation);
        return Task.FromResult<OperationStatus?>(null);
    }

    public Task<OperationStatus> SubmitAsync(RequestContext context, CommandEnvelope command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Context);
        var requiredCapability = string.Equals(command.Type, ReplaySafeInoCommandType, StringComparison.Ordinal) &&
                                 context.Grants.Contains("brain.interact")
            ? "brain.interact"
            : command.Type.Contains("admin", StringComparison.OrdinalIgnoreCase)
            ? "brain.admin"
            : command.Type.Contains("approv", StringComparison.OrdinalIgnoreCase)
                ? "brain.approve"
                : "brain.act";
        DemandCapability(context, requiredCapability);
        cancellationToken.ThrowIfCancellationRequested();
        DemandAuthenticatedAuthority(context, command);
        if (string.IsNullOrWhiteSpace(command.Type) || command.Type.Length > 256 || command.Version <= 0 ||
            string.IsNullOrWhiteSpace(command.CommandId) || command.CommandId.Length > 1024 ||
            command.Payload.ValueKind == JsonValueKind.Undefined)
            throw new ArgumentException("The command envelope is invalid.", nameof(command));
        var idempotency = context.IdempotencyKey ?? command.CommandId;
        if (string.IsNullOrWhiteSpace(idempotency) || idempotency.Length > 1024)
            throw new ArgumentException("The idempotency key is invalid.", nameof(context));
        var key = new IdempotencyScope(context.TenantId, context.WorkspaceId, context.Principal, idempotency);
        var inputFingerprint = InputFingerprint(command.Payload);
        lock (_stateLock)
        {
            if (_idempotency.TryGetValue(key, out var receipt))
            {
                if (!_operations.TryGetValue(receipt.OperationId, out var prior) ||
                    !_commands.TryGetValue(receipt.OperationId, out var priorCommand) || !SameOwner(context, priorCommand.Context))
                    throw new InvalidOperationException("The idempotency journal is internally inconsistent.");
                if (!receipt.Matches(command, inputFingerprint)) throw new IdempotencyConflictException();
                return Task.FromResult(prior);
            }
            var operationId = "v2-op-" + Guid.NewGuid().ToString("N");
            var operation = new OperationStatus(operationId, WorkflowState.ApplyQueued, null, DateTimeOffset.UtcNow);
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
        if (_journal is null) return;
        foreach (var record in _journal.Read())
        {
            PersistedOperation? item;
            try
            {
                item = JsonSerializer.Deserialize<PersistedOperation>(record.Payload);
            }
            catch (JsonException)
            {
                throw _journal.Invalid(record, "invalid-json", $"The operation journal contains invalid JSON at line {record.LineNumber}.");
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
                item.Command.Payload.ValueKind == JsonValueKind.Undefined ||
                item.Operation.State == WorkflowState.AwaitingExternalAuthorization && item.Authorization is null ||
                item.Authorization is not null &&
                (!ValidAuthorization(item.Authorization) ||
                 item.Operation.State is not (WorkflowState.AwaitingExternalAuthorization or WorkflowState.ApplyQueued or WorkflowState.Applying)) ||
                item.AuthorizationResolution is not null &&
                (item.Authorization is null || !ValidAuthorizationResolution(item.AuthorizationResolution) ||
                 item.Operation.State is not (WorkflowState.ApplyQueued or WorkflowState.Applying)))
            {
                throw _journal.Invalid(record, "invalid-record", $"The operation journal contains an invalid record at line {record.LineNumber}.");
            }
            if (!record.IsLegacy && !string.Equals(record.Kind, OperationJournalKind(item.Operation), StringComparison.Ordinal))
                throw _journal.Invalid(record, "kind-mismatch", $"The operation journal contains a mismatched record kind at line {record.LineNumber}.");
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
                throw _journal.Invalid(record, "idempotency-conflict", $"The operation journal contains an idempotency conflict at line {record.LineNumber}.");
            }
            _operations[item.Operation.OperationId] = item.Operation;
            _idempotency[idempotencyScope] = receipt;
            _operationIdempotency[item.Operation.OperationId] = item.Idempotency;
            _commands[item.Operation.OperationId] = item.Command;
            if (item.Authorization is null)
                _externalAuthorizations.TryRemove(item.Operation.OperationId, out _);
            else
                _externalAuthorizations[item.Operation.OperationId] = Clone(item.Authorization);
            if (item.AuthorizationResolution is null)
                _externalAuthorizationResolutions.TryRemove(item.Operation.OperationId, out _);
            else
                _externalAuthorizationResolutions[item.Operation.OperationId] = item.AuthorizationResolution;
        }
        _journal.SealLegacy();
        RecoverInterruptedApplications();
    }

    private void DemandCapability(RequestContext context, string requiredCapability)
    {
        if (!_capabilities.Any(capability =>
                capability.Enabled && string.Equals(capability.Id, requiredCapability, StringComparison.Ordinal)))
            throw new UnauthorizedAccessException($"Runtime capability '{requiredCapability}' is disabled.");
        _gate.Demand(context, context.TenantId, context.WorkspaceId, requiredCapability);
    }

    private void Persist(string idempotency, CommandEnvelope command, OperationStatus operation)
    {
        if (_journal is null) return;
        _journal.Append(OperationJournalKind(operation), JsonSerializer.Serialize(new PersistedOperation(
            idempotency, command.Context.TenantId.Value, command.Context.WorkspaceId.Value, operation, command)));
    }

    private sealed record PersistedOperation(
        string Idempotency,
        string Tenant,
        string Workspace,
        OperationStatus Operation,
        CommandEnvelope? Command = null,
        ExternalAuthorizationContinuation? Authorization = null,
        ExternalAuthorizationResolution? AuthorizationResolution = null);
    private readonly record struct IdempotencyScope(TenantId Tenant, WorkspaceId Workspace, PrincipalRef Principal, string Idempotency);
    private sealed record SubmissionReceipt(string OperationId, string CommandType, int CommandVersion, string InputFingerprint)
    {
        public bool Matches(CommandEnvelope command, string inputFingerprint) =>
            string.Equals(CommandType, command.Type, StringComparison.Ordinal) && CommandVersion == command.Version &&
            FixedTimeEquals(InputFingerprint, inputFingerprint);
    }

    /// <summary>Claims one durable command for execution. Claiming is idempotent and never trusts transport state.</summary>
    public bool TryClaimPending(string operationId, out CommandEnvelope? command)
        => TryClaimPending(operationId, out command, out _);

    public bool TryClaimPending(
        string operationId,
        out CommandEnvelope? command,
        out ExternalAuthorizationContinuation? authorization) =>
        TryClaimPending(operationId, out command, out authorization, out _);

    public bool TryClaimPending(
        string operationId,
        out CommandEnvelope? command,
        out ExternalAuthorizationContinuation? authorization,
        out ExternalAuthorizationResolution? authorizationResolution)
    {
        lock (_stateLock)
        {
            command = null;
            authorization = null;
            authorizationResolution = null;
            if (!_operations.TryGetValue(operationId, out var current) ||
                current.State is not (WorkflowState.ApplyQueued or WorkflowState.RetryScheduled) ||
                !_commands.TryGetValue(operationId, out command)) return false;
            if (!_claimed.TryAdd(operationId, 0)) return false;
            if (_externalAuthorizations.TryGetValue(operationId, out var pendingAuthorization))
                authorization = Clone(pendingAuthorization);
            if (_externalAuthorizationResolutions.TryGetValue(operationId, out var pendingResolution))
                authorizationResolution = pendingResolution;
            var applying = current with { State = WorkflowState.Applying, UpdatedAt = DateTimeOffset.UtcNow };
            try { PersistStatus(operationId, applying, authorization, authorizationResolution); }
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
        => _operations.Values.Where(x => x.State is WorkflowState.ApplyQueued or WorkflowState.RetryScheduled)
            .Select(x => x.OperationId).OrderBy(x => x, StringComparer.Ordinal).ToArray();

    public IReadOnlyList<ExternalAuthorizationWait> GetAwaitingExternalAuthorizations()
    {
        lock (_stateLock)
        {
            return _operations.Values
                .Where(static operation => operation.State == WorkflowState.AwaitingExternalAuthorization)
                .OrderBy(static operation => operation.OperationId, StringComparer.Ordinal)
                .Select(operation =>
                    _commands.TryGetValue(operation.OperationId, out var command) &&
                    _externalAuthorizations.TryGetValue(operation.OperationId, out var authorization)
                        ? new ExternalAuthorizationWait(operation.OperationId, command, Clone(authorization))
                        : null)
                .Where(static wait => wait is not null)
                .Select(static wait => wait!)
                .ToArray();
        }
    }

    public bool TryRequeueExternalAuthorization(string operationId, string attemptId) =>
        TryRequeueExternalAuthorization(
            operationId,
            attemptId,
            new ExternalAuthorizationResolution(ExternalAuthorizationResolutionState.Ready));

    public bool TryRequeueExternalAuthorization(
        string operationId,
        string attemptId,
        ExternalAuthorizationResolution resolution)
    {
        if (!ValidAuthorizationResolution(resolution))
            throw new ArgumentException("A terminal external authorization resolution is required.", nameof(resolution));
        lock (_stateLock)
        {
            if (!_operations.TryGetValue(operationId, out var current) ||
                current.State != WorkflowState.AwaitingExternalAuthorization ||
                !_externalAuthorizations.TryGetValue(operationId, out var authorization) ||
                !string.Equals(authorization.AttemptId, attemptId, StringComparison.Ordinal))
                return false;
            var queued = current with
            {
                State = WorkflowState.ApplyQueued,
                SafeReason = null,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            PersistStatus(operationId, queued, authorization, resolution);
            _operations[operationId] = queued;
            _externalAuthorizationResolutions[operationId] = resolution;
            return true;
        }
    }

    /// <summary>Records a worker outcome durably; ambiguous outcomes must be reported explicitly.</summary>
    public bool RecordOutcome(
        string operationId,
        WorkflowState state,
        string? safeReason = null,
        ExternalAuthorizationContinuation? authorization = null)
    {
        if (state is not (WorkflowState.RetryScheduled or WorkflowState.Succeeded or WorkflowState.Failed or
            WorkflowState.Cancelled or WorkflowState.OutcomeUnknown or WorkflowState.Compensated or
            WorkflowState.ManualIntervention or WorkflowState.AwaitingExternalAuthorization))
            throw new ArgumentOutOfRangeException(nameof(state));
        if (state == WorkflowState.AwaitingExternalAuthorization)
        {
            if (!ValidAuthorization(authorization))
                throw new ArgumentException("A valid external authorization continuation is required.", nameof(authorization));
        }
        else if (authorization is not null)
        {
            throw new ArgumentException("Only an external authorization wait may persist a continuation.", nameof(authorization));
        }
        lock (_stateLock)
        {
            if (!_operations.TryGetValue(operationId, out var current) || current.State != WorkflowState.Applying) return false;
            var updated = current with { State = state, SafeReason = safeReason, UpdatedAt = DateTimeOffset.UtcNow };
            PersistStatus(operationId, updated, authorization);
            _operations[operationId] = updated;
            if (authorization is null)
            {
                _externalAuthorizations.TryRemove(operationId, out _);
                _externalAuthorizationResolutions.TryRemove(operationId, out _);
            }
            else
            {
                _externalAuthorizations[operationId] = Clone(authorization);
                _externalAuthorizationResolutions.TryRemove(operationId, out _);
            }
            _claimed.TryRemove(operationId, out _);
            return true;
        }
    }

    private void PersistStatus(
        string operationId,
        OperationStatus status,
        ExternalAuthorizationContinuation? authorization = null,
        ExternalAuthorizationResolution? authorizationResolution = null)
    {
        if (_journal is null || !_commands.TryGetValue(operationId, out var command) ||
            !_operationIdempotency.TryGetValue(operationId, out var idempotency)) return;
        _journal.Append(OperationJournalKind(status), JsonSerializer.Serialize(new PersistedOperation(
            idempotency,
            command.Context.TenantId.Value,
            command.Context.WorkspaceId.Value,
            status,
            command,
            authorization is null ? null : Clone(authorization),
            authorizationResolution)));
    }

    private static string OperationJournalKind(OperationStatus operation) => "operation." + operation.State;

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
            _externalAuthorizations.TryGetValue(pair.Key, out var authorization);
            _externalAuthorizationResolutions.TryGetValue(pair.Key, out var authorizationResolution);
            PersistStatus(pair.Key, recovered, authorization, authorizationResolution);
            _operations[pair.Key] = recovered;
        }
    }

    private static bool ValidAuthorization(ExternalAuthorizationContinuation? authorization)
        => authorization?.IsValid() == true;

    private static bool ValidAuthorizationResolution(ExternalAuthorizationResolution? resolution) =>
        resolution is not null &&
        resolution.State is ExternalAuthorizationResolutionState.Ready or ExternalAuthorizationResolutionState.Failed &&
        (resolution.SafeReason is null || resolution.SafeReason.Length <= 256);

    private static ExternalAuthorizationContinuation Clone(ExternalAuthorizationContinuation authorization) =>
        authorization.Copy();

    private static void DemandAuthenticatedAuthority(RequestContext context, CommandEnvelope command)
    {
        if (!SameOwner(context, command.Context) || context.Assurance != command.Context.Assurance ||
            !string.Equals(context.SessionId, command.Context.SessionId, StringComparison.Ordinal) ||
            !context.Grants.SetEquals(command.Context.Grants))
            throw new UnauthorizedAccessException("Command authority must match the authenticated context.");
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
                throw new ArgumentException("The command input contains an unsupported JSON value.", nameof(value));
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
            throw new ArgumentException("Invalid operation cursor.", nameof(cursor));
        }
    }
}
