using DigitalBrain;
using DigitalBrain.Assistant;
using DigitalBrain.Compute.Usage;
using DigitalBrain.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Assistant.Tests.Unit;

public sealed class AssistantRouteFacts
{
    [Fact]
    public void ComputeUsageAndAssistantApplicationsAreServedUnderTheBrainRoute()
    {
        var snapshot = RouteSnapshot.Map(services =>
        {
            services.AddSingleton(typeof(IDigitalBrain), _ => null!);
            services.AddSingleton(typeof(IUsageStore), _ => null!);
        }, app => new AssistantModule().Configure(app));

        snapshot.AssertEveryMethodAndRouteIsDistinct();
        var routes = snapshot.Routes;
        Assert.Contains("/brains/{brainId}/compute/usage", routes);
        Assert.Contains("/brains/{brainId}/applications/assistant/open", routes);
        Assert.Contains("/brains/{brainId}/applications/assistant/start", routes);
        Assert.Contains("/brains/{brainId}/built-in/activate", routes);
        Assert.Contains("/brains/{brainId}/built-in/assistant/open", routes);
    }
}
