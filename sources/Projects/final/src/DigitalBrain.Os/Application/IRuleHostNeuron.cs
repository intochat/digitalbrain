using DigitalBrain.Protocol;
using DigitalBrain.Os;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.InoLang.Domain.Ino;
using DigitalBrain.Protocol.Domain.ValueObjects.Distribution;

namespace DigitalBrain.Os.Application;

public interface IRuleHostNeuron : INeuron, IHandle<BundleInstalled>
{
    Task InstallRulesAsync(string bundleIdValue, RuleSet ruleSet, CancellationToken cancellationToken = default);
    Task RemoveRuleSetAsync(string bundleIdValue, CancellationToken cancellationToken = default);
    Task<RuleReplayReport> ReplayObservedSynapsesAsync(ExperienceManifest manifest, CancellationToken cancellationToken = default);
}