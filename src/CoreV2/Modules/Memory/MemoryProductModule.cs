using System.Text.Json;
using Brain.Modules.Memory.Contracts;
using Brain.Runtime.Abstractions;

namespace Brain.Modules.Memory;

public sealed class MemoryProductModule(IGrainFactory grainFactory) : IRuntimeProductModule
{
    private const string StoreInputSchema = """
        {"type":"object","additionalProperties":false,"properties":{"namespace":{"type":"string"},"key":{"type":"string"},"text":{"type":"string"}},"required":["namespace","key","text"]}
        """;
    private const string SearchInputSchema = """
        {"type":"object","additionalProperties":false,"properties":{"namespace":{"type":"string"},"query":{"type":"string"},"limit":{"type":"integer","minimum":1,"maximum":100}},"required":["namespace","query"]}
        """;
    private const string RemoveInputSchema = """
        {"type":"object","additionalProperties":false,"properties":{"namespace":{"type":"string"},"key":{"type":"string"}},"required":["namespace","key"]}
        """;
    private const string MutationResultSchema = """
        {"type":"object","properties":{"namespace":{"type":"string"},"key":{"type":"string"},"status":{"type":"string"}},"required":["namespace","key","status"]}
        """;
    private const string SearchResultSchema = """
        {"type":"object","properties":{"namespace":{"type":"string"},"records":{"type":"array"}},"required":["namespace","records"]}
        """;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IGrainFactory _grainFactory = grainFactory;

    public RuntimeModuleDescriptor Module { get; } = new(
        MemoryContracts.ModuleId,
        "Memory",
        RuntimeModuleStatus.Ready);

    public IReadOnlyList<RuntimeOperationDescriptor> Operations { get; } =
    [
        new(MemoryContracts.StoreOperationId, MemoryContracts.ModuleId, "Store memory", StoreInputSchema, MutationResultSchema),
        new(MemoryContracts.SearchOperationId, MemoryContracts.ModuleId, "Search memory", SearchInputSchema, SearchResultSchema),
        new(MemoryContracts.RemoveOperationId, MemoryContracts.ModuleId, "Remove memory", RemoveInputSchema, MutationResultSchema),
    ];

    public async Task<string> ExecuteAsync(
        string operationId,
        string inputJson,
        RuntimeModuleExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        using var document = JsonDocument.Parse(inputJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Memory input must be a JSON object.");
        }

        var root = document.RootElement;
        var memoryNamespace = RequiredString(root, "namespace");
        ValidateIdentifier(memoryNamespace, "Memory namespace");
        var grain = _grainFactory.GetGrain<IMemoryGrain>(
            MemoryGrainKey.Create(context.Workspace, memoryNamespace));
        object result = operationId switch
        {
            MemoryContracts.StoreOperationId => await grain.StoreAsync(new StoreMemoryRequest(
                ValidatedKey(root),
                RequiredString(root, "text"),
                context.Principal,
                context.IdempotencyKey)),
            MemoryContracts.SearchOperationId => await grain.SearchAsync(
                RequiredString(root, "query"),
                OptionalLimit(root)),
            MemoryContracts.RemoveOperationId => await grain.RemoveAsync(
                ValidatedKey(root),
                context.IdempotencyKey),
            _ => throw new KeyNotFoundException($"Memory operation '{operationId}' is not installed."),
        };
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    private static string ValidatedKey(JsonElement root)
    {
        var key = RequiredString(root, "key");
        ValidateIdentifier(key, "Memory key");
        return key;
    }

    private static string RequiredString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && value.GetString()?.Trim() is { Length: > 0 } text
            ? text
            : throw new JsonException($"Memory input requires a non-empty '{name}'.");

    private static int OptionalLimit(JsonElement root)
    {
        if (!root.TryGetProperty("limit", out var value))
        {
            return 20;
        }

        return value.TryGetInt32(out var limit) && limit is >= 1 and <= 100
            ? limit
            : throw new JsonException("Memory input 'limit' must be between 1 and 100.");
    }

    private static void ValidateIdentifier(string value, string label)
    {
        if (value.Length is < 1 or > 80
            || value.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new JsonException($"{label} must contain only letters, digits, '-' or '_'.");
        }
    }
}
