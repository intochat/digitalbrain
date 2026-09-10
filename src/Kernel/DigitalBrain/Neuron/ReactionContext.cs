using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

// What the turn is reacting to: a delivery drained from Pending, or a command being executed.
public abstract record ReactionContext;

public sealed record DeliveryReaction(SignalDelivery Delivery) : ReactionContext;

// ScheduledWork is empty until execute calls Schedule and grows with each Schedule, in that order.
public sealed record CommandReaction(CommandId Command, IReadOnlyList<SignalId> ScheduledWork) : ReactionContext;
