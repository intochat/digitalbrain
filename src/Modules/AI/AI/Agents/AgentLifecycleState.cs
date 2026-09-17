using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using Orleans.Concurrency;

namespace DigitalBrain.AI;

internal interface IAgentLifecycle : IGrainWithStringKey
{
    Task Initialize(AgentInitialization initialization);

    Task Retire(CancelAgent command);

    [ReadOnly]
    Task<AgentWork?> Work(string requestId);
}

[GenerateSerializer]
internal sealed record AgentInitialization(
    [property: Id(0)] AgentSnapshot Snapshot,
    [property: Id(1)] string RequestHash,
    [property: Id(2)] string? InitialMessage);

[GenerateSerializer]
public sealed record AgentManagedState(
    [property: Id(0)] AgentSnapshot Snapshot,
    [property: Id(1)] string RequestHash,
    [property: Id(2)] string HistoryJson,
    [property: Id(3)] IReadOnlyList<AgentTaskEntry> Tasks,
    [property: Id(4)] IReadOnlyList<AgentReceipt> Receipts,
    [property: Id(5)] IReadOnlyList<AgentReceipt>? StopReceipts = null);

[GenerateSerializer]
public sealed record AgentTaskEntry(
    [property: Id(0)] AgentResponse Snapshot,
    [property: Id(1)] string RequestHash,
    [property: Id(2)] SignalId PumpSignal,
    [property: Id(3)] SignalId? WorkSignal = null,
    [property: Id(4)] NeuronId? ReplyTo = null,
    [property: Id(5)] CorrelationId? Correlation = null,
    [property: Id(6)] bool Say = false);

[GenerateSerializer]
public sealed record AgentReceipt([property: Id(0)] CommandId CommandId,
    [property: Id(1)] string RequestHash, [property: Id(2)] string? TaskId);

[GenerateSerializer]
internal sealed record AgentWork([property: Id(0)] AgentSnapshot Agent,
    [property: Id(1)] AgentResponse Task, [property: Id(2)] string HistoryJson);

[GenerateSerializer]
internal sealed record AgentTaskResult([property: Id(0)] string TaskId,
    [property: Id(1)] string Status, [property: Id(2)] string? Output,
    [property: Id(3)] string? Error, [property: Id(4)] string? HistoryJson);

internal sealed record AgentTaskStart(NeuronId AgentId, string TaskId);

internal static class AgentLifecycle
{
    internal static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);
    internal const int MaxTasks = 64;
    internal const int MaxReceipts = 256;
    internal const string Pump = "AgentTaskPump";
    internal const string Start = "AgentTaskStart";
    internal const string Completed = "AgentTaskCompleted";
    internal static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    internal static string Fingerprint<T>(T value) => Hash(JsonSerializer.Serialize(value, Json));
    internal static SignalId Signal(string key) => new(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16)));
    internal static NeuronId Worker(NeuronId agent, string taskId) => new("agent-task", Hash(agent + "/" + taskId));

    internal static void RequireText(string value, string parameter, int maximumBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        if (Encoding.UTF8.GetByteCount(value) > maximumBytes)
        {
            throw new ArgumentException($"{parameter} exceeds its {maximumBytes}-byte limit.", parameter);
        }
    }
}
