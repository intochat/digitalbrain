using Brain.Contracts;
using Orleans.Runtime;

namespace Brain.Kernel;

public sealed class BrainOwnerIncomingCallFilter : IIncomingGrainCallFilter
{
    public Task Invoke(IIncomingGrainCallContext context)
    {
        if (context.Grain is Neuron)
        {
            if (RequestContext.Get(nameof(BrainOwnerId)) is not BrainOwnerId owner)
                throw new BrainException(
                    NeuronFailureKind.AuthenticationRequired,
                    "An authenticated owner is required to call a neuron.");

            var grainKey = ((IAddressable)context.Grain).GetPrimaryKeyString();
            if (!string.Equals(owner.Value, grainKey, StringComparison.Ordinal))
                throw new BrainException(
                    NeuronFailureKind.AuthorizationDenied,
                    "The authenticated owner is not authorized for this neuron.");
        }

        return context.Invoke();
    }
}
