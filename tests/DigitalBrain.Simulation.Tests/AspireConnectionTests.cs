using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Microsoft;
using DigitalBrain.Simulation.Tests.Sdk;
using ModelContextProtocol.Protocol;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class AspireConnectionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("list_apphosts")]
    [InlineData("select_apphost")]
    public async Task Binding_discovers_before_selecting_and_refuses_server_errors(string? failingTool)
    {
        var calls = new List<string>();
        var settings = Settings();
        await using var server = new McpDiscoveredToolTests.FakeMcpServer
        {
            OnToolCall = parameters =>
            {
                var name = parameters.GetProperty("name").GetString()!;
                calls.Add(name);
                if (name == "select_apphost")
                {
                    Assert.Equal("list_apphosts", calls[0]);
                    Assert.Equal(settings.ProjectPath, parameters.GetProperty("arguments").GetProperty("appHostPath").GetString());
                }
                return new CallToolResult { IsError = name == failingTool, Content = [] };
            },
        };
        await using var client = await server.ConnectAsync("test", TestContext.Current.CancellationToken);
        var target = NeuronId.For<IAspire>(settings.Owner, PrincipalPartition.InstanceName(PrincipalId.New(), settings.Alias));

        var binding = AspireConnection.BindApplicationAsync(client, target, settings, TestContext.Current.CancellationToken);
        if (failingTool is null) { await binding; }
        else { await Assert.ThrowsAsync<DigitalBrain.Sdk.McpOperationException>(() => binding); }

        Assert.Equal(failingTool == "list_apphosts" ? ["list_apphosts"] : ["list_apphosts", "select_apphost"], calls);
    }

    [Theory]
    [InlineData("other-owner", "digitalbrain-local")]
    [InlineData("dev", "another-application")]
    public async Task Binding_rejects_wrong_owner_or_target_before_discovery(string owner, string alias)
    {
        var calls = 0;
        await using var server = new McpDiscoveredToolTests.FakeMcpServer
        {
            OnToolCall = _ => { calls++; return new CallToolResult { Content = [] }; },
        };
        await using var client = await server.ConnectAsync("test", TestContext.Current.CancellationToken);
        var target = NeuronId.For<IAspire>(new OwnerId(owner), PrincipalPartition.InstanceName(PrincipalId.New(), alias));
        await Assert.ThrowsAsync<DigitalBrain.Sdk.McpOperationException>(() => AspireConnection.BindApplicationAsync(
            client, target, Settings(), TestContext.Current.CancellationToken));
        Assert.Equal(0, calls);
    }

    private static AspireConnectionSettings Settings() => new(
        Path.GetFullPath("Application.AppHost.csproj"), "Application", "digitalbrain-local", new OwnerId("dev"), "aspire");
}
