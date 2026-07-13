using System.Reflection;
using DigitalBrain.Core;
using DigitalBrain.Ui.Contracts.Ui;

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
    public void Core_References_Only_The_Generic_Platform_Contracts()
    {
        var digitalBrainReferences = CoreReferenceNames()
            .Where(name => name.StartsWith("DigitalBrain.", StringComparison.Ordinal) &&
                !string.Equals(name, "DigitalBrain.Kernel.Contracts", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(digitalBrainReferences);
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
    public void Core_Does_Not_Reference_Runtime_Host_Or_Integration_Packages()
    {
        var offenders = CoreReferenceNames()
            .Where(name => ForbiddenRuntimeHostOrIntegrationPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Ui_Contracts_Do_Not_Reference_Runtime_Host_Integration_Or_Pack_Packages()
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
    public void Ui_Runtime_Depends_On_Core_And_Ui_Contracts_Not_Runtime_Host_Or_Integrations()
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

    private static string[] CoreReferenceNames() => ReferenceNames(typeof(Synapse).Assembly);

    private static string[] UiContractsReferenceNames() => ReferenceNames(typeof(UiSurface).Assembly);

    private static string[] UiRuntimeReferenceNames() => ReferenceNames(typeof(UiSurfaceLiveData).Assembly);

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


