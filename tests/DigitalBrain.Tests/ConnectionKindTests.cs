using System.Text.Json;
using Brain.Contracts;
using Brain.Kernel.Connections;
using DigitalBrain.Tests;
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
        var state = StateFrom(start.OutputJson);

        var complete = await connection.InvokeAsync(new(
            "connection.complete-auth.v1",
            JsonSerializer.Serialize(new { code = "auth-code", state }),
            "cmd-complete",
            OwnerSession));
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
        var start = await connection.InvokeAsync(new("connection.start-auth.v1", "{}", "cmd-start", OwnerSession));
        var state = StateFrom(start.OutputJson);
        ConnectionsKindsConfigurator.Clock.Advance(TimeSpan.FromMinutes(11));

        try
        {
            var exception = await Assert.ThrowsAsync<BrainException>(() =>
                connection.InvokeAsync(new(
                    "connection.complete-auth.v1",
                    JsonSerializer.Serialize(new { code = "auth-code", state }),
                    "cmd-complete",
                    OwnerSession)));
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
            connection.InvokeAsync(new(
                "connection.complete-auth.v1",
                JsonSerializer.Serialize(new { code = "auth-code", state = "unused" }),
                "cmd-complete",
                OwnerSession)));
        Assert.Equal(BrainErrors.ConnectionUnhealthy, exception.Code);
    }

    [Fact]
    public async Task Wrong_oauth_state_is_rejected_before_token_exchange()
    {
        ConnectionsKindsConfigurator.GoogleProvider.Reset();
        var connection = Neuron("connection", $"google-{Guid.NewGuid():N}");
        await connection.InvokeAsync(new("connection.start-auth.v1", "{}", Guid.NewGuid().ToString("N"), OwnerSession));

        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            connection.InvokeAsync(new(
                "connection.complete-auth.v1",
                """{"code":"auth-code","state":"wrong-state"}""",
                Guid.NewGuid().ToString("N"),
                OwnerSession)));

        Assert.Equal(BrainErrors.ConnectionUnhealthy, exception.Code);
        Assert.Equal(0, ConnectionsKindsConfigurator.GoogleProvider.ExchangeCodeCalls);
    }

    [Fact]
    public async Task Replayed_oauth_completion_is_rejected()
    {
        ConnectionsKindsConfigurator.GoogleProvider.Reset();
        var connection = Neuron("connection", $"google-{Guid.NewGuid():N}");
        var start = await connection.InvokeAsync(new("connection.start-auth.v1", "{}", Guid.NewGuid().ToString("N"), OwnerSession));
        var state = StateFrom(start.OutputJson);
        var input = JsonSerializer.Serialize(new { code = "auth-code", state });

        await connection.InvokeAsync(new("connection.complete-auth.v1", input, Guid.NewGuid().ToString("N"), OwnerSession));
        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            connection.InvokeAsync(new("connection.complete-auth.v1", input, Guid.NewGuid().ToString("N"), OwnerSession)));

        Assert.Equal(BrainErrors.ConnectionUnhealthy, exception.Code);
        Assert.Equal(1, ConnectionsKindsConfigurator.GoogleProvider.ExchangeCodeCalls);
    }

    [Fact]
    public async Task Connection_journal_never_contains_plaintext_credentials()
    {
        const string accessToken = "access-token-must-not-be-journaled";
        const string refreshToken = "refresh-token-must-not-be-journaled";
        const string authorizationCode = "authorization-code-must-not-be-journaled";
        ConnectionsKindsConfigurator.GoogleProvider.Reset();
        ConnectionsKindsConfigurator.GoogleProvider.ExchangeResult =
            new ConnectionToken(accessToken, refreshToken, DateTimeOffset.UtcNow.AddHours(1));

        try
        {
            var connection = Neuron("connection", $"google-{Guid.NewGuid():N}");
            var start = await connection.InvokeAsync(new(
                "connection.start-auth.v1",
                "{}",
                Guid.NewGuid().ToString("N"),
                OwnerSession));
            var state = StateFrom(start.OutputJson);

            await connection.InvokeAsync(new(
                "connection.complete-auth.v1",
                JsonSerializer.Serialize(new { code = authorizationCode, state }),
                Guid.NewGuid().ToString("N"),
                OwnerSession));

            var events = (await connection.ReadEventsAsync(0, 500)).Events;
            var journal = JsonSerializer.Serialize(events);
            Assert.DoesNotContain(accessToken, journal, StringComparison.Ordinal);
            Assert.DoesNotContain(refreshToken, journal, StringComparison.Ordinal);
            Assert.DoesNotContain(authorizationCode, journal, StringComparison.Ordinal);

            var projection = (await connection.ReadAsync("default")).StateJson;
            Assert.DoesNotContain(accessToken, projection, StringComparison.Ordinal);
            Assert.DoesNotContain(refreshToken, projection, StringComparison.Ordinal);
            Assert.DoesNotContain("protectedToken", projection, StringComparison.OrdinalIgnoreCase);
            using var connectedPayload = JsonDocument.Parse(
                events.Single(entry => entry.Kind == "connection.connected").PayloadJson);
            var protectedToken = connectedPayload.RootElement.GetProperty("protectedToken").GetString()!;
            Assert.DoesNotContain(protectedToken, projection, StringComparison.Ordinal);
        }
        finally
        {
            ConnectionsKindsConfigurator.GoogleProvider.Reset();
        }
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
        var start = await connection.InvokeAsync(new("connection.start-auth.v1", "{}", "cmd-start", OwnerSession));
        var state = StateFrom(start.OutputJson);
        await connection.InvokeAsync(new(
            "connection.complete-auth.v1",
            JsonSerializer.Serialize(new { code = "auth-code", state }),
            "cmd-complete",
            OwnerSession));
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
        var firstStart = await connection.InvokeAsync(new("connection.start-auth.v1", "{}", $"cmd-start-1-{uid}", OwnerSession));
        var firstState = StateFrom(firstStart.OutputJson);
        await connection.InvokeAsync(new(
            "connection.complete-auth.v1",
            JsonSerializer.Serialize(new { code = "auth-code", state = firstState }),
            $"cmd-complete-1-{uid}",
            OwnerSession));
        await connection.InvokeAsync(new(
            "neuron.grant.v1",
            JsonSerializer.Serialize(new { granteeKey = connectorCaller, contract = "connection.lease-token.v1" }),
            $"cmd-grant-{uid}",
            OwnerSession));

        var firstLease = await connection.InvokeAsync(new("connection.lease-token.v1", "{}", "cmd-lease", connectorCaller));
        var leasedFirst = JsonSerializer.Deserialize<ConnectionToken>(firstLease.OutputJson, TokenJsonOptions);
        Assert.Equal(firstToken, leasedFirst);

        var secondToken = new ConnectionToken("second-access", "second-refresh", DateTimeOffset.UtcNow.AddHours(2));
        ConnectionsKindsConfigurator.GoogleProvider.ExchangeResult = secondToken;
        var secondStart = await connection.InvokeAsync(new("connection.start-auth.v1", "{}", $"cmd-start-2-{uid}", OwnerSession));
        var secondState = StateFrom(secondStart.OutputJson);
        await connection.InvokeAsync(new(
            "connection.complete-auth.v1",
            JsonSerializer.Serialize(new { code = "auth-code", state = secondState }),
            $"cmd-complete-2-{uid}",
            OwnerSession));

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
        var start = await connection.InvokeAsync(new("connection.start-auth.v1", "{}", $"cmd-start-{Guid.NewGuid():N}", OwnerSession));
        var state = StateFrom(start.OutputJson);
        await connection.InvokeAsync(new(
            "connection.complete-auth.v1",
            JsonSerializer.Serialize(new { code = "auth-code", state }),
            $"cmd-complete-{Guid.NewGuid():N}",
            OwnerSession));

        try
        {
            ConnectionsKindsConfigurator.GoogleProvider.NextProbeResult = new ProbeResult("weird", "unexpected upstream status");
            var probe = await connection.InvokeAsync(new("connection.probe.v1", "{}", $"cmd-probe-{Guid.NewGuid():N}", OwnerSession));
            Assert.Contains($"\"health\":\"{ConnectionHealth.ProviderError}\"", probe.OutputJson);
            Assert.Contains("weird", probe.OutputJson);
        }
        finally
        {
            ConnectionsKindsConfigurator.GoogleProvider.Reset();
        }
    }

    [Fact]
    public async Task Lease_token_denied_to_ungranted_same_owner_neuron()
    {
        ConnectionsKindsConfigurator.GoogleProvider.Reset();
        var connection = Neuron("connection", $"google-{Guid.NewGuid():N}");
        var start = await connection.InvokeAsync(new(
            "connection.start-auth.v1",
            "{}",
            Guid.NewGuid().ToString("N"),
            OwnerSession));
        var state = StateFrom(start.OutputJson);
        await connection.InvokeAsync(new(
            "connection.complete-auth.v1",
            JsonSerializer.Serialize(new { code = "auth-code", state }),
            Guid.NewGuid().ToString("N"),
            OwnerSession));

        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            connection.InvokeAsync(new(
                "connection.lease-token.v1",
                "{}",
                Guid.NewGuid().ToString("N"),
                AddressKey("gmail", "ungranted"))));

        Assert.Equal(BrainErrors.GrantMissing, exception.Code);
    }

    private static string StateFrom(string outputJson)
    {
        using var output = JsonDocument.Parse(outputJson);
        var authorizationUrl = output.RootElement.GetProperty("authorizationUrl").GetString()!;
        return new Uri(authorizationUrl).Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => Uri.UnescapeDataString(pair[0]) == "state")
            .Select(pair => Uri.UnescapeDataString(pair[1]))
            .Single();
    }
}
