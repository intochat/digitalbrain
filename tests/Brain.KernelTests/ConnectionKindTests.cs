using Brain.Contracts;
using Brain.Modules.Connections;
using Brain.Modules.Sdk;
using Xunit;

namespace Brain.KernelTests;

public class ConnectionKindTests(BrainClusterFixture<ConnectionsKindsConfigurator> fixture)
    : BrainTest<ConnectionsKindsConfigurator>(fixture)
{
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
}
