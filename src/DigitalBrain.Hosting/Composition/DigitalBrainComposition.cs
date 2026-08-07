using System.Reflection;

namespace DigitalBrain;

public sealed class DigitalBrainComposition
{
    private readonly List<Assembly> vocabularyAssemblies = [];
    private readonly List<NeuronRegistration> neurons = [];
    private readonly List<WorkspaceServiceRegistration> workspaceServices = [];
    private readonly HashSet<Type> ingressSynapses = [];

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

    public DigitalBrainComposition RegisterIngress<TSynapse>()
        where TSynapse : Synapse
    {
        ingressSynapses.Add(typeof(TSynapse));
        return this;
    }

    public DigitalBrainComposition RegisterWorkspaceService<TService>(
        Func<WorkspaceBinding, TService> factory)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        workspaceServices.Add(new WorkspaceServiceRegistration(
            typeof(TService),
            workspace => factory(workspace)));
        return this;
    }

    internal CompositionCatalog Seal()
        => CompositionCatalog.Create(vocabularyAssemblies, neurons, workspaceServices, ingressSynapses);
}
