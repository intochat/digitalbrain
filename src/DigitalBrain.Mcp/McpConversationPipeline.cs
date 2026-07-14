using System.Diagnostics;
using System.Text.Json;
using DigitalBrain.Kernel.Contracts.Runtime;

namespace DigitalBrain.Mcp;

public sealed class McpInoCommandHandler(ConversationStateClient conversations)
{
    public const string CommandType = "ino.interact";

    public async Task<OperationReceipt> AcceptAsync(CommandEnvelope command)
    {
        if (!string.Equals(command.Type, CommandType, StringComparison.Ordinal))
            throw new InvalidOperationException("The command is not an INO interaction.");
        if (!TryGetPrompt(command.Payload, out var prompt))
            throw new ArgumentException("The INO request must contain one bounded prompt.", nameof(command));

        using var activity = InoTelemetry.Source.StartActivity("ino.conversation.accept", ActivityKind.Internal);
        activity?.SetTag("db.ino.command_type", command.Type);
        activity?.SetTag("db.ino.request_id", command.Context.CorrelationId);

        var snapshot = await conversations.BeginAsync(command.Context, command.CommandId, prompt, CancellationToken.None).ConfigureAwait(false);
        var operation = snapshot.Operations.Single(operation =>
            string.Equals(operation.CommandId, command.CommandId, StringComparison.Ordinal));
        var phase = string.Equals(operation.State, InoConversationStates.Queued, StringComparison.Ordinal)
            ? InoOperationPhase.Accepted
            : InoOperationPhases.FromConversationStatus(operation.State);
        activity?.SetTag("db.ino.operation_id", operation.OperationId);
        activity?.SetTag("db.ino.operation_phase", phase.ToString());
        activity?.SetTag("db.ino.outcome", "accepted");
        return new OperationReceipt(operation.OperationId, command.CommandId, phase, operation.Version);
    }

    public static bool TryGetPrompt(JsonElement payload, out string prompt)
    {
        prompt = string.Empty;
        if (payload.ValueKind != JsonValueKind.Object || payload.EnumerateObject().Count() != 1 ||
            !payload.TryGetProperty("prompt", out var value) || value.ValueKind != JsonValueKind.String)
            return false;
        prompt = value.GetString()?.Trim() ?? string.Empty;
        return prompt.Length is > 0 and <= 4096;
    }
}
