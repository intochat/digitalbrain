using DigitalBrain.Sdk.Secrets;

namespace DigitalBrain.Identity.Tests.Unit;

public sealed class IdentityBoundaryFacts
{
    [Fact]
    public void IdentityContractsBelongToTheSingleSdkAssembly()
    {
        Assert.Equal(typeof(ISecrets).Assembly, typeof(IIdentityDirectory).Assembly);
        Assert.Equal(typeof(ISecrets).Assembly, typeof(IGrantStore).Assembly);
    }
}
