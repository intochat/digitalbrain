using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Product.Identity;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Salesforce;
using DigitalBrain.Sdk;
using DigitalBrain.Simulation.Tests.Sdk;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class SalesforceSpecialistTests
{
    [Fact]
    public async Task Native_catalog_stays_on_specialist_and_write_wrapper_only_publishes_a_preview()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var actor = VerifiedActor.Enter(fixture.Actor);
        using var turn = AgentTurnContext.Enter(fixture.Turn);
        using var context = fixture.Context();
        var tools = await fixture.Tools.GetToolsAsync(context, Token);
        Assert.Equal(SalesforceMcp.AllowedTools.Order(), tools.Select(tool => tool.Name).Order());
        var write = Assert.IsAssignableFrom<AIFunction>(Assert.Single(tools, tool => tool.Name == "createRecord"));
        Assert.True(JsonElement.DeepEquals(fixture.Server.Tools[2].InputSchema, write.JsonSchema));
        Assert.False(write.JsonSchema.GetProperty("properties").TryGetProperty("confirmed", out _));
        var result = await write.InvokeAsync(new() { ["body"] = "the exact record change" }, Token);
        Assert.Equal("preview_ready", Assert.IsType<JsonElement>(result).GetProperty("status").GetString());
        Assert.Equal(0, fixture.Server.ToolCalls);
        Assert.Contains("confirm salesforce change", fixture.Previews.ResponseFor(fixture.Turn), StringComparison.Ordinal);

        var read = Assert.IsAssignableFrom<AIFunction>(Assert.Single(tools, tool => tool.Name == "soqlQuery"));
        await Assert.ThrowsAsync<McpOperationException>(() => read.InvokeAsync(new() { ["query"] = "SELECT Id FROM Account" }, Token).AsTask());
        Assert.Equal(0, fixture.Server.ToolCalls);
        await read.InvokeAsync(new() { ["query"] = "SELECT Id FROM Account WHERE Name='Acme' LIMIT 1" }, Token);
        Assert.Equal(1, fixture.Server.ToolCalls);

        var delegation = new AgentDelegation<ISalesforce>("ask_salesforce", "Salesforce specialist", "salesforce-local");
        var inoTools = await delegation.GetToolsAsync(context, Token);
        Assert.Equal("ask_salesforce", Assert.Single(inoTools).Name);
        Assert.DoesNotContain(inoTools, tool => SalesforceMcp.AllowedTools.Contains(tool.Name));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Confirmation_refreshes_native_schema_and_never_replays_a_write(bool changeSchema)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var actor = VerifiedActor.Enter(fixture.Actor);
        string preview;
        using (AgentTurnContext.Enter(fixture.Turn))
        using (var context = fixture.Context())
        {
            var tools = await fixture.Tools.GetToolsAsync(context, Token);
            var write = tools.OfType<AIFunction>().Single(tool => tool.Name == "updateRecord");
            _ = await write.InvokeAsync(new() { ["body"] = "reviewed" }, Token);
            preview = Assert.IsType<string>(fixture.Previews.ResponseFor(fixture.Turn));
            fixture.Previews.ResponsePublished(fixture.Turn, preview);
        }
        if (changeSchema) { fixture.Server.Tools[3] = Definition("updateRecord", "newBody"); }
        using (AgentTurnContext.Enter(fixture.Turn with { CommandId = CommandId.New() }))
        {
            var confirmation = Regex.Match(preview, @"confirm salesforce change [a-f0-9]{64}").Value;
            var result = await fixture.Previews.HandleAsync(confirmation, Token);
            Assert.Equal(result, await fixture.Previews.HandleAsync(confirmation, Token));
            Assert.Equal(changeSchema ? 0 : 1, fixture.Server.ToolCalls);
            Assert.True(fixture.Server.CatalogReads >= 2);
        }
    }

    [Fact]
    public async Task Owner_shared_connection_rejects_other_principal_and_revision_changes()
    {
        await using var fixture = await Fixture.CreateAsync();
        SalesforceBinding first;
        using (VerifiedActor.Enter(fixture.Actor))
        {
            first = fixture.Connections.Identity(fixture.Owner, fixture.Actor.PrincipalId);
            await fixture.Connections.StoreAsync(fixture.Owner, fixture.Actor.PrincipalId, "new-fixture-token", null,
                TimeSpan.FromHours(1), commit => commit(), Token);
            Assert.NotEqual(first.Revision, fixture.Connections.Identity(fixture.Owner, fixture.Actor.PrincipalId).Revision);
            await Assert.ThrowsAsync<McpOperationException>(() => fixture.Connections.AccessTokenAsync(fixture.Owner, first, false, Token));
        }
        using (VerifiedActor.Enter(new ActorContext(PrincipalId.New(), "other")))
        {
            Assert.Throws<McpAuthenticationRequiredException>(() => fixture.Connections.Connection(fixture.Owner));
            await Assert.ThrowsAsync<McpAuthenticationRequiredException>(() => fixture.Connections.AccessTokenAsync(fixture.Owner, first, false, Token));
        }
        Assert.Equal(0, fixture.Server.Connections);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("revision")]
    [InlineData("write-scope")]
    [InlineData("scope-substitution")]
    public async Task Restricted_continuation_rejects_substituted_target_binding_or_scope_before_discovery(string substitution)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var actor = VerifiedActor.Enter(fixture.Actor);
        var binding = fixture.Connections.Identity(fixture.Owner, fixture.Actor.PrincipalId);
        var continuation = new SpecialistContinuation(fixture.Target, "read records", ["getUserInfo"], binding.Revision);
        var allowed = new[] { "getUserInfo" };
        continuation = substitution switch
        {
            "target" => continuation with { Target = new NeuronId("salesforce", fixture.Owner, PrincipalPartition.InstanceName(fixture.Actor.PrincipalId, "other")) },
            "revision" => continuation with { ConnectionRevision = "different-revision" },
            "scope-substitution" => continuation with { AllowedToolNames = ["soqlQuery"] },
            _ => continuation,
        };
        if (substitution == "write-scope") { allowed = ["createRecord"]; continuation = continuation with { AllowedToolNames = allowed }; }
        using var turn = AgentTurnContext.Enter(fixture.Turn with { AllowedToolNames = allowed, SpecialistContinuation = continuation });
        using var context = fixture.Context();
        await Assert.ThrowsAsync<McpOperationException>(() => fixture.Tools.GetToolsAsync(context, Token).AsTask());
        Assert.Equal(0, fixture.Server.Connections);
    }

    [Fact]
    public async Task Unavailable_and_fake_bindings_have_deliberate_local_capabilities()
    {
        var owner = new OwnerId("dev");
        var actor = new ActorContext(PrincipalId.New(), "fixture");
        using var verified = VerifiedActor.Enter(actor);
        using var context = new AgentToolContext(NeuronId.For<ISalesforce>(owner,
            PrincipalPartition.InstanceName(actor.PrincipalId, "salesforce-local")), actor.PrincipalId, new Requests());
        var tools = await new SalesforceTools(new Screen()).GetToolsAsync(context, Token);
        var status = Assert.IsAssignableFrom<AIFunction>(Assert.Single(tools));
        Assert.Contains("not configured", (await status.InvokeAsync([], Token))?.ToString(), StringComparison.Ordinal);
        var fakeTools = await new SalesforceTools(new Screen(), fake: true).GetToolsAsync(context, Token);
        Assert.Equal(SalesforceLogins.ReadTools.Order(), fakeTools.Select(tool => tool.Name).Order());
        var fakeUser = fakeTools.OfType<AIFunction>().Single(tool => tool.Name == "getUserInfo");
        Assert.Contains("fake", (await fakeUser.InvokeAsync([], Token))?.ToString(), StringComparison.Ordinal);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static Tool Definition(string name, string property) => new()
    {
        Name = name,
        Description = "Native Salesforce fixture " + name,
        InputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new Dictionary<string, object> { [property] = new { type = "string" } },
            required = new[] { property },
        }),
    };

    private sealed class Fixture : IAsyncDisposable
    {
        internal readonly OwnerId Owner = new("dev");
        internal readonly ActorContext Actor = new(PrincipalId.New(), "salesforce-fixture");
        internal readonly McpDiscoveredToolTests.FakeMcpServer Server = new()
        {
            Tools = [Definition("getUserInfo", "unused"), Definition("soqlQuery", "query"), Definition("createRecord", "body"), Definition("updateRecord", "body")],
        };
        internal readonly SalesforceConnections Connections;
        internal readonly SalesforceMcp Mcp;
        internal readonly SalesforceWritePreviews Previews;
        internal readonly SalesforceTools Tools;
        internal readonly ServiceProvider Services = new ServiceCollection().BuildServiceProvider();
        internal readonly NeuronId Target;
        internal readonly AgentTurnContext Turn;

        private Fixture()
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SalesforceModule.OAuthConfigurationRoot + ":ConsumerKey"] = "fixture-key",
                [SalesforceModule.OAuthConfigurationRoot + ":ConsumerSecret"] = "fixture-secret",
                [SalesforceModule.OAuthConfigurationRoot + ":PublicOrigin"] = "http://localhost:5080",
            }).Build();
            var settings = new SalesforceOAuthConfiguration(configuration);
            Connections = new SalesforceConnections(settings);
            var client = new McpDiscoveredToolClient<SalesforceInvocation>(new McpStdioConnection
            {
                Name = "salesforce-fixture", Command = "not-started", AllowedToolNames = SalesforceMcp.AllowedTools,
            }, null, (identity, cancellation) => Server.ConnectAsync(identity.Agent.ToString(), cancellation));
            Mcp = new SalesforceMcp(Connections, client);
            Previews = new SalesforceWritePreviews(Mcp, new Screen());
            Tools = new SalesforceTools(Mcp, new SalesforceLogins(settings, Connections, Services), Previews, new Screen());
            Target = NeuronId.For<ISalesforce>(Owner, PrincipalPartition.InstanceName(Actor.PrincipalId, "salesforce-local"));
            Turn = new AgentTurnContext(new NeuronId("chat", Owner, PrincipalPartition.InstanceName(Actor.PrincipalId, "main")), CommandId.New(), Actor,
                SpecialistRequest: new SpecialistRequest(Target, "read Salesforce"));
        }

        internal AgentToolContext Context() => new(Target, Actor.PrincipalId, new Requests());

        internal static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            await fixture.Connections.StoreAsync(fixture.Owner, fixture.Actor.PrincipalId, "fixture-access-token", null,
                TimeSpan.FromHours(1), commit => commit(), Token);
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            await Mcp.DisposeAsync();
            await Server.DisposeAsync();
            Connections.Dispose();
            await Services.DisposeAsync();
        }
    }

    private sealed class Requests : IAgentRequests
    {
        public Task<AgentReply> RequestAsync<TAgent>(string instanceName, AgentRequest request, CancellationToken cancellationToken = default)
            where TAgent : IAgent => throw new NotSupportedException();
    }

    private sealed class Screen : IUntrustedContentScreen
    {
        public Task ScreenAsync(string content, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Trusted confirmation instructions belong in the application response,
            // never in the external data sent through the tool-content classifier.
            Assert.DoesNotContain("confirm salesforce change", content, StringComparison.Ordinal);
            return Task.CompletedTask;
        }
    }
}
