namespace DigitalBrain.Testing;

public sealed class DigitalBrainTestBuilder
{
    private readonly List<Type> moduleTypes = [];
    private readonly List<(Type Contract, object Instance)> services = [];
    private bool sealed_;

    public DigitalBrainTestBuilder AddModule<TNeuron>()
        where TNeuron : Neuron
    {
        RefuseSealed();
        if (!moduleTypes.Contains(typeof(TNeuron)))
        {
            moduleTypes.Add(typeof(TNeuron));
        }

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
        return new TestComposition([.. moduleTypes], [.. services]);
    }

    private void RefuseSealed()
    {
        if (sealed_)
        {
            throw new InvalidOperationException("The DigitalBrain test composition is already sealed.");
        }
    }
}

internal sealed record TestComposition(
    IReadOnlyList<Type> ModuleTypes,
    IReadOnlyList<(Type Contract, object Instance)> Services)
{
    internal string Fingerprint()
    {
        var modules = ModuleTypes
            .Select(module => module.FullName ?? module.Name)
            .Order(StringComparer.Ordinal);
        var serviceRows = Services
            .Select(service => $"{service.Contract.FullName}={service.Instance.GetType().FullName}")
            .Order(StringComparer.Ordinal);

        return string.Join('|', [.. modules, .. serviceRows]);
    }
}
