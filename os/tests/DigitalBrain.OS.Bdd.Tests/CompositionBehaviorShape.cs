using System.Reflection;
using DigitalBrain.Client;
using DigitalBrain.Compositions;
using Xunit;

namespace DigitalBrain.OS.Bdd.Tests;

public sealed class CompositionBehaviorShape
{
    private static readonly Type[] Compositions =
        [.. typeof(OpenHome).Assembly
            .GetExportedTypes()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];

    [Fact(DisplayName =
        "each pre-rail composition is a public sealed class (future Behavior identity)")]
    public void EachCompositionIsPublicSealedClass()
    {
        Assert.NotEmpty(Compositions);

        Assert.All(Compositions, static type =>
        {
            Assert.True(type is { IsClass: true, IsSealed: true, IsAbstract: false, IsNested: false });
            Assert.Empty(type.GetNestedTypes(BindingFlags.Public));
        });
    }

    [Fact(DisplayName =
        "composition type surfaces never wire peer compositions — only IDigitalBrain + contracts")]
    public void CompositionSurfacesNeverWirePeerCompositions()
    {
        Assert.NotEmpty(Compositions);
        var peers = Compositions.ToHashSet();

        Assert.All(Compositions, composition =>
        {
            Assert.Empty(PeerTypesOnSurface(composition, peers));

            var entryPoints = composition
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(static method => !method.IsSpecialName)
                .ToArray();

            Assert.NotEmpty(entryPoints);
            Assert.All(entryPoints, static method =>
            {
                Assert.Contains(
                    method.GetParameters(),
                    static parameter => parameter.ParameterType == typeof(IDigitalBrain));
            });
        });
    }

    private static IEnumerable<Type> PeerTypesOnSurface(Type composition, HashSet<Type> peers)
    {
        const BindingFlags flags =
            BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.DeclaredOnly;

        return composition.GetConstructors(flags)
            .SelectMany(static constructor => constructor.GetParameters())
            .Select(static parameter => parameter.ParameterType)
            .Concat(
                composition.GetMethods(flags)
                    .Where(static method => !method.IsSpecialName)
                    .SelectMany(static method => method.GetParameters())
                    .Select(static parameter => parameter.ParameterType))
            .Concat(composition.GetFields(flags).Select(static field => field.FieldType))
            .Concat(composition.GetProperties(flags).Select(static property => property.PropertyType))
            .Where(type => peers.Contains(type) && type != composition);
    }
}
