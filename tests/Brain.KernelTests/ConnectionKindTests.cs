using System.Text.Json;
using Brain.Contracts;
using Brain.Modules.Connections;
using Brain.Modules.Sdk;
using Xunit;

namespace Brain.KernelTests;

public class ConnectionKindTests(BrainClusterFixture<ConnectionsKindsConfigurator> fixture)
    : BrainTest<ConnectionsKindsConfigurator>(fixture)
{
    private static readonly JsonSerializerOptions TokenJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Full_happy_path_start_complete_probe_healthy()
    {
        ConnectionsKindsConfigurator.GoogleProvider.Reset();
        var connection = Neuron("connection", $"google-{Guid.NewGuid():N}");

        var start = await connection.InvokeAsync(new("connection.start-auth.v1", "{}", "cmd-start", OwnerSession));
        Assert.Contains("authorizationUrl", start.OutputJson);

        var complete = await connection.InvokeAsync(new("connection.complete-auth.v1", """{"code":"auth-code"}""", "cmd-complete", OwnerSession));
        Assert.Contains("connected", complete.OutputJson);

        var probe = await connection.InvokeAsync(new("connection.probe.v1", "{}", "cmd-probe", OwnerSession));
        Assert.Contains($"\"health\":\"{ConnectionHealth.Healthy}\"", probe.OutputJson);
        Assert.Equal(1, ConnectionsKindsConfigurator.GoogleProvider.ProbeCalls);
    }

    [Fact]
    public async Task Probe_before_auth_reports_not_authorized_with_connect_fix()
    {
        var connection = Neuron("connection", $"google-{Guid.NewGuid():N}");
        var probe = await connection.InvokeAsync(new("connection.probe.v1", "{}", "cmd-probe", OwnerSession));
        Assert.Contains($"\"health\":\"{ConnectionHealth.NotAuthorized}\"", probe.OutputJson);
        Assert.Contains("\"fix\":\"connect\"", probe.OutputJson);
    }

    [Fact]
    public async Task Complete_after_expiry_fails_closed()
    {
        var connection = Neuron("connection", $"google-{Guid.NewGuid():N}");
        await connection.InvokeAsync(new("connection.start-auth.v1", "{}", "cmd-start", OwnerSession));
        ConnectionsKindsConfigurator.Clock.Advance(TimeSpan.FromMinutes(11));

        try
        {
            var exception = await Assert.ThrowsAsync<BrainException>(() =>
                connection.InvokeAsync(new("connection.complete-auth.v1", """{"code":"auth-code"}""", "cmd-complete", OwnerSession)));
            Assert.Equal(BrainErrors.ConnectionUnhealthy, exception.Code);
        }
        finally
        {
            ConnectionsKindsConfigurator.Clock.Advance(TimeSpan.FromMinutes(-11));
        }
    }

    [Fact]
    public async Task Complete_without_start_fails_closed()
    {
        var connection = Neuron("connection", $"google-{Guid.NewGuid():N}");
        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            connection.InvokeAsync(new("connection.complete-auth.v1", """{"code":"auth-code"}""", "cmd-complete", OwnerSession)));
        Assert.Equal(BrainErrors.ConnectionUnhealthy, exception.Code);
    }

    [Fact]
    public async Task Unknown_provider_instance_reports_missing_app_credentials()
    {
        var connection = Neuron("connection", $"nobody-{Guid.NewGuid():N}");
        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            connection.InvokeAsync(new("connection.start-auth.v1", "{}", "cmd-start", OwnerSession)));
        Assert.Equal(BrainErrors.ConnectionUnhealthy, exception.Code);
        Assert.Contains(ConnectionHealth.MissingAppCredentials, exception.Message);
    }

    [Fact]
    public async Task Suspend_blocks_probe_and_resume_restores()
    {
        var connection = Neuron("connection", $"google-{Guid.NewGuid():N}");
        await connection.InvokeAsync(new("connection.start-auth.v1", "{}", "cmd-start", OwnerSession));
        await connection.InvokeAsync(new("connection.complete-auth.v1", """{"code":"auth-code"}""", "cmd-complete", OwnerSession));
        await connection.InvokeAsync(new("connection.probe.v1", "{}", "cmd-probe-1", OwnerSession));

        await connection.InvokeAsync(new("connection.suspend.v1", """{"reason":"maintenance"}""", "cmd-suspend", OwnerSession));
        var suspendedProbe = await connection.InvokeAsync(new("connection.probe.v1", "{}", "cmd-probe-2", OwnerSession));
        Assert.Contains("\"suspended\":true", suspendedProbe.OutputJson);
        Assert.Contains("\"fix\":\"none\"", suspendedProbe.OutputJson);

        await connection.InvokeAsync(new("connection.resume.v1", """{"reason":"back"}""", "cmd-resume", OwnerSession));
        var resumedProbe = await connection.InvokeAsync(new("connection.probe.v1", "{}", "cmd-probe-3", OwnerSession));
        Assert.Contains("\"suspended\":false", resumedProbe.OutputJson);
        Assert.Contains($"\"health\":\"{ConnectionHealth.Healthy}\"", resumedProbe.OutputJson);
    }

    [Fact]
    public async Task Lease_token_denied_to_session_caller_with_zero_state()
    {
        var connection = Neuron("connection", $"google-{Guid.NewGuid():N}");
        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            connection.InvokeAsync(new("connection.lease-token.v1", "{}", "cmd-lease", OwnerSession)));
        Assert.Equal(BrainErrors.GrantMissing, exception.Code);
        Assert.Empty((await connection.ReadEventsAsync(0, 1000)).Events);

        var retried = await Assert.ThrowsAsync<BrainException>(() =>
            connection.InvokeAsync(new("connection.lease-token.v1", "{}", "cmd-lease", OwnerSession)));
        Assert.Equal(BrainErrors.GrantMissing, retried.Code);
        Assert.Empty((await connection.ReadEventsAsync(0, 1000)).Events);
    }

    [Fact]
    public async Task Lease_token_bypasses_replay_cache_and_returns_current_token()
    {
        ConnectionsKindsConfigurator.GoogleProvider.Reset();
        var uid = Guid.NewGuid().ToString("N");
        var connection = Neuron("connection", $"google-{uid}");
        var connectorCaller = AddressKey("gmail", "primary");

        var firstToken = new ConnectionToken("first-access", "first-refresh", DateTimeOffset.UtcNow.AddHours(1));
        ConnectionsKindsConfigurator.GoogleProvider.ExchangeResult = firstToken;
        await connection.InvokeAsync(new("connection.start-auth.v1", "{}", $"cmd-start-1-{uid}", OwnerSession));
        await connection.InvokeAsync(new("connection.complete-auth.v1", """{"code":"auth-code"}""", $"cmd-complete-1-{uid}", OwnerSession));

        var firstLease = await connection.InvokeAsync(new("connection.lease-token.v1", "{}", "cmd-lease", connectorCaller));
        var leasedFirst = JsonSerializer.Deserialize<ConnectionToken>(firstLease.OutputJson, TokenJsonOptions);
        Assert.Equal(firstToken, leasedFirst);

        var secondToken = new ConnectionToken("second-access", "second-refresh", DateTimeOffset.UtcNow.AddHours(2));
        ConnectionsKindsConfigurator.GoogleProvider.ExchangeResult = secondToken;
        await connection.InvokeAsync(new("connection.start-auth.v1", "{}", $"cmd-start-2-{uid}", OwnerSession));
        await connection.InvokeAsync(new("connection.complete-auth.v1", """{"code":"auth-code"}""", $"cmd-complete-2-{uid}", OwnerSession));

        var secondLease = await connection.InvokeAsync(new("connection.lease-token.v1", "{}", "cmd-lease", connectorCaller));
        var leasedSecond = JsonSerializer.Deserialize<ConnectionToken>(secondLease.OutputJson, TokenJsonOptions);
        Assert.Equal(secondToken, leasedSecond);
        Assert.NotEqual(leasedFirst, leasedSecond);
    }

    [Fact]
    public async Task Probe_with_unknown_health_value_is_classified_as_provider_error()
    {
        ConnectionsKindsConfigurator.GoogleProvider.Reset();
        var connection = Neuron("connection", $"google-{Guid.NewGuid():N}");
        await connection.InvokeAsync(new("connection.start-auth.v1", "{}", $"cmd-start-{Guid.NewGuid():N}", OwnerSession));
        await connection.InvokeAsync(new("connection.complete-auth.v1", """{"code":"auth-code"}""", $"cmd-complete-{Guid.NewGuid():N}", OwnerSession));

        ConnectionsKindsConfigurator.GoogleProvider.NextProbeResult = new ProbeResult("weird", "unexpected upstream status");
        var probe = await connection.InvokeAsync(new("connection.probe.v1", "{}", $"cmd-probe-{Guid.NewGuid():N}", OwnerSession));
        Assert.Contains($"\"health\":\"{ConnectionHealth.ProviderError}\"", probe.OutputJson);
        Assert.Contains("weird", probe.OutputJson);
    }
}
