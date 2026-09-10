using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

// What the turn is reacting to: a delivery drained from Pending, or a command being executed.
public abstract record ReactionContext;

public sealed record DeliveryReaction(SignalDelivery Delivery) : ReactionContext;

// Work starts null because the scheduled work id does not exist until execute calls Schedule,
// which then replaces the context with `commandReaction with { Work = id }`.
public sealed record CommandReaction(CommandId Command, SignalId? Work = null) : ReactionContext;
