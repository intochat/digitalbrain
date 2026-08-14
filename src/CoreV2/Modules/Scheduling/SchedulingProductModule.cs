using System.Globalization;
using System.Text.Json;
using Brain.Modules.Scheduling.Contracts;
using Brain.Runtime.Abstractions;

namespace Brain.Modules.Scheduling;

public sealed class SchedulingProductModule(IGrainFactory grainFactory) : IRuntimeProductModule
{
    private const string ScheduleInputSchema = """
        {"type":"object","additionalProperties":false,"properties":{"scheduleId":{"type":"string"},"title":{"type":"string"},"dueAtUtc":{"type":"string","format":"date-time"}},"required":["scheduleId","title","dueAtUtc"]}
        """;
    private const string IdentityInputSchema = """
        {"type":"object","additionalProperties":false,"properties":{"scheduleId":{"type":"string"}},"required":["scheduleId"]}
        """;
    private const string ResultSchema = """
        {"type":"object","properties":{"scheduleId":{"type":"string"},"title":{"type":["string","null"]},"dueAtUtc":{"type":["string","null"]},"status":{"type":"string"}},"required":["scheduleId","status"]}
        """;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IGrainFactory _grainFactory = grainFactory;

    public RuntimeModuleDescriptor Module { get; } = new(
        SchedulingContracts.ModuleId,
        "Scheduling",
        RuntimeModuleStatus.Ready);

    public IReadOnlyList<RuntimeOperationDescriptor> Operations { get; } =
    [
        new(SchedulingContracts.ScheduleOperationId, SchedulingContracts.ModuleId, "Schedule reminder", ScheduleInputSchema, ResultSchema),
        new(SchedulingContracts.ReadOperationId, SchedulingContracts.ModuleId, "Read schedule", IdentityInputSchema, ResultSchema),
        new(SchedulingContracts.CancelOperationId, SchedulingContracts.ModuleId, "Cancel schedule", IdentityInputSchema, ResultSchema),
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
            throw new JsonException("Scheduling input must be a JSON object.");
        }

        var root = document.RootElement;
        var scheduleId = RequiredString(root, "scheduleId");
        ValidateIdentifier(scheduleId);
        var grain = _grainFactory.GetGrain<IScheduleGrain>(ScheduleGrainKey.Create(context.Workspace, scheduleId));
        var snapshot = operationId switch
        {
            SchedulingContracts.ScheduleOperationId => await grain.ScheduleAsync(new ScheduleRequest(
                RequiredString(root, "title"),
                RequiredInstant(root, "dueAtUtc"),
                context.IdempotencyKey)),
            SchedulingContracts.ReadOperationId => await grain.ReadAsync(),
            SchedulingContracts.CancelOperationId => await grain.CancelAsync(context.IdempotencyKey),
            _ => throw new KeyNotFoundException($"Scheduling operation '{operationId}' is not installed."),
        };
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    private static string RequiredString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && value.GetString()?.Trim() is { Length: > 0 } text
            ? text
            : throw new JsonException($"Scheduling input requires a non-empty '{name}'.");

    private static DateTimeOffset RequiredInstant(JsonElement root, string name)
        => DateTimeOffset.TryParse(
            RequiredString(root, name),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var value)
            ? value
            : throw new JsonException($"Scheduling input '{name}' must be an ISO-8601 instant.");

    private static void ValidateIdentifier(string scheduleId)
    {
        if (scheduleId.Length is < 1 or > 80
            || scheduleId.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new JsonException("Schedule id must contain only letters, digits, '-' or '_'.");
        }
    }
}
