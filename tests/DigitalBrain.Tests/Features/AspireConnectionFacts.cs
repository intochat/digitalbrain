using DigitalBrain.Microsoft;
using Xunit;

namespace DigitalBrain.Tests;

// The Aspire client is a stdio process, so these facts assert the two things that are decided before any
// process starts: what may be executed, and what an unconfigured AppHost answers.
public sealed class AspireConnectionFacts
{
    [Theory]
    [InlineData("delete")]
    [InlineData("rebuild")]
    [InlineData("")]
    public async Task Only_start_stop_and_restart_may_be_executed(string command)
    {
        var connection = new AspireConnection(new AspireConnectionSettings("E:/repo/AppHost.csproj", "DigitalBrain", "aspire"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connection.ExecuteResourceCommandAsync("kernel-b", command, TestContext.Current.CancellationToken));
        Assert.Contains("start, stop or restart", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unconfigured_aspire_application_refuses_a_command_with_advice()
    {
        var connection = new AspireConnection(settings: null);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connection.ExecuteResourceCommandAsync("kernel-b", "start", TestContext.Current.CancellationToken));
        Assert.Equal("Configure the Aspire AppHost project before reading or commanding its resources.", error.Message);
    }

    [Fact]
    public async Task An_unconfigured_aspire_application_refuses_a_read_with_the_same_advice()
    {
        var connection = new AspireConnection(settings: null);
        // The check is in front of the allowlist, so an allowed read on an unconfigured host says what to
        // do rather than trying to spawn the CLI.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connection.ReadAsync("list_resources", new Dictionary<string, object?>(), TestContext.Current.CancellationToken));
        Assert.Equal("Configure the Aspire AppHost project before reading or commanding its resources.", error.Message);
    }

    [Fact]
    public async Task A_resource_name_is_required()
    {
        var connection = new AspireConnection(new AspireConnectionSettings("E:/repo/AppHost.csproj", "DigitalBrain", "aspire"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            connection.ExecuteResourceCommandAsync("   ", "start", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void The_connection_is_the_resource_command_seam()
    {
        Assert.IsAssignableFrom<IAspireResourceCommands>(new AspireConnection(settings: null));
    }
}
