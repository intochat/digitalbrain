using DigitalBrain.Registry;

namespace DigitalBrain.Microsoft.CSharp;

internal sealed class CSharpRegistryResources : IRegistryResourceProvider
{
    public string Id => "csharp";
    public RegistryCapability Summary => new(Id, "Create and run C# behaviors using installed neuron contracts.", []);
    public Task<RegistryDiscovery> Discover(CancellationToken cancellationToken) => Task.FromResult(new RegistryDiscovery(
        [new("csharp", "Create C# behavior using installed neuron contracts. Start with csharp_contracts(modules: []), then select returned module IDs. Save complete source with csharp_write, run with csharp_run, and inspect logs before claiming success. csharp_arm installs a signal trigger.",
            ["csharp_contracts", "csharp_write", "csharp_run", "csharp_arm"])], []));
}
