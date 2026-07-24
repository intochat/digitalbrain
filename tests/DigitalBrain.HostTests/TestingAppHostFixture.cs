using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.HostTests;

[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires collection fixture types to be public.")]
public sealed class TestingAppHostFixture : IAsyncLifetime
{
    public HostedScenario App { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        App = await HostedApplication.OpenAsync<Projects.DigitalBrain_TestingAppHost>(
            cancellationToken: cancellationToken);

        await App.WaitHttpReadyAsync("silo", cancellationToken: cancellationToken);
        await App.WaitHttpReadyAsync("probe", cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (App is not null)
        {
            await App.DisposeAsync();
        }
    }
}
