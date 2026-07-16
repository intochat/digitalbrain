namespace Brain.Contracts;

public enum SynapseRelation { Contains, Requires, Grants, BackedBy, Projects, CausedBy, Awaits, Approves, EmitsTo, UsesModule }

[GenerateSerializer, Alias("brain.synapse.v2")]
public sealed record SynapseRecord(
    [property: Id(0)] SynapseRelation Relation,
    [property: Id(1)] string TargetKey,
    [property: Id(2)] string Constraint,
    [property: Id(3)] long Revision);
