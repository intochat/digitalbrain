using System.Reflection;
using DigitalBrain.Client;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class PublicSurfaceContracts
{
    [Fact]
    public void TestingSurfaceNamesWhatItOwns()
    {
        var exported = typeof(DigitalBrainFixture).Assembly
            .GetExportedTypes()
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(nameof(DigitalBrainFixture), exported);
        Assert.Contains(nameof(DigitalBrainTestBuilder), exported);
        Assert.Contains(nameof(TestBrain), exported);
        Assert.Contains("TestOwner", exported);
        Assert.Contains("TestNeuron`1", exported);
        Assert.Contains("ObservedSynapse`1", exported);
        Assert.DoesNotContain("Simulation", exported);
        Assert.DoesNotContain("Simulations", exported);
        Assert.DoesNotContain("Scenario", exported);
        Assert.DoesNotContain("SimulationCluster", exported);
    }

    [Fact]
    public void TestBrainDoesNotImplementTheProductionClient()
        => Assert.DoesNotContain(typeof(IDigitalBrain), typeof(TestBrain).GetInterfaces());

    [Fact]
    public void NoPublicMemberLeaksOrleans()
    {
        var leaked = typeof(TestBrain).Assembly
            .GetExportedTypes()
            .SelectMany(type => type.GetMembers())
            .SelectMany(ReferencedTypes)
            .SelectMany(Expand)
            .Where(type => type.FullName?.StartsWith("Orleans.", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Empty(leaked);
    }

    private static IEnumerable<Type> ReferencedTypes(MemberInfo member) =>
        member switch
        {
            MethodInfo method => method.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType),
            ConstructorInfo constructor => constructor.GetParameters()
                .Select(parameter => parameter.ParameterType),
            PropertyInfo property => [property.PropertyType],
            FieldInfo field => [field.FieldType],
            EventInfo @event => [@event.EventHandlerType!],
            _ => [],
        };

    private static IEnumerable<Type> Expand(Type type)
    {
        yield return type;

        if (type.HasElementType)
        {
            foreach (var nested in Expand(type.GetElementType()!))
            {
                yield return nested;
            }
        }

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var nested in Expand(argument))
            {
                yield return nested;
            }
        }
    }
}
