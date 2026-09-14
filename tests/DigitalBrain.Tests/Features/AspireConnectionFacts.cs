using DigitalBrain.Microsoft;
using ModelContextProtocol.Protocol;
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

    [Fact]
    public void An_error_result_with_text_yields_the_verbatim_message()
    {
        var result = new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = "kernel-b is not currently running." }],
        };
        var error = Assert.Throws<InvalidOperationException>(() => AspireConnection.ThrowIfFailed(result));
        Assert.Equal("Aspire: kernel-b is not currently running.", error.Message);
    }

    [Fact]
    public void An_error_result_without_text_yields_the_fallback_message()
    {
        var result = new CallToolResult { IsError = true, Content = [] };
        var error = Assert.Throws<InvalidOperationException>(() => AspireConnection.ThrowIfFailed(result));
        Assert.Equal("Aspire returned an error without a message.", error.Message);
    }

    [Fact]
    public void A_non_error_result_yields_no_exception()
    {
        var result = new CallToolResult { IsError = false, Content = [] };
        AspireConnection.ThrowIfFailed(result);
    }
}
