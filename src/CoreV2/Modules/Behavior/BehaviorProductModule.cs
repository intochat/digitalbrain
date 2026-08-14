using System.Text.Json;
using Brain.Modules.Behavior.Contracts;
using Brain.Runtime.Abstractions;

namespace Brain.Modules.Behavior;

public sealed class BehaviorProductModule(IGrainFactory grainFactory) : IRuntimeProductModule
{
    private const string PublishInputSchema = """
        {"type":"object","additionalProperties":false,"properties":{"behaviorId":{"type":"string"},"name":{"type":"string"},"source":{"type":"string"}},"required":["behaviorId","name","source"]}
        """;
    private const string ActivateInputSchema = """
        {"type":"object","additionalProperties":false,"properties":{"behaviorId":{"type":"string"},"revision":{"type":"integer","minimum":1}},"required":["behaviorId","revision"]}
        """;
    private const string RunInputSchema = """
        {"type":"object","additionalProperties":false,"properties":{"behaviorId":{"type":"string"},"input":{"type":"string"}},"required":["behaviorId","input"]}
        """;
    private const string ReadInputSchema = """
        {"type":"object","additionalProperties":false,"properties":{"behaviorId":{"type":"string"}},"required":["behaviorId"]}
        """;
    private const string ResultSchema = """
        {"type":"object","properties":{"behaviorId":{"type":"string"},"status":{"type":"string"},"latestRevision":{"type":"integer"},"activeRevision":{"type":["integer","null"]},"revisions":{"type":"array"},"runs":{"type":"array"}},"required":["behaviorId","status","latestRevision","revisions","runs"]}
        """;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IGrainFactory _grainFactory = grainFactory;

    public RuntimeModuleDescriptor Module { get; } = new(
        BehaviorContracts.ModuleId,
        "Behavior",
        RuntimeModuleStatus.Ready);

    public IReadOnlyList<RuntimeOperationDescriptor> Operations { get; } =
    [
        new(BehaviorContracts.PublishOperationId, BehaviorContracts.ModuleId, "Publish behavior revision", PublishInputSchema, ResultSchema),
        new(BehaviorContracts.ActivateOperationId, BehaviorContracts.ModuleId, "Activate behavior revision", ActivateInputSchema, ResultSchema),
        new(BehaviorContracts.RunOperationId, BehaviorContracts.ModuleId, "Run active behavior", RunInputSchema, ResultSchema),
        new(BehaviorContracts.ReadOperationId, BehaviorContracts.ModuleId, "Read behavior", ReadInputSchema, ResultSchema),
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
            throw new JsonException("Behavior input must be a JSON object.");
        }

        var root = document.RootElement;
        var behaviorId = RequiredString(root, "behaviorId");
        ValidateIdentifier(behaviorId);
        var grain = _grainFactory.GetGrain<IBehaviorGrain>(BehaviorGrainKey.Create(context.Workspace, behaviorId));
        var snapshot = operationId switch
        {
            BehaviorContracts.PublishOperationId => await grain.PublishAsync(new PublishBehaviorRequest(
                RequiredString(root, "name"),
                RequiredString(root, "source"),
                context.Principal,
                context.IdempotencyKey)),
            BehaviorContracts.ActivateOperationId => await grain.ActivateAsync(
                RequiredPositiveInteger(root, "revision"),
                context.IdempotencyKey),
            BehaviorContracts.RunOperationId => await grain.RunAsync(new RunBehaviorRequest(
                context.Activity.ToString("N"),
                RequiredString(root, "input"),
                context.Principal,
                context.IdempotencyKey)),
            BehaviorContracts.ReadOperationId => await grain.ReadAsync(),
            _ => throw new KeyNotFoundException($"Behavior operation '{operationId}' is not installed."),
        };
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    private static string RequiredString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && value.GetString()?.Trim() is { Length: > 0 } text
            ? text
            : throw new JsonException($"Behavior input requires a non-empty '{name}'.");

    private static int RequiredPositiveInteger(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) && number > 0
            ? number
            : throw new JsonException($"Behavior input requires a positive integer '{name}'.");

    private static void ValidateIdentifier(string behaviorId)
    {
        if (behaviorId.Length is < 1 or > 80
            || behaviorId.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new JsonException("Behavior id must contain only letters, digits, '-' or '_'.");
        }
    }
}
