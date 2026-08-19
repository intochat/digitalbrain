using DigitalBrain.Testing.E2E;
using Reqnroll;

namespace DigitalBrain.Testing.Bdd;

// The feature run's one AppHost boot. Feature-scoped rather than test-run-scoped on purpose:
// the kernel binds its unproxied HTTP endpoint on a fixed port (AppHost.cs UiHttpPort, spared
// by the fixture's proxied-port randomization), so this AppHost must never be alive while the
// classic e2e collection's own boot is. That collection is serialized against every other
// collection by DisableParallelization, and bounding this boot to the feature window keeps the
// two warm AppHosts from ever overlapping — in either execution order xunit picks.
[Binding]
public static class BddBrainHost
{
    private static readonly SemaphoreSlim BootGate = new(1, 1);
    private static BrainAppHostFixture<Projects.DigitalBrain_AppHost>? _fixture;
    private static int _activeFeatures;

    public static BrainAppHostFixture<Projects.DigitalBrain_AppHost> Fixture
        => _fixture ?? throw new InvalidOperationException(
            $"{nameof(BddBrainHost)} has not booted; steps only run inside a Reqnroll feature.");

    [BeforeFeature]
    public static async Task BootAsync()
    {
        await BootGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (++_activeFeatures > 1)
            {
                return;
            }

            var fixture = new BrainAppHostFixture<Projects.DigitalBrain_AppHost>(new BrainE2EOptions
            {
                ProjectEnvironment =
                {
                    // The same corpus Tier 2 consumes, copied to the test output by the csproj.
                    ["DigitalBrain__AI__Corpus__Path"] = Path.Combine(AppContext.BaseDirectory, "corpus"),
                },
            });
            try
            {
                await fixture.InitializeAsync().ConfigureAwait(false);
            }
            catch
            {
                _activeFeatures--;
                throw;
            }

            _fixture = fixture;
        }
        finally
        {
            BootGate.Release();
        }
    }

    [AfterFeature]
    public static async Task ShutdownAsync()
    {
        await BootGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_activeFeatures > 0 && --_activeFeatures == 0 && _fixture is { } fixture)
            {
                _fixture = null;
                await fixture.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            BootGate.Release();
        }
    }
}
