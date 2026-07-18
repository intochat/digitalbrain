using Reqnroll;
using Reqnroll.BoDi;

namespace Ino.Domains.Travel.Tests.Storyboard;

// Manages the StoryboardTestSiloFixture lifecycle across the test run.
// [BeforeTestRun] / [AfterTestRun] are static and run once per test process.
// [BeforeScenario] registers the fixture into the per-scenario IObjectContainer
// so it can be constructor-injected into TokyoSteps.
[Binding]
public sealed class StoryboardFixtureHooks
{
    private static StoryboardTestSiloFixture? fixture;

    private readonly IObjectContainer container;

    public StoryboardFixtureHooks(IObjectContainer container)
    {
        this.container = container;
    }

    [BeforeTestRun]
    public static async Task StartClusterAsync()
    {
        fixture = new StoryboardTestSiloFixture();
        await fixture.InitializeAsync();
    }

    [AfterTestRun]
    public static async Task StopClusterAsync()
    {
        if (fixture is not null)
        {
            await fixture.DisposeAsync();
            fixture = null;
        }
    }

    // Makes the fixture available for injection into step binding constructors.
    // Scoped per-scenario so each scenario gets a fresh reference (the fixture
    // itself is shared but the reference is fresh).
    [BeforeScenario]
    public void RegisterFixture()
    {
        if (fixture is null) return;
        container.RegisterInstanceAs(fixture);
    }
}
