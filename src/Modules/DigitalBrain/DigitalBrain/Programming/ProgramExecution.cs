using System.Text.Json;
using DigitalBrain.Abstractions.Programming;

namespace DigitalBrain.Core.Programming;

public interface IProgramNodeExecutor
{
    Task<JsonElement> ExecuteAsync(ProgramExecutionContext context, CancellationToken cancellationToken);
}

public interface IProgramRunCancellation
{
    void Cancel(string programId, string runId);
}

public sealed record ProgramExecutionContext(
    string ProgramId, string RunId, long Version, ProgramNode Node,
    JsonElement Input, JsonElement Value, IReadOnlyDictionary<string, JsonElement> Outputs);

internal static class ProgramWire
{
    internal static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);
    internal static JsonElement Null { get; } = JsonSerializer.SerializeToElement<object?>(null);
    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    internal static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Json)
        ?? throw new ArgumentException("The program signal body is empty.");
}

internal sealed record ProgramMutation(string OperationId, string Action, ProgramDefinition? Definition = null,
    long? ExpectedVersion = null, long? Version = null, bool? Enabled = null, string? RunId = null, JsonElement Input = default);

internal sealed record ProgramStart(string RunId, long Version, ProgramDefinition Definition, JsonElement Input);
internal sealed record ProgramWork(string ProgramId, string RunId, long Version, ProgramNode Node,
    JsonElement Input, JsonElement Value, IReadOnlyList<ProgramStep> Previous, bool Skip);
internal sealed record ProgramCompletion(ProgramStep Step);

[GenerateSerializer]
internal sealed record ProgramRunState(
    [property: Id(0)] ProgramDefinition Definition,
    [property: Id(1)] ProgramRunSnapshot Snapshot);

[GenerateSerializer]
internal sealed record ProgramCatalogState([property: Id(0)] IReadOnlyList<string> Names);
