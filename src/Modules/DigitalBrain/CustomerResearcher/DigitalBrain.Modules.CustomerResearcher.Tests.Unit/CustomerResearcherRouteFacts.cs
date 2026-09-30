using DigitalBrain.Contracts;
using DigitalBrain.Testing.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.CustomerResearcher.Tests;

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
