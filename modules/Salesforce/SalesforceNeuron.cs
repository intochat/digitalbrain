using Brain.Kernel;
using Salesforce.Contracts;

namespace Salesforce;

public sealed class SalesforceNeuron([NeuronState] NeuronDurableState durableState)
    : Neuron(durableState), ISalesforce;
