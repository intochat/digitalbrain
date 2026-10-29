using DigitalBrain.Capabilities;

namespace DigitalBrain;

/// <summary>
/// What a source's <c>AdmitSynapse</c> decides on (04-19): the subscriber, the signal types it asks for and the trusted caller
/// stamp of the call that asked. Only a runtime builds it; it takes <see cref="Caller"/> from the stamp the host put on the call
/// (<c>RuntimeCallerContext</c> on Orleans, <c>TestOS.Caller</c> under test), never from anything the subscriber sends.
/// </summary>
/// <param name="Subscriber">The neuron that will hold the synapse.</param>
/// <param name="Signals">The signal types it asks for.</param>
/// <param name="Caller">The stamped caller (principal, account, brain, app as module), or null when the call carries no stamp.</param>
public sealed record SynapseRequest(NeuronId Subscriber, IReadOnlyList<Type> Signals, CapabilityCaller? Caller);
