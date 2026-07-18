using Reqnroll;

namespace DigitalBrain.Os.Tests;

[Binding]
public class DistributionBindings
{
    private readonly SimulationContext _sim;

    public DistributionBindings(SimulationContext sim)
    {
        _sim = sim;
    }

    [Given("a clean digital brain")]
    public async Task GivenClean() => await _sim.ResetAsync();

    [Given("the demo experience handler grain is active")]
    public async Task GivenDemoActive() => await _sim.EnsureDemoHandlerAsync();

    [When("I install the \"demo-experience\" bundle")]
    public async Task WhenInstallDemo() => await _sim.InstallAsync("demo-experience");

    [Then("ListSubscribers for BundleInstalled has grown to at least {int}")]
    public async Task ThenSubscribersGrow(int min) => await _sim.AssertSubscribersAtLeastAsync("BundleInstalled", min);

    [Then("the demo experience handler has reacted to BundleInstalled")]
    public async Task ThenDemoReacted() => await _sim.AssertDemoReactedAsync();

    // TDD extension: ClientTap Demo triggers surface emit (for headless/mcp + coverage)
    [When("I send ClientTap for Demo")]
    public async Task WhenClientTapDemo() => await _sim.TriggerClientTapDemoAsync();
}
