using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Sdk;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class AgentToolExecutionTests
{
    [Theory]
    [InlineData(McpFailureKind.CatalogChanged, "catalog_changed")]
    [InlineData(McpFailureKind.ConnectionChanged, "connection_changed")]
    [InlineData(McpFailureKind.AccessDenied, "access_denied")]
    public async Task Safe_tool_failure_identity_reaches_observations(McpFailureKind kind, string expected)
    {
        var observations = new List<AgentActivity>();
        using var context = Context(observations);
        Func<string> fail = () => throw new McpOperationException("Safe operation failure.", kind);
        var function = AIFunctionFactory.Create(fail,
            new AIFunctionFactoryOptions { Name = "read" });
        var tool = AgentToolExecution.Observe(context, function, "fixture", new Screen());

        var failure = await Assert.ThrowsAsync<McpOperationException>(() => tool.InvokeAsync([], TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(kind, failure.Kind);
        var finished = Assert.Single(observations, activity => activity.State == "failed");
        Assert.Equal(expected, finished.FailureCode);
        Assert.True(finished.IsError);
    }

    [Fact]
    public async Task Expired_prepared_tool_cannot_invoke_or_record_late_activity()
    {
        var observations = new List<AgentActivity>();
        var context = Context(observations);
        var called = false;
        var function = AIFunctionFactory.Create(() => { called = true; return "result"; },
            new AIFunctionFactoryOptions { Name = "read" });
        var tool = AgentToolExecution.Observe(context, function, "fixture", new Screen());
        context.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => tool.InvokeAsync([], TestContext.Current.CancellationToken).AsTask());
        Assert.False(called);
        Assert.Empty(observations);
    }

    [Fact]
    public async Task Content_is_redacted_before_screening_and_model_or_journal_publication()
    {
        var observations = new List<AgentActivity>();
        using var context = Context(observations);
        var screen = new Screen();
        var function = AIFunctionFactory.Create(() => JsonSerializer.SerializeToElement(new
        {
            access_token = "private-credential-marker",
            text = "safe evidence",
        }), new AIFunctionFactoryOptions { Name = "read" });
        var tool = AgentToolExecution.Observe(context, function, "fixture", screen);
        var result = await tool.InvokeAsync([], TestContext.Current.CancellationToken);

        Assert.DoesNotContain("private-credential-marker", Assert.IsType<JsonElement>(result).GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("private-credential-marker", screen.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("private-credential-marker", Assert.Single(observations, activity => activity.State == "completed").Preview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Screening_failure_withholds_content_and_raw_exception_details()
    {
        var observations = new List<AgentActivity>();
        using var context = Context(observations);
        var function = AIFunctionFactory.Create(() => "blocked content", new AIFunctionFactoryOptions { Name = "read" });
        var tool = AgentToolExecution.Observe(context, function, "fixture", new Screen { Reject = true });
        var failure = await Assert.ThrowsAsync<McpOperationException>(() => tool.InvokeAsync([], TestContext.Current.CancellationToken).AsTask());

        Assert.DoesNotContain("private-screen-error", failure.Message, StringComparison.Ordinal);
        var finished = Assert.Single(observations, activity => activity.State == "failed");
        Assert.Equal("content_rejected", finished.FailureCode);
        Assert.Null(finished.Preview);
    }

    private static AgentToolContext Context(List<AgentActivity> observations)
        => new(new NeuronId("probe", new OwnerId("dev"), "probe"), null, new NoRequests(), activity =>
        {
            observations.Add(activity);
            return Task.CompletedTask;
        });

    private sealed class Screen : IUntrustedContentScreen
    {
        internal bool Reject { get; init; }
        internal string Content { get; private set; } = "";
        public Task ScreenAsync(string content, CancellationToken cancellationToken)
        {
            Content = content;
            return Reject ? Task.FromException(new InvalidOperationException("private-screen-error")) : Task.CompletedTask;
        }
    }

    private sealed class NoRequests : IAgentRequests
    {
        public Task<AgentReply> RequestAsync<TAgent>(string instanceName, AgentRequest request,
            CancellationToken cancellationToken = default) where TAgent : IAgent
            => throw new InvalidOperationException("No delegation is available in this fixture.");
    }
}
