using Reqnroll;

namespace DigitalBrain.OS.Bdd.Tests;

[Binding]
public static class OSCluster
{
    private static OSFixture? _fixture;

    internal static OSFixture Fixture =>
        _fixture ?? throw new InvalidOperationException(
            "The BDD cluster has not started. Reqnroll [BeforeTestRun] did not run.");

    [BeforeTestRun]
    public static async Task StartAsync()
    {
        var fixture = new OSFixture();
        await fixture.InitializeAsync();
        _fixture = fixture;
    }

    [AfterTestRun]
    public static async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _fixture, null) is { } fixture)
        {
            await fixture.DisposeAsync();
        }
    }
}
