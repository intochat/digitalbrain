using System.Reflection;

namespace DigitalBrain.Testing;

public sealed class DigitalBrainTestBuilder
{
    private readonly List<Action<DigitalBrainComposition>> registrations = [];
    private readonly List<string> registrationRows = [];
    private readonly List<(Type Contract, object Instance)> services = [];
    private bool sealed_;

    public DigitalBrainTestBuilder RegisterVocabulary(Assembly assembly)
    {
        RefuseSealed();
        ArgumentNullException.ThrowIfNull(assembly);
        registrations.Add(composition => composition.RegisterVocabulary(assembly));
        registrationRows.Add($"vocabulary={assembly.FullName}");
        return this;
    }

    public DigitalBrainTestBuilder RegisterNeuron<TBehavior>(string kind)
        where TBehavior : Neuron
    {
        RefuseSealed();
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        registrations.Add(composition => composition.RegisterNeuron<TBehavior>(kind));
        registrationRows.Add($"neuron={typeof(TBehavior).FullName}:{kind}");
        return this;
    }

    public DigitalBrainTestBuilder RegisterIngress<TSynapse>()
        where TSynapse : Synapse
    {
        RefuseSealed();
        registrations.Add(composition => composition.RegisterIngress<TSynapse>());
        registrationRows.Add($"ingress={typeof(TSynapse).FullName}");
        return this;
    }

    public DigitalBrainTestBuilder RegisterWorkspaceService<TService>(
        Func<WorkspaceBinding, TService> factory)
        where TService : class
    {
        RefuseSealed();
        ArgumentNullException.ThrowIfNull(factory);
        registrations.Add(composition => composition.RegisterWorkspaceService(factory));
        registrationRows.Add(
            $"workspace-service={typeof(TService).FullName}:{factory.Method.DeclaringType?.FullName}.{factory.Method.Name}");
        return this;
    }

    public DigitalBrainTestBuilder AddService<TService>(TService instance)
        where TService : class
    {
        RefuseSealed();
        ArgumentNullException.ThrowIfNull(instance);
        services.Add((typeof(TService), instance));
        return this;
    }

    internal TestComposition Seal()
    {
        sealed_ = true;
        return new TestComposition([.. registrations], [.. registrationRows], [.. services]);
    }

    private void RefuseSealed()
    {
        if (sealed_)
        {
            throw new InvalidOperationException("The DigitalBrain test composition is already sealed.");
        }
    }
}
