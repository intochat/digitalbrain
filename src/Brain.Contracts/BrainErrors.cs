namespace Brain.Contracts;

public static class BrainErrors
{
    public const string RevisionConflict = "action.revision-conflict";
    public const string CausalLoop = "synapse.causal-loop";
    public const string CausalDepthExceeded = "synapse.causal-depth-exceeded";
    public const string OutOfOrderSource = "synapse.out-of-order-source";
    public const string DuplicateEvent = "synapse.duplicate-event";
    public const string JournalCommitFailed = "journal.commit-failed";
    public const string FailureSanitized = "neuron.failure";
}

[GenerateSerializer, Alias("brain.exception.v1")]
public sealed class BrainException(string code, string detail) : Exception($"{code}: {detail}")
{
    [Id(0)]
    public string Code { get; } = code;
}
