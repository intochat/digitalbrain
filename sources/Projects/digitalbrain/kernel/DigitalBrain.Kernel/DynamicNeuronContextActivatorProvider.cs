using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.Kernel;

public sealed class DynamicNeuronContextActivatorProvider : IGrainContextActivatorProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEnumerable<IGrainContextActivatorProvider> _providers;
    private IGrainContextActivator? _baseActivator;
    private IInterpretedNeuronRegistry? _registry;

    public DynamicNeuronContextActivatorProvider(IServiceProvider serviceProvider, IEnumerable<IGrainContextActivatorProvider> providers)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
    }

    private IInterpretedNeuronRegistry Registry =>
        _registry ??= _serviceProvider.GetRequiredService<IInterpretedNeuronRegistry>();

    public bool TryGet(GrainType grainType, out IGrainContextActivator activator)
    {
        var fqn = grainType.ToString() ?? "";
        
        if (IsDynamicNeuron(fqn))
        {
            if (_baseActivator == null)
            {
                var baseGrainType = GrainType.Create("DynamicNeuronGrain");
                foreach (var provider in _providers)
                {
                    // Prevent infinite recursion by skipping ourself
                    if (provider != this && provider.TryGet(baseGrainType, out _baseActivator))
                    {
                        break;
                    }
                }
            }

            if (_baseActivator != null)
            {
                activator = _baseActivator;
                return true;
            }
        }

        // Delegate to original providers for all non-dynamic grains
        foreach (var provider in _providers)
        {
            if (provider != this && provider.TryGet(grainType, out activator))
            {
                return true;
            }
        }

        activator = null!;
        return false;
    }

    private bool IsDynamicNeuron(string fqn)
    {
        if (fqn.StartsWith("Dynamic.Test", StringComparison.Ordinal)) return true;
        if (Registry.RegisteredFqns.Contains(fqn)) return true;
        
        // Spec files registered at boot
        if (fqn == "DigitalBrain.Developer.Specs.CodeReviewerFlows" ||
            fqn == "DigitalBrain.Developer.Specs.SoftwareDeveloperFlows" ||
            fqn == "DigitalBrain.Developer.Specs.FileAndDirectoryFlows" ||
            fqn == "DigitalBrain.SDK.Aspire.Runtime.Specs.SelfHealing" ||
            fqn == "DigitalBrain.Developer.Specs.GitHubFlows" ||
            fqn == "DigitalBrain.Kernel.Settings.SettingsNeuron") return true;

        return false;
    }
}
