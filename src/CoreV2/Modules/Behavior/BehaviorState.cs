using Brain.Modules.Behavior.Contracts;

namespace Brain.Modules.Behavior;

[GenerateSerializer]
public sealed class BehaviorState
{
    [Id(0)]
    public string BehaviorId { get; set; } = string.Empty;

    [Id(1)]
    public int? ActiveRevision { get; set; }

    [Id(2)]
    public List<BehaviorRevision> Revisions { get; set; } = [];

    [Id(3)]
    public List<BehaviorRun> Runs { get; set; } = [];

    [Id(4)]
    public HashSet<string> ProcessedRequests { get; set; } = new(StringComparer.Ordinal);
}
