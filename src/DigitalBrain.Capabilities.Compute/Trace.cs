namespace DigitalBrain.Capabilities.Compute;

// Why a piece of work runs: started at a trusted edge (a request, a connector event), inherited by
// everything published or called while handling it. Stamped by the runtime like Publisher, never
// set by user code; whoever it names pays for the work.
public sealed record Trace
{
    public required string TraceId { get; init; }
    public required string PayerAccountId { get; init; }
    public required string BrainId { get; init; }
    public required NeuronId Origin { get; init; }
    public long? BudgetMicros { get; init; }
}
