using System.Text.Json;
using Brain.Abstractions.Activities;
using Brain.Abstractions.Identity;

namespace Brain.Product.Abstractions.Activities;

public sealed record ProductActivityReceipt
{
    public ProductActivityReceipt(BrainActivityId activity, OperationId operation)
    {
        if (activity.Value == Guid.Empty)
        {
            throw new ArgumentException("A product activity receipt requires an activity.", nameof(activity));
        }

        if (string.IsNullOrWhiteSpace(operation.Value))
        {
            throw new ArgumentException("A product activity receipt requires an operation.", nameof(operation));
        }

        Activity = activity;
        Operation = operation;
    }

    public BrainActivityId Activity { get; }

    public OperationId Operation { get; }
}

public sealed record ProductActivityProjection
{
    public ProductActivityProjection(ActivityView activity, JsonElement? progress, JsonElement? result)
    {
        ArgumentNullException.ThrowIfNull(activity);
        Activity = activity;
        Progress = progress?.Clone();
        Result = result?.Clone();
    }

    public ActivityView Activity { get; }

    public JsonElement? Progress { get; }

    public JsonElement? Result { get; }
}
