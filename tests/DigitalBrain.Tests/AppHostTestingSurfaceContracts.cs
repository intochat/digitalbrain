using System.Reflection;
using Aspire.Hosting;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class AppHostTestingSurfaceContracts
{
    [Fact]
    public void L2SurfaceNamesTheFixtureGraphAndResource()
    {
        var exported = typeof(DigitalBrainFixture).Assembly
            .GetExportedTypes()
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("DigitalBrainAppHostFixture`1", exported);
        Assert.Contains(nameof(RunningAppHost), exported);
        Assert.Contains(nameof(HostedResource), exported);
        Assert.DoesNotContain("HostedApplication", exported);
        Assert.DoesNotContain("HostedScenario", exported);
    }

    [Fact]
    public void PublicL2MembersDoNotLeakAspireRuntimeObjects()
    {
        var exposed = typeof(RunningAppHost).Assembly
            .GetExportedTypes()
            .SelectMany(type => type.GetMembers(BindingFlags.Instance | BindingFlags.Public))
            .SelectMany(MemberTypes)
            .SelectMany(Expand)
            .Where(type => type.FullName?.StartsWith(
                "Aspire.Hosting.",
                StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Empty(exposed);
    }

    private static IEnumerable<Type> MemberTypes(MemberInfo member) => member switch
    {
        MethodInfo method => method.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .Append(method.ReturnType),
        PropertyInfo property => [property.PropertyType],
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
