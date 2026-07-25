using System.Reflection;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Aspire.Hosting;
using DigitalBrain.Kernel;
using DigitalBrain.Tests.Boundary;
using DigitalBrain.Tests.Packages;
using Xunit;

namespace DigitalBrain.Tests.Flutter;

public sealed class FlutterContracts
{
    private static readonly string FlutterNamespace =
        typeof(IShell).Namespace
        ?? throw new InvalidOperationException($"{nameof(IShell)} has no namespace.");

    private static readonly string FlutterRuntime =
        typeof(FlutterModule).Assembly.GetName().Name
        ?? throw new InvalidOperationException($"{nameof(FlutterModule)} assembly has no name.");

    private static readonly string FlutterContractsPackage =
        typeof(IShell).Assembly.GetName().Name
        ?? throw new InvalidOperationException($"{nameof(IShell)} assembly has no name.");

    private static readonly string FlutterHostingPackage =
        typeof(FlutterHostingExtensions).Assembly.GetName().Name
        ?? throw new InvalidOperationException($"{nameof(FlutterHostingExtensions)} assembly has no name.");

    private static readonly string Kernel =
        typeof(Neuron).Assembly.GetName().Name
        ?? throw new InvalidOperationException($"{nameof(Neuron)} assembly has no name.");

    private static readonly string Abstractions =
        typeof(NeuronId).Assembly.GetName().Name
        ?? throw new InvalidOperationException($"{nameof(NeuronId)} assembly has no name.");

    private static readonly string AspireHosting =
        typeof(DigitalBrain.Aspire.Hosting.DigitalBrainBuilder).Assembly.GetName().Name
        ?? throw new InvalidOperationException("DigitalBrain.Aspire.Hosting assembly has no name.");

    [Fact(DisplayName =
        "Flutter.Contracts public vocabulary is first-five surface only — no IFlutter god")]
    public void PublicVocabularyIsFirstVerticalSurfaceOnly()
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

