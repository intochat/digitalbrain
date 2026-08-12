namespace DigitalBrain.Abstractions.Inbox;

// Owner/principal refusals log. Declares NO IHandle<T> — outcomes arrive by directed send
// and are accepted in OnUnboundSynapseAsync (avoids broadcast ghost inboxes).
[ClientEntryPoint]
[Alias("db.inbox")]
public partial interface IInbox : INeuron
{
    const string GrainTypeName = "inbox";
    const string InstanceName = "main";

    static NeuronId ForOwner(OwnerId owner)
        => new(GrainTypeName, owner, InstanceName);

    // A18: per-principal refusals log once delivery.Principal rides the outbox.
    static NeuronId ForPrincipal(OwnerId owner, PrincipalId principal)
        => new(GrainTypeName, owner, PrincipalPartition.InstanceName(principal, InstanceName));
}
