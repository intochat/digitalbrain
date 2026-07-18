using DigitalBrain.Kernel;
using Google.Contracts;

namespace Google;

public sealed class GmailNeuron([NeuronState] NeuronDurableState durableState)
    : Neuron(durableState), IGmail;
