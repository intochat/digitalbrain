using DigitalBrain.Core;
using DigitalBrain.Core.Distribution;
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
    public void Core_Does_Not_Reference_Runtime_Host_Or_Integration_Packages()
    {
        var offenders = CoreReferenceNames()
            .Where(name => ForbiddenRuntimeHostOrIntegrationPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Core_Live_Data_Does_Not_Expose_NeuroPack_Projection_Methods()
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
    public void Marketplace_Contracts_Depend_On_Core_Not_Runtime_Host_Or_Integrations()
    {
        var references = MarketplaceContractsReferenceNames();
        var coreAssemblyName = typeof(Synapse).Assembly.GetName().Name!;

        Assert.Contains(coreAssemblyName, references);

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

    private static string[] CoreReferenceNames() => ReferenceNames(typeof(Synapse).Assembly);

    private static string[] PackContractsReferenceNames() => ReferenceNames(typeof(IPackBehavior).Assembly);

    private static string[] MarketplaceContractsReferenceNames() => ReferenceNames(typeof(MarketplaceUiSurfaces).Assembly);

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
