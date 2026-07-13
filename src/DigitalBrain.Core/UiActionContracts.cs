using System.Text.Json;

namespace DigitalBrain.Core.Runtime;

public sealed record ActionSubmission(string OperationId, string IdempotencyKey, JsonElement Input, string ActionType);

public enum ActionRejection
{
    Unavailable,
    Forged,
    Expired,
    Replay,
    WrongOwner,
    WrongWorkspace,
    WrongRevision,
    PolicyDenied
}

public sealed class ActionRejectedException(ActionRejection reason) : UnauthorizedAccessException("Action authorization failed.")
{
    public ActionRejection Reason { get; } = reason;
}