        Assert.Null(contracts.GetType($"DigitalBrain.UI.{nameof(IShell)}"));
        Assert.Null(contracts.GetType($"{FlutterNamespace}.IFlutter"));
        Assert.DoesNotContain(
            contracts.GetExportedTypes().Concat(typeof(FlutterModule).Assembly.GetExportedTypes()),
            type => type.Name is "IFlutter" or "Flutter" or "IUiRoot" or "IUiGateway" or "AutoHost");
    }

    [Fact(DisplayName =
        "IShell.Open is unsuffixed, aliased, and takes OpenScene only — IScene is surface key only")]
    public void ShellAndSceneMethodsMatchFirstVerticalWire()
    {
        var shellMethods = typeof(IShell)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.Equal([nameof(IShell.Open)], shellMethods.Select(method => method.Name));
        Assert.All(shellMethods, method =>
        {
            Assert.DoesNotContain("Async", method.Name, StringComparison.Ordinal);
            Assert.Equal(method.Name, method.GetCustomAttribute<AliasAttribute>()?.Alias);
            Assert.Equal(typeof(Task), method.ReturnType);
            Assert.Equal(
                [typeof(OpenScene)],
                method.GetParameters().Select(parameter => parameter.ParameterType));
        });

        Assert.DoesNotContain(
            typeof(IScene)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            method => !method.IsSpecialName);
        Assert.Contains(typeof(INeuron), typeof(IShell).GetInterfaces());
        Assert.Contains(typeof(INeuron), typeof(IScene).GetInterfaces());
    }

    [Fact(DisplayName = "flutter-wire-contracts.golden.json matches Contracts assembly wire shape")]
    public void WireContractGoldenMatchesContractsAssembly()
    {
        var actual = ExtractWireManifest(typeof(IShell).Assembly);
        var goldenPath = Path.Combine(
            PackageBoundarySupport.RepositoryRoot,
            RepositoryLayout.Modules,
            PackageInventory.ModulesFlutterContracts,
            "flutter-wire-contracts.golden.json");
        Assert.True(File.Exists(goldenPath));

        var expected = JsonNode.Parse(File.ReadAllText(goldenPath))!;
        Assert.True(JsonNode.DeepEquals(expected, actual));
    }

    [Fact(DisplayName =
        "Flutter.Contracts is Abstractions-only and reaches neither Dart/Flutter SDKs nor Ui/hosting")]
    public void ContractsCompileGraphIsAbstractionsOnlyAndSdkFree()
    {
        Assert.Equal(
            [Abstractions],
            PackageBoundarySupport.DirectCompileProjectReferencesOf(FlutterContractsPackage)
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            [Abstractions],
            PackageBoundarySupport.CompileProjectsReachableFrom(FlutterContractsPackage)
                .Order(StringComparer.Ordinal));

        Assert.Empty(PackageBoundarySupport.DirectPackageReferencesOf(FlutterContractsPackage));
        Assert.DoesNotContain(
            PackageBoundarySupport.PackagesReachableFrom(FlutterContractsPackage),
            PackageBoundarySupport.IsDartOrFlutterSdkPackage);

        var projects = PackageBoundarySupport.CompileProjectsReachableFrom(FlutterContractsPackage);
        Assert.DoesNotContain(projects, IsForbiddenFlutterContractsProject);
    }

    [Fact(DisplayName =
        "Flutter runtime public surface is FlutterModule only — ShellNeuron/SceneNeuron stay internal")]
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
            type => type.Name is "ShellNeuron" or "SceneNeuron" or "IFlutter" or "IUiGateway");
    }

    [Fact(DisplayName =
        "Flutter runtime compile graph is Kernel + Flutter.Contracts — never Ui, Client, or peer modules")]
    public void RuntimeCompileGraphIsKernelAndContractsOnly()
    {
        Assert.Equal(
            new[] { Kernel, FlutterContractsPackage }.Order(StringComparer.Ordinal),
            PackageBoundarySupport.DirectCompileProjectReferencesOf(FlutterRuntime)
                .Order(StringComparer.Ordinal));

        Assert.Equal(
            new[] { Abstractions, Kernel, FlutterContractsPackage }.Order(StringComparer.Ordinal),
            PackageBoundarySupport.CompileProjectsReachableFrom(FlutterRuntime)
                .Order(StringComparer.Ordinal));

        var projects = PackageBoundarySupport.CompileProjectsReachableFrom(FlutterRuntime);
        Assert.DoesNotContain(projects, IsForbiddenFlutterRuntimeProject);
        Assert.DoesNotContain(
            PackageBoundarySupport.PackagesReachableFrom(FlutterRuntime),
            PackageBoundarySupport.IsDartOrFlutterSdkPackage);
    }

    [Fact(DisplayName =
        "Flutter.Aspire.Hosting public surface is projection API only — Desktop/Headless, no Auto")]
    public void HostingPublicSurfaceIsProjectionApiOnly()
    {
        var hostingNamespace = typeof(FlutterHostingExtensions).Namespace;
        var exported = typeof(FlutterHostingExtensions).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace == hostingNamespace)
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(DesktopHost),
                nameof(FlutterHostOptions),
                nameof(FlutterHostingExtensions),
                nameof(FlutterUiEdgeOptions),
                nameof(HeadlessHost),
            ],
            exported);

        Assert.DoesNotContain(
            typeof(FlutterHostingExtensions).Assembly.GetExportedTypes(),
            type => type.Name is "AutoHost" or "FlutterHostLaunch" or "FlutterHostKind" or "IFlutter");
    }

    [Fact(DisplayName =
        "Flutter.Aspire.Hosting compile graph is Aspire.Hosting + Flutter runtime — projects Ui, never hand-wires host logic")]
    public void HostingCompileGraphIsAspireHostingAndFlutterRuntime()
    {
        Assert.Equal(
            new[] { AspireHosting, FlutterRuntime }.Order(StringComparer.Ordinal),
            PackageBoundarySupport.DirectCompileProjectReferencesOf(FlutterHostingPackage)
                .Order(StringComparer.Ordinal));

        Assert.Contains(
            FlutterContractsPackage,
            PackageBoundarySupport.CompileProjectsReachableFrom(FlutterHostingPackage));
        Assert.DoesNotContain(
            PackageInventory.Ui,
            PackageBoundarySupport.DirectCompileProjectReferencesOf(FlutterHostingPackage),
            StringComparer.Ordinal);
        Assert.DoesNotContain(
            PackageBoundarySupport.PackagesReachableFrom(FlutterHostingPackage),
            PackageBoundarySupport.IsDartOrFlutterSdkPackage);
    }

    [Fact(DisplayName =
        "northbound Ui host is client + Flutter.Contracts only — never Flutter runtime, Kernel, or hosting")]
    public void NorthboundUiHostOwnsEdgeOverContractsNotRuntime()
    {
        Assert.Equal(
            new[]
            {
                PackageInventory.Aspire,
                PackageInventory.Client,
                FlutterContractsPackage,
            }.Order(StringComparer.Ordinal),
            PackageBoundarySupport.DirectCompileProjectReferencesOf(PackageInventory.Ui)
                .Order(StringComparer.Ordinal));

        var reachable = PackageBoundarySupport.CompileProjectsReachableFrom(PackageInventory.Ui);
        Assert.Contains(FlutterContractsPackage, reachable);
        Assert.DoesNotContain(FlutterRuntime, reachable, StringComparer.Ordinal);
        Assert.DoesNotContain(FlutterHostingPackage, reachable, StringComparer.Ordinal);
        Assert.DoesNotContain(Kernel, reachable, StringComparer.Ordinal);
        Assert.DoesNotContain(
            reachable,
            project => project.StartsWith(PackageInventory.ModulesAi, StringComparison.Ordinal)
                || project.StartsWith(PackageInventory.ModulesGoogle, StringComparison.Ordinal)
                || project.StartsWith(PackageInventory.ModulesSalesforce, StringComparison.Ordinal)
                || project.StartsWith(PackageInventory.IntegrationsPrefix, StringComparison.Ordinal));
    }

    private static bool IsForbiddenFlutterContractsProject(string project) =>
        project is PackageInventory.Kernel
            or PackageInventory.Client
            or PackageInventory.Ui
            || project.StartsWith(PackageInventory.ModulesFlutter, StringComparison.Ordinal)
                && !string.Equals(project, FlutterContractsPackage, StringComparison.Ordinal)
            || project.StartsWith(PackageInventory.ModulesAi, StringComparison.Ordinal)
            || project.StartsWith(PackageInventory.ModulesGoogle, StringComparison.Ordinal)
            || project.StartsWith(PackageInventory.ModulesSalesforce, StringComparison.Ordinal)
            || project.StartsWith(PackageInventory.IntegrationsPrefix, StringComparison.Ordinal)
            || PackageInventory.IsAspireFamilyProject(project);

    private static bool IsForbiddenFlutterRuntimeProject(string project) =>
        project is PackageInventory.Client
            or PackageInventory.Ui
            || project.StartsWith(PackageInventory.ModulesAi, StringComparison.Ordinal)
            || project.StartsWith(PackageInventory.ModulesTasks, StringComparison.Ordinal)
            || project.StartsWith(PackageInventory.ModulesTime, StringComparison.Ordinal)
            || project.StartsWith(PackageInventory.ModulesGoogle, StringComparison.Ordinal)
            || project.StartsWith(PackageInventory.ModulesSalesforce, StringComparison.Ordinal)
            || project.StartsWith(PackageInventory.IntegrationsPrefix, StringComparison.Ordinal)
            || PackageInventory.IsAspireFamilyProject(project)
            || string.Equals(project, FlutterHostingPackage, StringComparison.Ordinal);

    private static JsonObject ExtractWireManifest(Assembly assembly)
    {
        var types = assembly
            .GetExportedTypes()
            .Where(type => type.Namespace == FlutterNamespace)
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .Select(DescribeType)
            .ToArray();

        return new JsonObject
        {
            ["version"] = 1,
            ["namespace"] = FlutterNamespace,
            ["types"] = new JsonArray(types.Select(node => (JsonNode)node).ToArray()),
        };
    }

    private static JsonObject DescribeType(Type type)
    {
        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod is { IsPublic: true, IsStatic: false })
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => new JsonObject
            {
                ["name"] = property.Name,
                ["type"] = TypeDisplayName(property.PropertyType),
            })
            .ToArray();

        var methods = type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(IsWireMethod)
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .Select(method => new JsonObject
            {
                ["name"] = method.Name,
                ["alias"] = FirstAlias(method),
                ["parameters"] = new JsonArray(
                    method.GetParameters()
                        .Select(parameter => (JsonNode)new JsonObject
                        {
                            ["name"] = parameter.Name,
                            ["type"] = TypeDisplayName(parameter.ParameterType),
                        })
                        .ToArray()),
                ["returnType"] = TypeDisplayName(method.ReturnType),
            })
            .ToArray();

        return new JsonObject
        {
            ["name"] = type.Name,
            ["kind"] = type.IsInterface ? "interface" : "record",
            ["alias"] = FirstAlias(type),
            ["properties"] = new JsonArray(properties.Select(node => (JsonNode)node).ToArray()),
            ["methods"] = new JsonArray(methods.Select(node => (JsonNode)node).ToArray()),
        };
    }

    private static string? FirstAlias(MemberInfo member)
        => member
            .GetCustomAttributes<AliasAttribute>(inherit: false)
            .Select(attribute => attribute.Alias)
            .Distinct(StringComparer.Ordinal)
            .SingleOrDefault();

    private static bool IsWireMethod(MethodInfo method)
        => !method.IsSpecialName
            && method.Name is not (
                nameof(object.Equals)
                or nameof(object.GetHashCode)
                or nameof(object.ToString)
                or "Deconstruct"
                or "<Clone>$");

    private static string TypeDisplayName(Type type)
    {
        if (type == typeof(string))
        {
            return nameof(String);
        }

        if (type == typeof(Task))
        {
            return nameof(Task);
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return $"{nameof(Task)}<{TypeDisplayName(type.GetGenericArguments()[0])}>";
        }

        return type.Name;
    }
}
