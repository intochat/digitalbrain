using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.HostTests;

public sealed class FixtureExclusivity(TestingAppHostFixture testing)
{
    private const int LiveAppHostTimeoutMs = 300_000;

    [Fact(
        Timeout = LiveAppHostTimeoutMs,
        DisplayName = "a second graph waits for the first within the same AppHost fixture")]
    public async Task ASecondGraphWaitsForTheFirstWithinTheSameFixture()
    {
        RunningAppHost? first = null;
        Task<RunningAppHost>? waiting = null;
        try
        {
            first = await testing.StartAsync(TestContext.Current.CancellationToken);
            waiting = testing.StartAsync(TestContext.Current.CancellationToken);
            Assert.False(waiting.IsCompleted);

            await first.DisposeAsync();
            first = null;

            await using var second = await waiting.WaitAsync(TestContext.Current.CancellationToken);
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
            var host = await starting.WaitAsync(TimeSpan.FromMinutes(5));
            await DisposeQuietlyAsync(host);
        }
        catch
        {
        }
    }
}
