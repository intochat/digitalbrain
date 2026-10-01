using DigitalBrain.CustomerResearcher;
using DigitalBrain.Contracts;
using DigitalBrain.Modules.Assistant.Tests.Unit;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.CustomerResearcher.Tests.Unit;

public sealed class CustomerResearcherRouteFacts
{
    [Fact]
    public void CustomerResearcherOpensOnlyUnderTheBrainRoute()
    {
        var snapshot = RouteSnapshot.Map(
            services => services.AddSingleton(typeof(IDigitalBrain), _ => null!),
            app => new CustomerResearcherModule().Configure(app));

        Assert.Equal(["/brains/{brainId}/applications/customer-researcher/open"], snapshot.Routes);
    }
}
