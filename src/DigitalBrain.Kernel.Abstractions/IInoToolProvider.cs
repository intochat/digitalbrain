namespace DigitalBrain.Kernel;

using Microsoft.Extensions.AI;

// Runtime registration port: an integration implements this once and is auto-discovered via DI
// (see DigitalBrainOrleansExtensions.AddIntegrations / NeuronTestKernelConfigurator). Ino enumerates
// all registered providers and never references a concrete integration project directly.
public interface IInoToolProvider
{
    IReadOnlyList<AIFunction> BuildTools(string? clientId, CancellationToken cancellationToken);
}
