using OrleansSdk = Orleans;

namespace DigitalBrain.Runtime.Orleans.Hosting;

/// <summary>
/// The silo's answer to a process that has contracts but not the neuron classes: which host grain serves a neuron type.
/// Keyed <c>/</c>. The kernel grain that knows the composed modules implements it.
/// </summary>
public interface INeuronHostCatalog : OrleansSdk.IGrainWithStringKey
{
    /// <summary>The host grain type (<c>neuron.host</c>, <c>neuron.host.reentrant</c> or <c>neuron.host.stateless</c>), or null when the type is not hosted.</summary>
    Task<string?> HostType(string neuronType);
}
