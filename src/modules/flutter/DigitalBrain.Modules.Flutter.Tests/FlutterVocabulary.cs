using System.Reflection;
using DigitalBrain.Abstractions;
using Xunit;

namespace DigitalBrain.Flutter.Tests;

public sealed class FlutterVocabulary
{
    private static readonly string FlutterNamespace =
        typeof(IShell).Namespace
        ?? throw new InvalidOperationException($"{nameof(IShell)} has no namespace.");

    [Fact(DisplayName =
        "Flutter.Contracts public vocabulary is the first-five shell/scene surface only — IFlutter remains absent")]
    public void PublicVocabularyIsFirstFiveOnly()
    {
        var contracts = typeof(IShell).Assembly;

        var vocabulary = contracts
            .GetExportedTypes()
            .Where(type => type.Namespace == FlutterNamespace)
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(ControlActivated),
                nameof(IScene),
                nameof(IShell),
                nameof(OpenScene),
                nameof(SceneOpened),
            ],
            vocabulary);

        Assert.Null(contracts.GetType($"{FlutterNamespace}.IFlutter"));
        Assert.Null(contracts.GetType($"DigitalBrain.UI.{nameof(IShell)}"));
        Assert.DoesNotContain(
            contracts.GetExportedTypes(),
            type => type.Name is "IFlutter" or "Widget" or "BuildContext" or "ShellNeuron" or "SceneNeuron");
    }

    [Fact(DisplayName =
        "IShell.Open is unsuffixed, aliased, and takes OpenScene — IScene carries no methods")]
    public void ShellAndSceneMethodShapes()
    {
        var shellMethods = typeof(IShell)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.Equal([nameof(IShell.Open)], shellMethods.Select(method => method.Name));
        Assert.All(shellMethods, method =>
        {
            Assert.DoesNotContain("Async", method.Name, StringComparison.Ordinal);
            Assert.Equal(method.Name, method.GetCustomAttribute<AliasAttribute>()?.Alias);
            Assert.Equal(typeof(Task), method.ReturnType);
        });

        var open = shellMethods.Single();
        Assert.Equal([typeof(OpenScene)], open.GetParameters().Select(parameter => parameter.ParameterType));

        Assert.Contains(typeof(INeuron), typeof(IShell).GetInterfaces());
        Assert.Contains(typeof(INeuron), typeof(IScene).GetInterfaces());

        var sceneMethods = typeof(IScene)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Empty(sceneMethods);
    }

    [Fact(DisplayName =
        "OpenScene, SceneOpened, and ControlActivated carry flutter.* wire aliases and field vocabulary")]
    public void SynapseWireAliasesAndFields()
    {
        Assert.Equal(
            "flutter.open-scene",
            typeof(OpenScene)
                .GetCustomAttributes<AliasAttribute>(inherit: false)
                .Select(attribute => attribute.Alias)
                .Single());
        Assert.Equal(
            "flutter.scene-opened",
            typeof(SceneOpened)
                .GetCustomAttributes<AliasAttribute>(inherit: false)
                .Select(attribute => attribute.Alias)
                .Single());
        Assert.Equal(
            "flutter.control-activated",
            typeof(ControlActivated)
                .GetCustomAttributes<AliasAttribute>(inherit: false)
                .Select(attribute => attribute.Alias)
                .Single());

        Assert.Equal(
            [
                nameof(OpenScene.CommandId),
                nameof(OpenScene.SceneKey),
                nameof(OpenScene.Title),
            ],
            PublicPropertyNames(typeof(OpenScene)));
        Assert.Equal(
            [
                nameof(SceneOpened.CommandId),
                nameof(SceneOpened.SceneKey),
                nameof(SceneOpened.Shell),
                nameof(SceneOpened.Title),
            ],
            PublicPropertyNames(typeof(SceneOpened)));
        Assert.Equal(
            [
                nameof(ControlActivated.ControlId),
                nameof(ControlActivated.Intent),
                nameof(ControlActivated.SceneKey),
            ],
            PublicPropertyNames(typeof(ControlActivated)));

        Assert.True(typeof(Synapse).IsAssignableFrom(typeof(SceneOpened)));
        Assert.True(typeof(Synapse).IsAssignableFrom(typeof(ControlActivated)));
        Assert.False(typeof(Synapse).IsAssignableFrom(typeof(OpenScene)));
    }

    [Fact(DisplayName =
        "Flutter runtime public surface is FlutterModule only — no public neurons, Dart, or host types")]
    public void RuntimePublicSurfaceIsModuleMarkerOnly()
    {
        var exported = typeof(FlutterModule).Assembly
            .GetExportedTypes()
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([nameof(FlutterModule)], exported);
        Assert.DoesNotContain(
            typeof(FlutterModule).Assembly.GetExportedTypes(),
            type => type.Name is "ShellNeuron" or "SceneNeuron" or "IFlutter"
                || type.Name.Contains("Widget", StringComparison.Ordinal)
                || type.Name.Contains("Dart", StringComparison.Ordinal)
                || type.Name.Contains("Host", StringComparison.Ordinal));
    }

    [Fact(DisplayName =
        "Contracts assembly references neither Flutter nor Dart SDKs")]
    public void ContractsReachNeitherFlutterNorDartSdks()
    {
        var references = typeof(IShell).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name!)
            .ToArray();

        Assert.DoesNotContain(
            references,
            name => name.Contains("Flutter", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Dart", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Skia", StringComparison.OrdinalIgnoreCase));
    }

    private static string[] PublicPropertyNames(Type type)
        => type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
