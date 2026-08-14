using System.Text.Json;
using Brain.Modules.Proof.Contracts;
using Brain.Runtime.Abstractions;

namespace Brain.Modules.Proof;

public sealed class ProofProductModule : IRuntimeProductModule
{
    private const string InputSchema = """
        {"type":"object","additionalProperties":false,"properties":{"value":{"type":"string"}},"required":["value"]}
        """;
    private const string ResultSchema = """
        {"type":"object","additionalProperties":false,"properties":{"route":{"type":"string"}},"required":["route"]}
        """;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public RuntimeModuleDescriptor Module { get; } = new(
        ProofContracts.Module.Value,
        "Proof",
        RuntimeModuleStatus.Ready);

    public IReadOnlyList<RuntimeOperationDescriptor> Operations { get; } =
    [
        new(
            ProofContracts.Run.Id.Value,
            ProofContracts.Module.Value,
            "Run durable proof",
            InputSchema,
            ResultSchema),
    ];

    public Task<string> ExecuteAsync(
        string operationId,
        string inputJson,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(operationId, ProofContracts.Run.Id.Value, StringComparison.Ordinal))
        {
            throw new KeyNotFoundException($"Proof operation '{operationId}' is not installed.");
        }

        using var document = JsonDocument.Parse(inputJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Proof input must be a JSON object.");
        }

        var properties = document.RootElement.EnumerateObject().ToArray();
        if (properties.Length != 1
            || !string.Equals(properties[0].Name, "value", StringComparison.Ordinal)
            || properties[0].Value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(properties[0].Value.GetString()))
        {
            throw new JsonException("Proof input must contain exactly one non-empty string property named 'value'.");
        }

        var input = JsonSerializer.Deserialize<ProofInput>(inputJson, JsonOptions)
            ?? throw new JsonException("Proof input is required.");
        var result = new ProofResult($"proof/{input.Value.Trim()}");
        return Task.FromResult(JsonSerializer.Serialize(result, JsonOptions));
    }
}
