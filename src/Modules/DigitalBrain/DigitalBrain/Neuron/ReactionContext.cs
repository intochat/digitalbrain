using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

public abstract record ReactionContext;

public sealed record DeliveryReaction(SignalDelivery Delivery) : ReactionContext;
