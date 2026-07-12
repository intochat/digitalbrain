using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;

namespace DigitalBrain.RuntimeMigration;

public enum MigrationMode { DryRun, Apply }

public sealed class MigrationGapException(string code) : InvalidOperationException(code)
{
    public string Code { get; } = code;
}

public static class MigrationModeParser
{
    public static MigrationMode Parse(string[] args) => args switch
    {
        ["--dry-run"] => MigrationMode.DryRun,
        ["--apply"] => MigrationMode.Apply,
        _ => throw new MigrationGapException("mode-required")
    };
}

public sealed record VerifiedJournalRecord(
    int LineNumber,
    string? Kind,
    string Payload,
    bool IsLegacy,
    string SourceDigest);

public sealed record VerifiedJournal(
    string Domain,
    IReadOnlyList<VerifiedJournalRecord> Records,
    long Sequence,
    string HeadDigest);

public sealed record LegacyPersistedOperation(
    string Idempotency,
    string Tenant,
    string Workspace,
    DigitalBrain.Core.Runtime.OperationStatus Operation,
    CommandEnvelope? Command = null,
    ExternalAuthorizationContinuation? Authorization = null,
    ExternalAuthorizationResolution? AuthorizationResolution = null);

public sealed record LegacyPersistedConversation(
    int Version,
    TenantId Tenant,
    WorkspaceId Workspace,
    PrincipalRef Principal,
    InoConversationSnapshot Snapshot);

public readonly record struct LegacyConversationScope(
    TenantId Tenant,
    WorkspaceId Workspace,
    PrincipalRef Principal);

public sealed record PlannedTurn(
    string CommandId,
    string OperationId,
    string Role,
    string Text,
    DateTimeOffset CreatedAt);

public sealed record PlannedOperation(
    string CommandId,
    string InputHash,
    string UserText,
    ConversationOperation Destination);

public sealed record ConversationImportPlan(
    string GrainKey,
    ConversationIdentity Identity,
    IReadOnlyList<PlannedTurn> Turns,
    IReadOnlyList<PlannedOperation> Operations,
    string MigrationId,
    string ExpectedDigest);

public sealed record RuntimeMigrationPlan(
    int SchemaVersion,
    string SourceDigest,
    string MigrationId,
    string MigrationDigest,
    string ExpectedDigest,
    IReadOnlyList<ConversationImportPlan> Conversations)
{
    public int TurnCount => Conversations.Sum(static conversation => conversation.Turns.Count);
    public int ActiveOperationCount => Conversations.Sum(static conversation =>
        conversation.Operations.Count(operation => !MigrationHash.IsTerminal(operation.Destination.Status)));
    public int TerminalOperationCount => Conversations.Sum(static conversation =>
        conversation.Operations.Count(operation => MigrationHash.IsTerminal(operation.Destination.Status)));
}

public sealed record MigrationMarker(
    int SchemaVersion,
    string SourceDigest,
    string MigrationId,
    string ExpectedDigest,
    int ConversationCount,
    int TurnCount,
    int ActiveOperationCount,
    int TerminalOperationCount);

public static class MigrationHash
{
    public static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string Sha256(ReadOnlySpan<byte> value) =>
        Convert.ToHexStringLower(SHA256.HashData(value));

    public static string PromptHash(string prompt) => Sha256(prompt.Trim());

    public static string OperationId(LegacyConversationScope scope, string commandId)
    {
        var context = ValidationContext(scope);
        return "runtime-op-" + Sha256(RequestScope.Id(context) + "\0" + commandId);
    }

    public static string GrainKey(LegacyConversationScope scope, string conversationId) =>
        RuntimeStateKeys.Conversation(scope.Tenant, scope.Workspace, scope.Principal, conversationId);

    public static string ConversationDigest(ConversationIdentity identity, IEnumerable<PlannedTurn> turns,
        IEnumerable<PlannedOperation> operations)
    {
        var sanitized = new
        {
            identity = new
            {
                tenant = Sha256(identity.TenantId.Value),
                workspace = Sha256(identity.WorkspaceId.Value),
                principal = Sha256($"{(int)identity.Principal.Kind}\0{identity.Principal.Value}"),
                conversation = Sha256(identity.ConversationId)
            },
            turns = turns.Select(static turn => new
            {
                command = Sha256(turn.CommandId),
                operation = Sha256(turn.OperationId),
                turn.Role,
                text = Sha256(turn.Text),
                created = turn.CreatedAt.ToUniversalTime().ToString("O")
            }).ToArray(),
            operations = operations.OrderBy(static operation => operation.Destination.OperationId, StringComparer.Ordinal)
                .Select(static operation => Sanitize(operation.Destination, operation.InputHash)).ToArray()
        };
        return Sha256(JsonSerializer.SerializeToUtf8Bytes(sanitized));
    }

