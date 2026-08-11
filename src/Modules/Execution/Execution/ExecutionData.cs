using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.persisted-state")]
internal sealed class ExecutionData(
    Goal goal,
    NeuronId worker,
    ExecutionPolicy policy,
    ExecutionState state,
    long revision,
    AttemptId? activeAttempt,
    ExecutionBlocker? blocker,
    Result? result,
    Failure? failure,
    FactReference[] evidence,
    NeuronId? retryOf,
    int attemptCount,
    Dictionary<CommandId, ExecutionSnapshot> receipts,
    List<CommandId> receiptOrder,
    PendingWorkerDispatch? pendingDispatch,
    Dictionary<string, OperationSnapshot> operations,
    List<string> operationOrder)
{
    [Id(0)]
    public Goal Goal { get; set; } = goal;

    [Id(1)]
    public NeuronId Worker { get; set; } = worker;

    [Id(2)]
    public ExecutionPolicy Policy { get; set; } = policy;

    [Id(3)]
    public ExecutionState State { get; set; } = state;

    [Id(4)]
    public long Revision { get; set; } = revision;

    [Id(5)]
    public AttemptId? ActiveAttempt { get; set; } = activeAttempt;

    [Id(6)]
    public ExecutionBlocker? Blocker { get; set; } = blocker;

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
    public Dictionary<CommandId, ExecutionSnapshot> Receipts { get; set; } = receipts;

    [Id(13)]
    public List<CommandId> ReceiptOrder { get; set; } = receiptOrder;

    [Id(14)]
    public PendingWorkerDispatch? PendingDispatch { get; set; } = pendingDispatch;

    [Id(15)]
    public Dictionary<string, OperationSnapshot> Operations { get; set; } =
        operations ?? new Dictionary<string, OperationSnapshot>(StringComparer.Ordinal);

    [Id(16)]
    public List<string> OperationOrder { get; set; } = operationOrder;
}
