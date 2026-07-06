using DigitalBrain.Core;
using DigitalBrain.Core.Distribution;
using DigitalBrain.Demo.Runtime;
using DigitalBrain.Ino;
using DigitalBrain.Marketplace.Contracts;
using System.Reflection;

namespace DigitalBrain.Tests.Architecture;

public class CoreBoundaryTests
{
    private static readonly string[] ForbiddenRuntimeHostOrIntegrationPrefixes =
    {
        "Aspire.",
        "Azure.",
        "Google.",
        "Grpc.",
        "ModelContextProtocol",
        "Qdrant.",
        "Stripe",
        "Telegram.",
        "Microsoft.AspNetCore",
        "Microsoft.CodeAnalysis",
        "Microsoft.Extensions.AI",
        "Microsoft.Extensions.Hosting"
    };

    [Fact]
    public void Core_Does_Not_Reference_Other_DigitalBrain_Assemblies()
    {
        var digitalBrainReferences = CoreReferenceNames()
            .Where(name => name.StartsWith("DigitalBrain.", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(digitalBrainReferences);
    }

    [Fact]
    public void Pack_Contracts_Depend_On_Core_Not_The_Other_Way_Around()
    {
        var coreAssemblyName = typeof(Synapse).Assembly.GetName().Name!;
        var packContractsAssemblyName = typeof(IPackBehavior).Assembly.GetName().Name!;

        Assert.Contains(coreAssemblyName, PackContractsReferenceNames());
        Assert.DoesNotContain(packContractsAssemblyName, CoreReferenceNames());
    }

    [Fact]
    public void Ui_Contracts_Depend_On_Core_Not_The_Other_Way_Around()
    {
        var coreAssemblyName = typeof(Synapse).Assembly.GetName().Name!;
        var uiContractsAssemblyName = typeof(UiSurface).Assembly.GetName().Name!;
        var references = UiContractsReferenceNames();

        Assert.Contains(coreAssemblyName, references);
        Assert.DoesNotContain(uiContractsAssemblyName, CoreReferenceNames());
    }

    [Fact]
    public void Demo_Contracts_Depend_On_Core_Not_The_Other_Way_Around()
    {
        var coreAssemblyName = typeof(Synapse).Assembly.GetName().Name!;
        var demoContractsAssemblyName = typeof(DemoMessageSynapse).Assembly.GetName().Name!;
        var references = DemoContractsReferenceNames();

        Assert.Equal("DigitalBrain.Demo.Contracts", demoContractsAssemblyName);
        Assert.Contains(coreAssemblyName, references);
        Assert.DoesNotContain(demoContractsAssemblyName, CoreReferenceNames());
    }

    [Fact]
    public void Demo_Contracts_Do_Not_Reference_Runtime_Host_Integration_Or_Marketplace_Packages()
    {
        var references = DemoContractsReferenceNames();
        var coreAssemblyName = typeof(Synapse).Assembly.GetName().Name!;

        var unexpectedDigitalBrainReferences = references
            .Where(name => name.StartsWith("DigitalBrain.", StringComparison.Ordinal) &&
                !string.Equals(name, coreAssemblyName, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unexpectedDigitalBrainReferences);

        var offenders = references
            .Where(name => ForbiddenRuntimeHostOrIntegrationPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Demo_Runtime_Depends_On_Contract_Packages_Not_Runtime_Host_Or_Integrations()
    {
        var references = DemoRuntimeReferenceNames();
        var allowedDigitalBrainReferences = new HashSet<string>(StringComparer.Ordinal)
        {
            typeof(Synapse).Assembly.GetName().Name!,
            typeof(DemoMessageSynapse).Assembly.GetName().Name!,
            typeof(IPackBehavior).Assembly.GetName().Name!,
            typeof(MarketplaceUiSurfaces).Assembly.GetName().Name!,
            typeof(UiSurface).Assembly.GetName().Name!
        };

        var unexpectedDigitalBrainReferences = references
            .Where(name => name.StartsWith("DigitalBrain.", StringComparison.Ordinal) &&
                !allowedDigitalBrainReferences.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unexpectedDigitalBrainReferences);

        var offenders = references
            .Where(name => ForbiddenRuntimeHostOrIntegrationPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Core_Does_Not_Own_Demo_Test_Contracts()
    {
        var coreAssemblyName = typeof(Synapse).Assembly.GetName().Name!;

        Assert.NotEqual(coreAssemblyName, typeof(DemoMessageSynapse).Assembly.GetName().Name);
        Assert.NotEqual(coreAssemblyName, typeof(IDemoNeuron).Assembly.GetName().Name);
        Assert.Equal("DigitalBrain.Demo.Runtime", typeof(SurfaceDemoRuntime).Assembly.GetName().Name);
        Assert.NotEqual(coreAssemblyName, typeof(SurfaceDemoRuntime).Assembly.GetName().Name);
    }

    [Fact]
    public void Demo_Runtime_Owns_Surface_Demo_Request_And_Runtime_Helpers()
    {
        Assert.Equal("DigitalBrain.Demo.Runtime", typeof(SurfaceDemoRuntime).Assembly.GetName().Name);
        Assert.Equal("DigitalBrain.Kernel.SurfaceDemoRequested", SurfaceDemoRuntime.RequestType);
    }

    [Fact]
    public void Pack_Contracts_Own_NeuroPack_And_Bundle_Manifest()
    {
        Assert.Equal("DigitalBrain.Pack.Contracts", typeof(NeuroPack).Assembly.GetName().Name);
        Assert.Equal("DigitalBrain.Pack.Contracts", typeof(BundleManifest).Assembly.GetName().Name);
    }

    [Fact]
    public void Core_Does_Not_Reference_Runtime_Host_Or_Integration_Packages()
    {
        var offenders = CoreReferenceNames()
            .Where(name => ForbiddenRuntimeHostOrIntegrationPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Ui_Contracts_Do_Not_Reference_Runtime_Host_Integration_Or_Marketplace_Packages()
    {
        var references = UiContractsReferenceNames();
        var coreAssemblyName = typeof(Synapse).Assembly.GetName().Name!;

        var unexpectedDigitalBrainReferences = references
            .Where(name => name.StartsWith("DigitalBrain.", StringComparison.Ordinal) &&
                !string.Equals(name, coreAssemblyName, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unexpectedDigitalBrainReferences);

        var offenders = references
            .Where(name => ForbiddenRuntimeHostOrIntegrationPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Ui_Contracts_Own_Ui_Schema_Types()
    {
        Assert.Equal("DigitalBrain.Ui.Contracts", typeof(UiSurface).Assembly.GetName().Name);
        Assert.Equal("DigitalBrain.Ui.Contracts", typeof(UiWidgetTree).Assembly.GetName().Name);
        Assert.Equal("DigitalBrain.Ui.Contracts", typeof(NeuronUiKit).Assembly.GetName().Name);
        Assert.Equal("DigitalBrain.Ui.Contracts", typeof(UiKitVocabulary).Assembly.GetName().Name);
        Assert.Equal("DigitalBrain.Ui.Contracts", typeof(ChartSpec).Assembly.GetName().Name);
        Assert.Equal("DigitalBrain.Ui.Contracts", typeof(GraphicSpec).Assembly.GetName().Name);
        Assert.Equal("DigitalBrain.Ui.Contracts", typeof(UiSurfaceActions).Assembly.GetName().Name);
        Assert.Equal("DigitalBrain.Ui.Contracts", typeof(AuthButtonSurface).Assembly.GetName().Name);
        Assert.Equal("DigitalBrain.Ui.Contracts", typeof(ListSurface).Assembly.GetName().Name);
        Assert.Equal("DigitalBrain.Ui.Contracts", typeof(TableSurface).Assembly.GetName().Name);
        Assert.Equal("DigitalBrain.Ui.Contracts", typeof(IdeSurface).Assembly.GetName().Name);
        Assert.Equal("DigitalBrain.Ui.Contracts", typeof(RfwCard).Assembly.GetName().Name);
        Assert.Equal("DigitalBrain.Ui.Contracts", typeof(IFlutterUiNeuron).Assembly.GetName().Name);
    }

    [Fact]
    public void Ui_Contracts_Do_Not_Own_Demo_Or_Live_Surface_Builders()
    {
        var uiContractsTypeNames = typeof(UiSurface).Assembly.GetTypes()
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(nameof(UiSurfaceSamples), uiContractsTypeNames);
        Assert.DoesNotContain(nameof(UiSurfaceLiveData), uiContractsTypeNames);
    }

    [Fact]
    public void Ui_Runtime_Owns_Demo_And_Live_Surface_Builders()
    {
        Assert.Equal("DigitalBrain.Ui.Runtime", typeof(UiSurfaceSamples).Assembly.GetName().Name);
        Assert.Equal("DigitalBrain.Ui.Runtime", typeof(UiSurfaceLiveData).Assembly.GetName().Name);
    }

    [Fact]
    public void Ino_Integration_Owns_Assistant_Reasoning_Not_Kernel_Orleans()
    {
        Assert.Equal("DigitalBrain.Ino", typeof(InoIntentClassifier).Assembly.GetName().Name);
        Assert.Equal("DigitalBrain.Ino", typeof(IInoCapabilityRecall).Assembly.GetName().Name);

        var references = InoReferenceNames();
        var forbiddenReferences = references
            .Where(name => name.StartsWith("DigitalBrain.", StringComparison.Ordinal) ||
                name.StartsWith("Orleans", StringComparison.Ordinal) ||
                name.StartsWith("Microsoft.Orleans", StringComparison.Ordinal) ||
                name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
                name.StartsWith("Microsoft.Extensions.Hosting", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(forbiddenReferences);
    }
    [Fact]
    public void Ui_Runtime_Depends_On_Core_And_Ui_Contracts_Not_Marketplace_Runtime_Host_Or_Integrations()
    {
        var references = UiRuntimeReferenceNames();
        var coreAssemblyName = typeof(Synapse).Assembly.GetName().Name!;
        var uiContractsAssemblyName = typeof(UiSurface).Assembly.GetName().Name!;

        Assert.Contains(coreAssemblyName, references);
        Assert.Contains(uiContractsAssemblyName, references);

        var unexpectedDigitalBrainReferences = references
            .Where(name => name.StartsWith("DigitalBrain.", StringComparison.Ordinal) &&
                !string.Equals(name, coreAssemblyName, StringComparison.Ordinal) &&
                !string.Equals(name, uiContractsAssemblyName, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unexpectedDigitalBrainReferences);

        var offenders = references
            .Where(name => ForbiddenRuntimeHostOrIntegrationPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Ui_Surface_Live_Data_Does_Not_Expose_NeuroPack_Projection_Methods()
    {
        var offenders = typeof(UiSurfaceLiveData)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => MethodSignatureContains(method, typeof(NeuroPack)))
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Marketplace_Contracts_Depend_On_Core_Pack_And_Ui_Contracts_Not_Runtime_Host_Or_Integrations()
    {
        var references = MarketplaceContractsReferenceNames();
        var coreAssemblyName = typeof(Synapse).Assembly.GetName().Name!;
        var packContractsAssemblyName = typeof(IPackBehavior).Assembly.GetName().Name!;
        var uiContractsAssemblyName = typeof(UiSurface).Assembly.GetName().Name!;

        Assert.Contains(coreAssemblyName, references);
        Assert.Contains(packContractsAssemblyName, references);
        Assert.Contains(uiContractsAssemblyName, references);

        var unexpectedDigitalBrainReferences = references
            .Where(name => name.StartsWith("DigitalBrain.", StringComparison.Ordinal) &&
                !string.Equals(name, coreAssemblyName, StringComparison.Ordinal) &&
                !string.Equals(name, packContractsAssemblyName, StringComparison.Ordinal) &&
                !string.Equals(name, uiContractsAssemblyName, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unexpectedDigitalBrainReferences);

        var offenders = references
            .Where(name => ForbiddenRuntimeHostOrIntegrationPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Core_Does_Not_Own_Marketplace_Pack_Contracts()
    {
        var coreAssemblyName = typeof(Synapse).Assembly.GetName().Name!;
        var marketplaceContractsAssemblyName = typeof(MarketplaceUiSurfaces).Assembly.GetName().Name!;

        Assert.Equal(marketplaceContractsAssemblyName, typeof(PublishToMarketplace).Assembly.GetName().Name);
        Assert.Equal(marketplaceContractsAssemblyName, typeof(InstallFromMarketplace).Assembly.GetName().Name);
        Assert.Equal(marketplaceContractsAssemblyName, typeof(PublishedList).Assembly.GetName().Name);
        Assert.NotEqual(coreAssemblyName, typeof(NeuroPack).Assembly.GetName().Name);
    }

    private static string[] CoreReferenceNames() => ReferenceNames(typeof(Synapse).Assembly);

    private static string[] PackContractsReferenceNames() => ReferenceNames(typeof(IPackBehavior).Assembly);

    private static string[] MarketplaceContractsReferenceNames() => ReferenceNames(typeof(MarketplaceUiSurfaces).Assembly);

    private static string[] UiContractsReferenceNames() => ReferenceNames(typeof(UiSurface).Assembly);

    private static string[] UiRuntimeReferenceNames() => ReferenceNames(typeof(UiSurfaceLiveData).Assembly);

    private static string[] InoReferenceNames() => ReferenceNames(typeof(InoIntentClassifier).Assembly);

    private static string[] DemoContractsReferenceNames() => ReferenceNames(typeof(DemoMessageSynapse).Assembly);

    private static string[] DemoRuntimeReferenceNames() => ReferenceNames(typeof(SurfaceDemoRuntime).Assembly);

    private static bool MethodSignatureContains(MethodInfo method, Type type) =>
        TypeContains(method.ReturnType, type) ||
        method.GetParameters().Any(parameter => TypeContains(parameter.ParameterType, type));

    private static bool TypeContains(Type candidate, Type type)
    {
        if (candidate == type)
        {
            return true;
        }

        if (candidate.HasElementType && candidate.GetElementType() is { } elementType)
        {
            return TypeContains(elementType, type);
        }

        return candidate.IsGenericType &&
            candidate.GetGenericArguments().Any(argument => TypeContains(argument, type));
    }

    private static string[] ReferenceNames(System.Reflection.Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(name => name.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToArray();
}


