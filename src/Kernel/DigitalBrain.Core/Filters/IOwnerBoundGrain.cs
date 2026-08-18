namespace DigitalBrain.Core;

// Marks grain types whose grain key is "{owner}{separator}{name}", so OwnerBoundCallFilter can
// enforce the cross-owner wall and the unattributed-caller gate against them. Neuron and
// Entity<TState> both qualify; the filter never needs a member off this interface — it recovers
// the owner from context.TargetId, which carries the identical grain key.
internal interface IOwnerBoundGrain
{
}
