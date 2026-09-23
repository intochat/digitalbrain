using DigitalBrain.Broker.Hosting;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class AssemblyTrustFacts
{
    private static readonly AssemblyTrustVerifier Verifier = new();

    [Fact]
    public void ASignedContractsAssemblyWithoutBannedApisIsLoadable()
    {
        var decision = Verifier.Verify(Signed("DigitalBrain.Broker.Contracts", "System.String", "System.Collections.Generic.List"));

        Assert.True(decision.Allowed);
        Assert.Empty(decision.Reasons);
    }

    [Fact]
    public void AnUnsignedAssemblyIsNotLoadable()
    {
        var decision = Verifier.Verify(new AssemblyLoadRequest { AssemblyName = "DigitalBrain.Broker.Contracts" });

        Assert.False(decision.Allowed);
        Assert.Contains(decision.Reasons, reason => reason.Contains("not signed", StringComparison.Ordinal));
    }

    [Fact]
    public void AContractsAssemblyReferencingABannedApiIsNotLoadable()
    {
        var decision = Verifier.Verify(Signed("DigitalBrain.Broker.Contracts", "System.Diagnostics.Process"));

        Assert.False(decision.Allowed);
        Assert.Contains(decision.Reasons, reason => reason.Contains("banned API", StringComparison.Ordinal));
    }

    [Fact]
    public void ANonContractsAssemblyIsNotLoadable()
    {
        var decision = Verifier.Verify(Signed("Acme.RemoteApp"));

        Assert.False(decision.Allowed);
        Assert.Contains(decision.Reasons, reason => reason.Contains("contracts assembly", StringComparison.Ordinal));
    }

    [Fact]
    public void TheHostSelectsOnlySignedContractsAssemblies()
    {
        AssemblyLoadRequest[] candidates =
        [
            Signed("DigitalBrain.Broker.Contracts"),
            Signed("Acme.RemoteApp"),
            new AssemblyLoadRequest { AssemblyName = "Acme.Contracts" },
            Signed("Acme.Contracts", "System.IO.File"),
        ];

        var loadable = Verifier.SelectLoadable(candidates);

        var single = Assert.Single(loadable);
        Assert.Equal("DigitalBrain.Broker.Contracts", single.AssemblyName);
    }

    private static AssemblyLoadRequest Signed(string assemblyName, params string[] referencedTypes) => new()
    {
        AssemblyName = assemblyName,
        PublicKeyToken = "a1b2c3d4e5f60718",
        ReferencedTypes = referencedTypes,
    };
}
