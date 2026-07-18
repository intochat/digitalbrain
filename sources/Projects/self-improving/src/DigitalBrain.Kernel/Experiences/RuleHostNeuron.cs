using DigitalBrain.Os.Infrastructure.Orleans;
using DigitalBrain.Os.Application;
using DigitalBrain.Protocol.Domain.Events;

namespace DigitalBrain.Kernel.Experiences;

// Minimal stub for RuleHost (InoLang/RuleSet deleted). 
// Demo authoring loop will use simple surface emits from Shell for speed. Full yaml rule engine 10% later.
public class RuleHostNeuron : Neuron, IRuleHostNeuron
{
    public Task InstallYamlRulesAsync(string bundleId, string yaml, CancellationToken ct = default) => Task.CompletedTask;
    public Task RemoveRulesAsync(string bundleId, CancellationToken ct = default) => Task.CompletedTask;

    public Task HandleAsync(BundleInstalled synapse, CancellationToken ct = default) => Task.CompletedTask;
}
