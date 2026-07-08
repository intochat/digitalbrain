#nullable enable
using DigitalBrain.Core;
using DigitalBrain.Kernel.Foundry;

namespace DigitalBrain.Kernel;

// Extracted from GeneratedNeuron: owns the single embodied-pack lifecycle (install/lookup/dispose) so the
// grain body only orchestrates dispatch. Embodiment itself stays delegated to IPackEmbodiment (PackAlcEmbodier).
public sealed class GeneratedPackRuntime(IServiceProvider serviceProvider, ILogger logger) : IDisposable
{
    private EmbodiedPack? _current;

    public EmbodiedPack? Current => _current;

    public void Install(NeuroPack pack)
    {
        if (string.IsNullOrWhiteSpace(pack.Code))
        {
            return;
        }

        var embodier = serviceProvider.GetService<IPackEmbodiment>();
        if (embodier is null)
        {
            logger.LogWarning("No IPackEmbodiment registered; pack '{Pack}' will use the LLM fallback.", pack.Name);
            return;
        }

        try
        {
            _current?.Dispose();
            _current = embodier.Embody(pack.Name, pack.Code);
            logger.LogInformation("GeneratedNeuron EMBODIED pack {Name}@{Ver} as real compiled C#.", pack.Name, pack.Version);
        }
        catch (PackEmbodimentException ex)
        {
            _current = null;
            logger.LogWarning(ex, "Pack '{Pack}' is not a compilable IPackBehavior; using LLM fallback on use.", pack.Name);
        }
    }

    public void Ensure(IEnumerable<Synapse> journal, string primaryKey)
    {
        if (_current is not null)
        {
            return;
        }

        logger.LogDebug("No installed pack found for generated neuron '{PrimaryKey}'.", primaryKey);
    }

    public void Dispose() => _current?.Dispose();
}
