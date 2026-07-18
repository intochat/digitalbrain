using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Abstractions.Bundles;

// A bundle is a unit of capability installed onto the substrate. Every bundle assembly contributes
// exactly one IBundle, which registers the bundle's neurons and services when the Kernel installs it.
// The Kernel owns silo-level wiring (streams, storage, the neuron runtime); a bundle only contributes
// to the service collection, so it never depends on the host.
public interface IBundle
{
    BundleId Id { get; }

    void Install(IServiceCollection services);
}
