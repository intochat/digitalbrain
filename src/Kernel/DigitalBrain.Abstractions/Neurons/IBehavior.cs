namespace DigitalBrain.Abstractions;

// Durable behavior runs (repo review): file stances + moderator folds → plan.
[ClientEntryPoint]
[Alias("db.behavior")]
public partial interface IBehavior :
    INeuron,
    IHandle<StartRepoReview>,
    IHandle<ReadBehaviorRun>
{
    const string GrainTypeName = "behavior";
    const string InstanceName = "main";

    static NeuronId ForOwner(OwnerId owner)
        => new(GrainTypeName, owner, InstanceName);
}
