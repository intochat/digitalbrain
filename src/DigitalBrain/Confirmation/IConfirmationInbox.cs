using DigitalBrain.Confirmation;
using DigitalBrain.Contracts;

namespace DigitalBrain.Confirmation;

/// <summary>
/// The confirmations neuron interface for managing pending confirmations.
/// </summary>
[NeuronAlias("confirmations"), DefaultNeuronType("runtime.confirmations")]
public interface IConfirmationInbox
{
    /// <summary>Canonical key of the confirmations inbox.</summary>
    static string Key(string brain) => brain + "/confirmations";

    /// <summary>Reads all pending confirmations for this brain.</summary>
    Task<ConfirmationInbox> Read();

    /// <summary>Resolves a confirmation plan by id.</summary>
    Task Resolve(string planId, bool approve);
}

/// <summary>Pending confirmation inbox state snapshot.</summary>
public sealed record ConfirmationInbox(long Revision, ConfirmationRequested[] Pending);
