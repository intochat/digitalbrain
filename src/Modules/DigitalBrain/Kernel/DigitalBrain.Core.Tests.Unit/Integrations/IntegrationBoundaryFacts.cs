using DigitalBrain.Sdk.Secrets;
using DigitalBrain.Platform.Integrations;
using DigitalBrain.Platform.Integrations.Accounts;

namespace DigitalBrain.Core.Tests.Unit.Integrations;

public sealed class IntegrationBoundaryFacts
{
    [Fact]
    public void RegistrationAndProbeContractsBelongToTheSdk()
    {
        Assert.Equal(typeof(ISecrets).Assembly, typeof(IIntegrationRegistration).Assembly);
        Assert.Equal(typeof(ISecrets).Assembly, typeof(IAccountProbe).Assembly);
    }
}
