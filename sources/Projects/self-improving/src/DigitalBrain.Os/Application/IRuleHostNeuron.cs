using DigitalBrain.Protocol;
using DigitalBrain.Protocol.Domain.Events;

namespace DigitalBrain.Os.Application;

// Minimal: rules as raw yaml text for 2.0 authoring closed loop. No InoLang dep.
public interface IRuleHostNeuron : INeuron, IHandle<BundleInstalled>
{
    Task InstallYamlRulesAsync(string bundleId, string yaml, CancellationToken ct = default);
    Task RemoveRulesAsync(string bundleId, CancellationToken ct = default);
}