using DigitalBrain.Core;
using DigitalBrain.Core.Distribution;

namespace DigitalBrain.Tests.Architecture;

public class CoreBoundaryTests
{
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
        var forbiddenPrefixes = new[]
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

        var offenders = CoreReferenceNames()
            .Where(name => forbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    private static string[] CoreReferenceNames() => ReferenceNames(typeof(Synapse).Assembly);

    private static string[] PackContractsReferenceNames() => ReferenceNames(typeof(IPackBehavior).Assembly);

    private static string[] ReferenceNames(System.Reflection.Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(name => name.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToArray();
}
