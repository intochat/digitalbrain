using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Client;
using DigitalBrain.Flutter;
using DigitalBrain.Tasks;
using DigitalBrain.Time;
using Xunit;

namespace DigitalBrain.Tests.Boundary;

public sealed class CompositionBoundaryContracts
{
    private const string CompositionsPackage = "DigitalBrain.Compositions";

    private static readonly string[] AllowedDirect =
    [
        PackageOf(typeof(NeuronId)),
        PackageOf(typeof(DigitalBrainClient)),
        PackageOf(typeof(ILLM)),
        PackageOf(typeof(IShell)),
        PackageOf(typeof(ICountdown)),
    ];

    private static readonly string[] AllowedReachable =
    [
        .. AllowedDirect,
        PackageOf(typeof(ITask)),
    ];

    [Fact(DisplayName = "pre-rail compositions reference only client + contracts — never Kernel or runtimes")]
    public void PreRailCompositionsNeverReferenceKernelOrModuleRuntimes()
    {
        Assert.Equal(
            AllowedDirect.Order(StringComparer.Ordinal),
            PackageBoundarySupport.DirectCompileProjectReferencesOf(CompositionsPackage)
                .Order(StringComparer.Ordinal));
        Assert.Empty(PackageBoundarySupport.DirectPackageReferencesOf(CompositionsPackage));
        Assert.False(PackageBoundarySupport.IsPackable(CompositionsProjectFile));
    }

    [Fact(DisplayName = "pre-rail compositions transitively reach only client + contracts — never Kernel/runtimes/Integrations")]
    public void PreRailCompositionsTransitivelyReachOnlyClientAndContracts()
    {
        Assert.Equal(
            AllowedReachable.Order(StringComparer.Ordinal),
            PackageBoundarySupport.CompileProjectsReachableFrom(CompositionsPackage)
                .Order(StringComparer.Ordinal));
    }

    private static string CompositionsProjectFile =>
        Path.Combine(
            RepositoryLayout.Root,
            RepositoryLayout.Samples,
            CompositionsPackage,
            $"{CompositionsPackage}.csproj");

    private static string PackageOf(Type type)
        => type.Assembly.GetName().Name
           ?? throw new InvalidOperationException($"Assembly for {type.FullName} has no name.");
}
