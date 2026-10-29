using DigitalBrain.Confirmation;

namespace DigitalBrain.Runtime.Orleans.Operations;

/// <summary>Host-bound confirmation contexts for an authoritative brain and the neuron requesting an operation.</summary>
public interface IConfirmationContexts
{
    /// <summary>The requester describes provenance; it never grants authority to approve a plan.</summary>
    IConfirmations For(BrainId brain, string requester);
}
