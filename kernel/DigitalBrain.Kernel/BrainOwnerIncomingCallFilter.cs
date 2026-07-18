using DigitalBrain;
using Orleans.Runtime;
using Orleans.Streams;

namespace DigitalBrain.Kernel;

public sealed class BrainOwnerIncomingCallFilter : IIncomingGrainCallFilter
{
    public Task Invoke(IIncomingGrainCallContext context)
    {
        if (TryGetNotificationSubscription(context, out var streamId))
            AuthorizeNotificationSubscription(streamId);

        if (context.Grain is Neuron)
        {
            if (IsOrleansSystemReminder(context))
                return context.Invoke();

            if (RequestContext.Get(nameof(BrainOwnerId)) is not BrainOwnerId owner)
                throw new BrainException(
                    NeuronFailureKind.AuthenticationRequired,
                    "An authenticated owner is required to call a neuron.");

            var grainKey = ((IAddressable)context.Grain).GetPrimaryKeyString();
            if (context.Grain is IConversationGrain)
            {
                if (!ConversationKey.TryParse(grainKey, out var keyOwner, out _) || keyOwner != owner)
                    throw new BrainException(
                        NeuronFailureKind.AuthorizationDenied,
                        "The authenticated owner is not authorized for this conversation.");
            }
            else if (!string.Equals(owner.Value, grainKey, StringComparison.Ordinal))
            {
                throw new BrainException(
                    NeuronFailureKind.AuthorizationDenied,
                    "The authenticated owner is not authorized for this neuron.");
            }
        }

        return context.Invoke();
    }

    private static void AuthorizeNotificationSubscription(QualifiedStreamId streamId)
    {
        if (RequestContext.Get(nameof(BrainOwnerId)) is not BrainOwnerId owner)
            throw new BrainException(
                NeuronFailureKind.AuthenticationRequired,
                "An authenticated owner is required to subscribe to neuron notifications.");

        var key = streamId.StreamId.GetKeyAsString();
        var authorized = ConversationKey.TryParse(key, out var keyOwner, out _)
            ? keyOwner == owner
            : string.Equals(owner.Value, key, StringComparison.Ordinal);
        if (!authorized)
            throw new BrainException(
                NeuronFailureKind.AuthorizationDenied,
                "The authenticated owner is not authorized for this notification stream.");
    }

    private static bool TryGetNotificationSubscription(
        IIncomingGrainCallContext context,
        out QualifiedStreamId streamId)
    {
        streamId = default;
        if (context.InterfaceMethod.Name is not (
                nameof(IStreamPubSub.ConsumerCount) or
                nameof(IStreamPubSub.FaultSubscription) or
                nameof(IStreamPubSub.GetAllSubscriptions) or
                nameof(IStreamPubSub.RegisterConsumer) or
                nameof(IStreamPubSub.UnregisterConsumer)))
            return false;

        for (var index = 0; index < context.Request.GetArgumentCount(); index++)
        {
            if (context.Request.GetArgument(index) is not QualifiedStreamId candidate ||
                !string.Equals(
                    candidate.ProviderName,
                    NeuronNotificationPublisher.StreamProviderName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    candidate.StreamId.GetNamespace(),
                    NeuronNotificationPublisher.StreamNamespace,
                    StringComparison.Ordinal))
                continue;

            streamId = candidate;
            return true;
        }

        return false;
    }

    private static bool IsOrleansSystemReminder(IIncomingGrainCallContext context) =>
        context.SourceId is { } sourceId
        && sourceId.IsSystemTarget()
        && context.InterfaceMethod.DeclaringType == typeof(IRemindable)
        && context.InterfaceMethod.Name == nameof(IRemindable.ReceiveReminder);
}
