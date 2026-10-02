using DigitalBrain.Sdk.Secrets;
using DigitalBrain.Identity;

namespace DigitalBrain.Core.Tests.Unit.Identity;

public sealed class IdentityBoundaryFacts
{
    [Fact]
    public void IdentityContractsBelongToTheSingleSdkAssembly()
    {
        Assert.Equal(typeof(ISecrets).Assembly, typeof(IIdentityDirectory).Assembly);
        Assert.Equal(typeof(ISecrets).Assembly, typeof(IGrantStore).Assembly);
    }
}
