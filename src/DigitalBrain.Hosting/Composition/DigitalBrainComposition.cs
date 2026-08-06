using System.Reflection;

namespace DigitalBrain;

public sealed class DigitalBrainComposition
{
    private readonly List<Assembly> vocabularyAssemblies = [];
    private readonly List<NeuronRegistration> neurons = [];

    public DigitalBrainComposition RegisterVocabulary(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        if (!vocabularyAssemblies.Contains(assembly))
        {
            vocabularyAssemblies.Add(assembly);
        }

        return this;
    }

    public DigitalBrainComposition RegisterNeuron<TBehavior>(string kind)
        where TBehavior : Neuron
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        neurons.Add(new NeuronRegistration(typeof(TBehavior), kind));
        return this;
    }

    internal CompositionCatalog Seal()
        => CompositionCatalog.Create(vocabularyAssemblies, neurons);
}
