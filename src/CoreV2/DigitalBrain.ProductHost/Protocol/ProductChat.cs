using System.Text.Json;
using Brain.Abstractions.Activities;
using Brain.Abstractions.Runtime;
using Brain.Modules.UI.Contracts;

namespace DigitalBrain.ProductHost.Protocol;

public static class ProductChat
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<ChatTurnEnvelope> SendAsync(
        IProductRuntimeClient runtime,
        string message,
        string workspace,
        string principal,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        var receipt = await runtime.InvokeAsync(
            new BrainOperationInvocation(
                "Chat.Send@1",
                JsonSerializer.Serialize(new ChatSendInput(message.Trim()), JsonOptions),
                workspace,
                principal,
                idempotencyKey),
            cancellationToken);

        while (true)
        {
            var activity = await runtime.GetActivityAsync(
                receipt.ActivityId,
                workspace,
                cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Chat activity '{receipt.ActivityId:N}' disappeared after acceptance.");
            if (activity.Status == ActivityStatus.Completed)
            {
                var resultJson = activity.ResultJson
                    ?? throw new InvalidOperationException(
                        $"Chat activity '{receipt.ActivityId:N}' completed without a result.");
                var turn = JsonSerializer.Deserialize<ChatTurnResult>(resultJson, JsonOptions)
                    ?? throw new InvalidOperationException(
                        $"Chat activity '{receipt.ActivityId:N}' completed without a result.");
                return new ChatTurnEnvelope(receipt.ActivityId, turn);
            }

            if (activity.Status is ActivityStatus.Failed
                or ActivityStatus.Refused
                or ActivityStatus.Cancelled)
            {
                throw new InvalidOperationException(
                    activity.Problem ?? $"Chat activity '{receipt.ActivityId:N}' ended as {activity.Status}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }
}
