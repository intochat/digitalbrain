namespace DigitalBrain;

// The Core refusal signal (§4 step 10): a receiver whose catalog binds no exact handler
// journals the reception as terminal-unhandled and throws this back; the sender's drain
// classifies it terminal on attempt 1 and journals DeliveryFailed. It crosses the wire
// through Orleans' exception codec — Core is on every silo, so the TYPE arrives intact;
// only the message is wire-guaranteed, the properties serve same-process callers.
internal sealed class UnhandledFactException(string factKind, NeuronId receiver)
    : Exception($"{receiver} binds no exact handler for '{factKind}'; the delivery is terminal on the first attempt.")
{
    public string FactKind { get; } = factKind;

    public NeuronId Receiver { get; } = receiver;
}
