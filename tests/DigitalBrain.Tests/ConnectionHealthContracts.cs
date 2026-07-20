using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ConnectionHealthContracts
{
    [Fact(DisplayName = "connection health is a closed union that drives distinct UI actions")]
    public void HealthUnionIsClosedAndExhaustive()
    {
        ConnectionHealth[] cases =
        [
            new ConnectionHealthy(),
            new ConnectionMissingAppCredentials("no client id"),
            new ConnectionNotConfigured(),
            new ConnectionNotAuthorized(),
            new ConnectionTokenExpired(),
            new ConnectionProviderError("400"),
            new ConnectionNetworkError("timeout"),
        ];

        var actions = cases.Select(ConnectionLifecycle.DescribeAction).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(7, cases.Length);
        Assert.Contains("administrator-must-configure", actions);
        Assert.Contains("user-must-sign-in", actions);
        Assert.Contains("retry", actions);
        Assert.Equal(5, actions.Count);
    }

    [Fact(DisplayName = "modules supply adapters; the kernel owns lifecycle transitions")]
    public void KernelOwnsLifecycleTransitions()
    {
        Assert.Equal(
            ConnectionLifecycleState.Authorized,
            ConnectionLifecycle.Advance(ConnectionLifecycleState.Authorizing, new ConnectionHealthy()));

        Assert.Equal(
            ConnectionLifecycleState.Expired,
            ConnectionLifecycle.Advance(ConnectionLifecycleState.Authorized, new ConnectionTokenExpired()));

        Assert.Equal(
            ConnectionLifecycleState.NotConfigured,
            ConnectionLifecycle.Advance(ConnectionLifecycleState.Authorized, new ConnectionMissingAppCredentials("x")));
    }
}
