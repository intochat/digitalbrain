using Reqnroll;

namespace DigitalBrain.Os.Bdd.Tests;

[Binding]
public static class OsCluster
{
    private static OsFixture? _fixture;

    internal static OsFixture Fixture =>
        _fixture ?? throw new InvalidOperationException(
            "The BDD cluster has not started. Reqnroll [BeforeTestRun] did not run.");

    [BeforeTestRun]
    public static async Task StartAsync()
    {
        var fixture = new OsFixture();
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
