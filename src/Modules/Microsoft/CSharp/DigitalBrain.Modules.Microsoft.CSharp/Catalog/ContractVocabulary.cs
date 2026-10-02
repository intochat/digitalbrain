using System.Reflection;
using DigitalBrain.Contracts;

namespace DigitalBrain.Microsoft.CSharp;

internal sealed class ContractVocabulary(IEnumerable<Assembly> assemblies) : IContractVocabulary
{
    private readonly ContractVocabularyEntry[] entries = VisibleTypes(assemblies)
        .Where(t => IsNeuron(t) || t != typeof(Signal) && typeof(Signal).IsAssignableFrom(t))
        .Select(t => new ContractVocabularyEntry(t.FullName!, t.Name, IsNeuron(t) ? "neuron" : "signal", t.Assembly.GetName().Name ?? "", null))
        .OrderBy(e => e.QualifiedName, StringComparer.Ordinal).ToArray();

    public ContractVocabularyEntry[] Read() => [.. entries];
    internal static bool IsNeuron(Type t) => t.IsInterface && t != typeof(INeuron) && typeof(INeuron).IsAssignableFrom(t);
    internal static IEnumerable<Type> VisibleTypes(IEnumerable<Assembly> assemblies) => assemblies.Distinct()
        .Where(a => !PlatformAssemblyAttribute.IsPlatform(a)).SelectMany(a => a.GetExportedTypes())
        .Where(t => !PlatformOnlyAttribute.AppliesTo(t));
}
