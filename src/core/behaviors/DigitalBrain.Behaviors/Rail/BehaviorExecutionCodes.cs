namespace DigitalBrain.Behaviors;

public static class BehaviorExecutionCodes
{
    public const string Succeeded = "behavior-executed";
    public const string Failed = "behavior-execution-failed";
    public const string Exception = "behavior-execution-exception";
    public const string Cancelled = "behavior-execution-cancelled";
    public const string InProcessClosed =
        "Hardened execution requires an isolated host/broker; in-process raw execution is closed.";

    public const string TriggerMissing = "behavior-trigger-missing";
    public const string TriggerUnauthorized = "behavior-trigger-unauthorized";
    public const string GrantMismatch = "capability-grant-mismatch";
    public const string ContractMismatch = "behavior-contract-mismatch";
    public const string HostNotConfigured = "protected-trigger-broker-not-configured";
    public const string UserActionRequired = "behavior-user-action-required";
    public const string UserActionDenied = "behavior-user-action-denied";

    public static string MapHostFailure(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Failed;
        }

        var trimmed = reason.Trim();
        return trimmed switch
        {
            Succeeded => Succeeded,
            Failed => Failed,
            Exception => Exception,
            Cancelled => Cancelled,
            InProcessClosed => InProcessClosed,
            TriggerMissing => TriggerMissing,
            TriggerUnauthorized => TriggerUnauthorized,
            GrantMismatch => GrantMismatch,
            ContractMismatch => ContractMismatch,
            HostNotConfigured => HostNotConfigured,
            UserActionRequired => UserActionRequired,
            UserActionDenied => UserActionDenied,
            "unknown-trigger-case" => ContractMismatch,
            "ambiguous-trigger-case" => ContractMismatch,
            "manifest-behavior-mismatch" => ContractMismatch,
            "behavior-mismatch" => ContractMismatch,
            "revision-hash-mismatch" => ContractMismatch,
            "revision-not-active" => ContractMismatch,
            "revision-not-deployed" => ContractMismatch,
            "owner-task-mismatch" => TriggerUnauthorized,
            "worker-mismatch" => TriggerUnauthorized,
            "attempt-mismatch" => TriggerUnauthorized,
            "activation-required" => TriggerUnauthorized,
            "activation-mismatch" => TriggerUnauthorized,
            "task-not-started" => TriggerUnauthorized,
            "invalid-task-identity" => TriggerUnauthorized,
            "invalid-protected-reference" => TriggerMissing,
            "invalid-payload-content" => TriggerMissing,
            "empty-payload" => TriggerMissing,
            "missing-owner" => TriggerUnauthorized,
            "missing-task-identity" => TriggerUnauthorized,
            "missing-task-owner" => TriggerUnauthorized,
            "invalid-request" => TriggerUnauthorized,
            "invalid-attempt" => TriggerUnauthorized,
            "one-way-capability-not-supported" => GrantMismatch,
            _ => Failed,
        };
    }

    public static bool IsInProcessClosed(string? outcome)
        => string.Equals(outcome, InProcessClosed, StringComparison.Ordinal);
}
