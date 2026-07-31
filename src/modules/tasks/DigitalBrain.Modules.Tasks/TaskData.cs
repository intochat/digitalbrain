using DigitalBrain.Abstractions;

namespace DigitalBrain.Tasks;

[GenerateSerializer]
[Alias("tasks.persisted-state")]
internal sealed class TaskData(
    Goal goal,
    NeuronId worker,
    TaskPolicy policy,
    TaskState state,
    long revision,
    AttemptId? activeAttempt,
    TaskBlocker? blocker,
    Result? result,
    Failure? failure,
    FactReference[] evidence,
    NeuronId? retryOf,
    int attemptCount,
    Dictionary<CommandId, TaskSnapshot> receipts,
    PendingWorkerDispatch? pendingDispatch,
    BehaviorTaskActivation? activation,
    Dictionary<string, TaskOperationSnapshot> operations)
{
    [Id(0)]
    public Goal Goal { get; set; } = goal;

    [Id(1)]
    public NeuronId Worker { get; set; } = worker;

    [Id(2)]
    public TaskPolicy Policy { get; set; } = policy;

    [Id(3)]
    public TaskState State { get; set; } = state;

    [Id(4)]
    public long Revision { get; set; } = revision;

    [Id(5)]
    public AttemptId? ActiveAttempt { get; set; } = activeAttempt;

    [Id(6)]
    public TaskBlocker? Blocker { get; set; } = blocker;

    [Id(7)]
    public Result? Result { get; set; } = result;

    [Id(8)]
    public Failure? Failure { get; set; } = failure;

    [Id(9)]
    public FactReference[] Evidence { get; set; } = evidence;

    [Id(10)]
    public NeuronId? RetryOf { get; set; } = retryOf;

    [Id(11)]
    public int AttemptCount { get; set; } = attemptCount;

    [Id(12)]
    public Dictionary<CommandId, TaskSnapshot> Receipts { get; set; } = receipts;

    [Id(13)]
    public PendingWorkerDispatch? PendingDispatch { get; set; } = pendingDispatch;

    [Id(14)]
    public BehaviorTaskActivation? Activation { get; set; } = activation;

    [Id(15)]
    public Dictionary<string, TaskOperationSnapshot> Operations { get; set; } = operations ?? new Dictionary<string, TaskOperationSnapshot>(StringComparer.Ordinal);
}
