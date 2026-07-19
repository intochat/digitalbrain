using System.Reflection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class PackableSurfaceContracts
{
    public static TheoryData<string> PackableAssemblyNames { get; } = new(PackableProjects.Names);

    [Theory]
    [MemberData(nameof(PackableAssemblyNames))]
    public void EveryPublicConcreteReferenceTypeIsSealed(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);

        var unsealedPublicTypes = assembly.GetExportedTypes()
            .Where(type => type.IsClass && !type.IsAbstract && !type.IsSealed)
            .Select(type => type.FullName)
            .ToList();

        Assert.Empty(unsealedPublicTypes);
    }
}
