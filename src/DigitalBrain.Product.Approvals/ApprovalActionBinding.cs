namespace DigitalBrain.Product.Approvals;

public sealed record ApprovalActionBinding
{
    public ApprovalActionBinding(
        string actionKind,
        string actionId,
        string actionFingerprint,
        NeuronId executionTarget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionFingerprint);
        if (executionTarget == default
            || string.IsNullOrWhiteSpace(executionTarget.Kind)
            || string.IsNullOrWhiteSpace(executionTarget.Name))
        {
            throw new ArgumentException("An approval action needs an execution target.", nameof(executionTarget));
        }

        ActionKind = actionKind.Trim();
        ActionId = actionId.Trim();
        ActionFingerprint = actionFingerprint.Trim();
        ExecutionTarget = executionTarget;
    }

    public string ActionKind { get; }

    public string ActionId { get; }

    public string ActionFingerprint { get; }

    public NeuronId ExecutionTarget { get; }
}
