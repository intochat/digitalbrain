using System.Text.Json;
using Brain.Abstractions.Runtime;
using Brain.Core.Runtime;

namespace Brain.Modules.Proof;

public sealed class ProofRunOperationHandler : IBrainOperationHandler
{
    private const string InputSchema = """
        {"type":"object","additionalProperties":false,"properties":{"value":{"type":"string"}},"required":["value"]}
        """;
    private const string ResultSchema = """
        {"type":"object","additionalProperties":false,"properties":{"route":{"type":"string"}},"required":["route"]}
        """;

    public BrainOperationDescriptor Descriptor { get; } = new(
        "Proof.Run@1",
        "proof",
        "Run proof through live BrainGraph",
        InputSchema,
        ResultSchema);

    public async Task<string> ExecuteAsync(
        BrainOperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var input = JsonDocument.Parse(context.Invocation.InputJson);
        if (!input.RootElement.TryGetProperty("value", out var valueElement)
            || valueElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(valueElement.GetString()))
        {
            throw new JsonException("Proof.Run@1 requires a non-empty value.");
        }

        var source = context.Grains.GetGrain<IProofSourceNeuron>(
            $"{context.Invocation.WorkspaceId}/{context.Invocation.PrincipalId}");
        return await source.RunAsync(new ProofNeuronRequest(
            context.ActivityId,
            context.Invocation,
            valueElement.GetString()!.Trim()));
    }
}
