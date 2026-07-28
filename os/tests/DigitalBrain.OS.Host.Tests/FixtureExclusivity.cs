using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.HostTests;

public sealed class FixtureExclusivity(
    TestingAppHostFixture testing,
    QuickstartAppHostFixture quickstart)
{
    private const int LiveAppHostTimeoutMs = 120_000;

    [Fact(
        Timeout = LiveAppHostTimeoutMs,
        DisplayName = "a second graph waits for the first within the same AppHost fixture")]
    public async Task ASecondGraphWaitsForTheFirstWithinTheSameFixture()
    {
        await AssertSecondGraphWaitsAsync(
            testing,
            testing,
            TestContext.Current.CancellationToken);
    }

    [Fact(
        Timeout = LiveAppHostTimeoutMs,
        DisplayName =
            "a second graph waits for the first across silo-only AppHost fixture types")]
    public async Task ASecondGraphWaitsForTheFirstAcrossFixtureTypes()
    {
        await AssertSecondGraphWaitsAsync(
            testing,
            quickstart,
            TestContext.Current.CancellationToken);
    }

    private static async Task AssertSecondGraphWaitsAsync(
        DigitalBrainAppHostFixture firstFixture,
        DigitalBrainAppHostFixture secondFixture,
        CancellationToken cancellationToken)
    {
        RunningAppHost? first = null;
        Task<RunningAppHost>? waiting = null;
        try
        {
            first = await firstFixture.StartAsync(cancellationToken);
            waiting = secondFixture.StartAsync(cancellationToken);
            Assert.False(waiting.IsCompleted);

            await first.DisposeAsync();
            first = null;

            await using var second = await waiting.WaitAsync(cancellationToken);
            waiting = null;
            Assert.NotNull(second);
        }
        finally
        {
            await DisposeQuietlyAsync(first);
            await DisposeStartedAsync(waiting);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Cleanup after exclusivity failures must not mask the original test exception.")]
    private static async Task DisposeQuietlyAsync(RunningAppHost? host)
    {
        if (host is null)
        {
            return;
        }

        try
        {
            await host.DisposeAsync();
        }
        catch
        {
            // Preserve the original test failure when cleanup races or stop times out.
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Orphaned AppHost starts must be torn down without masking the original test exception.")]
    private static async Task DisposeStartedAsync(Task<RunningAppHost>? starting)
    {
        if (starting is null)
        {
            return;
        }

        try
        {
            var host = await starting.WaitAsync(TimeSpan.FromSeconds(90));
            await DisposeQuietlyAsync(host);
        }
        catch
        {
            // Orphaned starts must not leave AppHost graphs running after the fact ends.
        }
    }
}
