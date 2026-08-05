namespace DigitalBrain;

internal sealed class UnhandledFactException(string factKind, NeuronId receiver)
    : Exception($"{receiver} binds no exact handler for '{factKind}'; the delivery is terminal on the first attempt.");