    public static string ConversationDigest(ConversationState state)
    {
        if (state.Identity is null) throw new MigrationGapException("destination-identity-missing");
        var operations = state.Operations.Select(operation => new PlannedOperation(
            operation.CommandId,
            state.Inbox.Single(entry => string.Equals(entry.OperationId, operation.OperationId, StringComparison.Ordinal)).InputHash,
            state.Turns.Single(turn => turn.Kind == ConversationTurnKind.User &&
                                       string.Equals(turn.OperationId, operation.OperationId, StringComparison.Ordinal)).Text,
            operation));
        var turns = state.Turns.Select(turn => new PlannedTurn(
            state.Operations.Single(operation => string.Equals(operation.OperationId, turn.OperationId, StringComparison.Ordinal)).CommandId,
            turn.OperationId,
            turn.Role,
            turn.Text,
            turn.CreatedAt));
        return ConversationDigest(state.Identity, turns, operations);
    }

    public static bool IsTerminal(ConversationOperationStatus status) => status is
        ConversationOperationStatus.Succeeded or ConversationOperationStatus.Failed or
        ConversationOperationStatus.OutcomeUnknown or ConversationOperationStatus.Cancelled;

    public static DigitalBrain.Core.Runtime.RequestContext ValidationContext(LegacyConversationScope scope) => new(
        scope.Tenant,
        scope.Workspace,
        scope.Principal,
        "migration-validation",
        AuthAssurance.Password,
        "migration-validation",
        null,
        new HashSet<string>(StringComparer.Ordinal));

    private static object Sanitize(ConversationOperation operation, string inputHash) => new
    {
        operation = Sha256(operation.OperationId),
        command = Sha256(operation.CommandId),
        inputHash = inputHash.ToLowerInvariant(),
        status = (int)operation.Status,
        operation.Attempt,
        next = operation.NextAttemptAt?.ToUniversalTime().ToString("O"),
        terminalPolicy = (int)operation.TerminalPolicy,
        reason = operation.SafeReason is null ? null : Sha256(operation.SafeReason),
        updated = operation.UpdatedAt.ToUniversalTime().ToString("O"),
        authorization = operation.SuspendedInvocation is null ? null : new
        {
            provider = Sha256(operation.SuspendedInvocation.Provider),
            tool = Sha256(operation.SuspendedInvocation.ToolId),
            input = Sha256(operation.SuspendedInvocation.InputUtf8),
            attempt = Sha256(operation.SuspendedInvocation.AuthorizationAttemptId),
            expires = operation.SuspendedInvocation.AuthorizationExpiresAt.ToUniversalTime().ToString("O"),
            flow = Sha256(operation.SuspendedInvocation.AuthorizationFlowReference)
        }
    };
}

public sealed class MigrationOutput(TextWriter writer)
{
    public void Write(RuntimeMigrationPlan plan, MigrationMode mode, string status)
    {
        if (status is not ("verified" or "complete"))
            throw new MigrationGapException("output-status-invalid");
        writer.WriteLine($"schema={plan.SchemaVersion}");
        writer.WriteLine($"migration_status={status}");
        writer.WriteLine($"mode={(mode == MigrationMode.Apply ? "apply" : "dry-run")}");
        writer.WriteLine($"conversations={plan.Conversations.Count}");
        writer.WriteLine($"turns={plan.TurnCount}");
        writer.WriteLine($"active_operations={plan.ActiveOperationCount}");
        writer.WriteLine($"terminal_operations={plan.TerminalOperationCount}");
        writer.WriteLine("sessions_imported=0");
        writer.WriteLine("feeds_imported=0");
        writer.WriteLine($"source_digest={plan.SourceDigest}");
        writer.WriteLine($"migration_digest={plan.MigrationDigest}");
        writer.WriteLine($"destination_digest={plan.ExpectedDigest}");
    }

    public void WriteFailure(string code)
    {
        if (code.Length is not (> 0 and <= 64) || code.Any(static character =>
                !(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
            code = "migration-failed";
        writer.WriteLine("schema=1");
        writer.WriteLine("migration_status=blocked");
        writer.WriteLine($"migration_gap={code}");
    }
}
