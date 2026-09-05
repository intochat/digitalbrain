using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Google;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Product.Identity;
using DigitalBrain.Sdk;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using DigitalBrain.Simulation.Tests.Sdk;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class GmailPolicyTests
{
    private static readonly OwnerId Owner = new("gmail-tests");
    private static readonly ActorContext Actor = new(PrincipalId.New(), "gmail-user");
    private static readonly NeuronId Target = NeuronId.For<IGmail>(Owner, PrincipalPartition.InstanceName(Actor.PrincipalId, "gmail-local"));

    [Fact]
    public async Task Selected_connection_is_principal_bound_and_each_acceptance_changes_revision()
    {
        await using var fixture = new Fixture();
        await fixture.ConnectAsync();
        var first = fixture.Connections.Identity(Owner, Actor.PrincipalId);
        Assert.Throws<McpAuthenticationRequiredException>(() => fixture.Connections.Identity(Owner, PrincipalId.New()));
        using (VerifiedActor.Enter(Actor))
        {
            GmailMcp.Authorize(new(Target, Actor.PrincipalId), first);
            Assert.Throws<McpAuthenticationRequiredException>(() => GmailMcp.Authorize(new(Target, PrincipalId.New()), first));
        }
        using (VerifiedActor.Enter(new ActorContext(PrincipalId.New(), "other")))
        {
            Assert.Throws<McpAuthenticationRequiredException>(() => GmailMcp.Authorize(new(Target, Actor.PrincipalId), first));
        }
        await fixture.ConnectAsync();
        Assert.NotEqual(first.Revision, fixture.Connections.Identity(Owner, Actor.PrincipalId).Revision);
        await Assert.ThrowsAsync<McpAuthenticationRequiredException>(() => fixture.Connections.AccessTokenAsync(Owner, first, false, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Native_arguments_keep_their_names_and_enforce_read_and_plain_text_bounds()
    {
        var input = JsonSerializer.Deserialize<Dictionary<string, object?>>("""{"query":"release","pageSize":4,"includeTrash":false,"view":"THREAD_VIEW_MINIMAL"}""")!;
        var normalized = GmailContent.Normalize("search_threads", input);
        Assert.Equal(4, normalized["pageSize"]);
        Assert.Equal("release", normalized["query"]);
        Assert.Throws<McpOperationException>(() => GmailContent.Normalize("search_threads", input.Append(new KeyValuePair<string, object?>("account", "another")).ToDictionary()));
        input["pageSize"] = 11;
        Assert.Throws<McpOperationException>(() => GmailContent.Normalize("search_threads", input));
        Assert.Throws<McpOperationException>(() => GmailContent.Normalize("get_thread", new Dictionary<string, object?> { ["threadId"] = "t", ["messageFormat"] = "HTML" }));
        Assert.Throws<McpOperationException>(() => GmailContent.Normalize("send_message", new Dictionary<string, object?>()));
    }

    [Fact]
    public void Projection_excludes_html_headers_attachments_and_unknown_fields()
    {
        var raw = JsonSerializer.SerializeToElement(new { id = "t", messages = new[] { new {
            id = "m", subject = "hello", plaintextBody = "useful body", html = "<script>unsafe</script>", attachments = new[] { "secret" }, headers = "hidden", unknown = "hidden" } } });
        var result = GmailContent.Project("get_thread", raw, new Dictionary<string, object?> { ["messageFormat"] = "PLAIN_TEXT" });
        var text = result.GetRawText();
        Assert.Contains("useful body", text, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Native_catalog_preserves_schema_applies_policy_and_rechecks_prepared_binding()
    {
        var calls = 0;
        await using var server = new McpDiscoveredToolTests.FakeMcpServer
        {
            Tools = [NativeDefinition("search_threads"), NativeDefinition("create_draft"), NativeDefinition("send_message")],
            OnToolCall = _ =>
            {
                calls++;
                return new CallToolResult { Content = [], StructuredContent = JsonSerializer.SerializeToElement(new { threads = Array.Empty<object>() }) };
            },
        };
        var client = NativeClient(server);
        await using var fixture = new Fixture(client);
        await fixture.ConnectAsync(compose: true);
        using var actor = VerifiedActor.Enter(Actor);
        using var turn = AgentTurnContext.Enter(Turn());
        using var context = new AgentToolContext(Target, Actor.PrincipalId, new NoRequests());
        var tools = await fixture.Tools.GetToolsAsync(context, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(tools, tool => tool.Name is "send_message" or "gmail_search_threads");
        var search = Assert.Single(tools.OfType<AIFunction>(), tool => tool.Name == "search_threads");
        Assert.True(JsonElement.DeepEquals(server.Tools[0].InputSchema, search.JsonSchema));
        var args = new AIFunctionArguments { ["query"] = "release" };
        var evidence = Assert.IsType<JsonElement>(await search.InvokeAsync(args, TestContext.Current.CancellationToken));
        Assert.Empty(evidence.GetProperty("threads").EnumerateArray());
        Assert.Equal(1, calls);
        await Assert.ThrowsAsync<McpOperationException>(() => search.InvokeAsync(new() { ["query"] = "release", ["pageSize"] = 11 }, TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(1, calls);
        var draft = Assert.Single(tools.OfType<AIFunction>(), tool => tool.Name == "create_draft");
        Assert.Contains("prepares an exact preview only", draft.Description, StringComparison.Ordinal);
        await fixture.ConnectAsync(compose: true);
        await Assert.ThrowsAsync<McpOperationException>(() => search.InvokeAsync(args, TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Draft_preview_preserves_native_schema_snapshot_until_confirmation()
    {
        var writes = 0;
        await using var server = new McpDiscoveredToolTests.FakeMcpServer
        {
            Tools = [NativeDefinition("create_draft")],
            OnToolCall = _ => { writes++; return new CallToolResult { Content = [], StructuredContent = JsonSerializer.SerializeToElement(new { id = "draft" }) }; },
        };
        var client = NativeClient(server);
        await using var fixture = new Fixture(client, screen: new RejectConfirmationScreen());
        await fixture.ConnectAsync(compose: true);
        using var actor = VerifiedActor.Enter(Actor);
        var original = Turn();
        string response;
        using (AgentTurnContext.Enter(original))
        {
            using var context = new AgentToolContext(Target, Actor.PrincipalId, new NoRequests());
            var tools = await fixture.Tools.GetToolsAsync(context, TestContext.Current.CancellationToken);
            var draft = Assert.Single(tools.OfType<AIFunction>(), tool => tool.Name == "create_draft");
            var result = await draft.InvokeAsync(new() { ["to"] = new[] { "recipient@example.com" }, ["subject"] = "subject", ["body"] = "body" }, TestContext.Current.CancellationToken);
            var status = Assert.IsType<JsonElement>(result);
            Assert.Equal("preview_ready", status.GetProperty("status").GetString());
            Assert.DoesNotContain("confirm gmail draft", status.GetRawText(), StringComparison.Ordinal);
            response = Assert.IsType<string>(fixture.Previews.ResponseFor(original));
            Assert.Contains("confirm gmail draft", response, StringComparison.Ordinal);
            fixture.Previews.ResponsePublished(original, response);
        }
        Assert.Equal(0, writes);
        server.Tools = [new Tool { Name = "create_draft", InputSchema = JsonSerializer.SerializeToElement(new { type = "object", properties = new { changed = new { type = "string" } } }) }];
        using var followup = AgentTurnContext.Enter(Turn());
        var confirmation = response.Split('\n')[^1];
        var first = await fixture.Previews.HandleAsync(confirmation, TestContext.Current.CancellationToken);
        var repeated = await fixture.Previews.HandleAsync(confirmation, TestContext.Current.CancellationToken);
        Assert.Equal(first, repeated);
        Assert.Equal(0, writes);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("revision")]
    [InlineData("scope")]
    [InlineData("missing")]
    public async Task Restricted_continuation_requires_exact_target_binding_and_current_read_scope(string change)
    {
        await using var fixture = new Fixture();
        await fixture.ConnectAsync();
        var binding = fixture.Connections.Identity(Owner, Actor.PrincipalId);
        var continuation = new SpecialistContinuation(Target, "find release", [.. GmailLogins.ReadTools], binding.Revision.ToString("N"));
        using var context = new AgentToolContext(Target, Actor.PrincipalId, new NoRequests());
        using var actor = VerifiedActor.Enter(Actor);
        using (AgentTurnContext.Enter(Turn() with { AllowedToolNames = [.. GmailLogins.ReadTools], SpecialistContinuation = continuation }))
        {
            GmailTools.RequireContinuation(context, binding);
        }
        continuation = change switch
        {
            "target" => continuation with { Target = new NeuronId("gmail", Owner, PrincipalPartition.InstanceName(Actor.PrincipalId, "different")) },
            "revision" => continuation with { ConnectionRevision = Guid.NewGuid().ToString("N") },
            "scope" => continuation with { AllowedToolNames = [.. GmailLogins.ReadTools, "create_draft"] },
            _ => null,
        };
        using var turn = AgentTurnContext.Enter(Turn() with { AllowedToolNames = [.. GmailLogins.ReadTools], SpecialistContinuation = continuation });
        Assert.Throws<McpOperationException>(() => GmailTools.RequireContinuation(context, binding));
    }

    [Fact]
    public async Task Missing_credentials_offer_local_login_with_native_read_scope_and_no_mutation()
    {
        await using var fixture = new Fixture();
        using var actor = VerifiedActor.Enter(Actor);
        var original = Turn() with { SpecialistRequest = new(Target, "find release email") };
        using var turn = AgentTurnContext.Enter(original);
        using var context = new AgentToolContext(Target, Actor.PrincipalId, new NoRequests());
        var tools = await fixture.Tools.GetToolsAsync(context, TestContext.Current.CancellationToken);
        var account = Assert.IsAssignableFrom<AIFunction>(Assert.Single(tools));
        Assert.Equal("get_current_account", account.Name);
        var result = await account.InvokeAsync([], TestContext.Current.CancellationToken);
        Assert.Contains("authentication_required", result?.ToString(), StringComparison.Ordinal);
        var login = Assert.IsType<UserActionRequest>(fixture.Logins.Find(Owner, original.CommandId));
        var continuation = Assert.IsType<SpecialistContinuation>(login.SpecialistContinuation);
        Assert.Equal(Target, continuation.Target);
        Assert.Equal("find release email", continuation.RequestText);
        Assert.Equal(GmailLogins.ReadTools, continuation.AllowedToolNames);
        Assert.DoesNotContain("create_draft", continuation.AllowedToolNames);
        Assert.Null(fixture.Previews.ResponseFor(original));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Accepted_login_with_missing_or_reassigned_credentials_has_no_resumable_specialist(bool reassigned)
    {
        await using var fixture = new Fixture();
        using var actor = VerifiedActor.Enter(Actor);
        var original = Turn() with { SpecialistRequest = new(Target, "find release email") };
        using var turn = AgentTurnContext.Enter(original);
        var login = fixture.Logins.RequireLogin(false, TestContext.Current.CancellationToken);
        var request = new Uri(login.LoginUrl).Query["?request=".Length..];
        Assert.True(fixture.Logins.TryBegin(request, out _));
        Assert.True(fixture.Logins.TryClaim(request));
        await fixture.Logins.AcceptForActorAsync(request, (accepted, _, commit) => fixture.Connections.AcceptAsync(
            accepted.Chat.Owner, accepted.Actor.PrincipalId, "accepted-subject", "me@example.com", "fixture-access", "fixture-refresh",
            $"openid email {GmailOAuthConfiguration.ReadScope}", "3600", false, commit, TestContext.Current.CancellationToken));
        Assert.NotNull(fixture.Logins.ResolveSpecialistContinuation(original, login.Id));
        if (reassigned)
        {
            await fixture.Connections.AcceptAsync(Owner, PrincipalId.New(), "other-subject", "other@example.com", "other-access", "other-refresh",
                $"openid email {GmailOAuthConfiguration.ReadScope}", "3600", false, static commit => commit(), TestContext.Current.CancellationToken);
        }
        else
        {
            await fixture.Connections.RejectAsync(Owner, fixture.Connections.Identity(Owner, Actor.PrincipalId), TestContext.Current.CancellationToken);
        }
        // Lost volatile credentials must finish the login rail without throwing/retrying
        // WaitingForUser indefinitely or selecting another principal's account.
        Assert.Null(fixture.Logins.ResolveSpecialistContinuation(original, login.Id));
        Assert.Null(fixture.Logins.ResolveSpecialistContinuation(original, login.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_published_draft_is_consumed_once_even_when_remote_outcome_is_uncertain(bool fail)
    {
        await using var fixture = new Fixture();
        await fixture.ConnectAsync(compose: true);
        // Signal delivery restores principal identity with a transport username; display
        // names cannot be part of draft authorization or chat/preview identity.
        using var actor = VerifiedActor.Enter(Actor with { Username = "_delivery" });
        var original = Turn();
        var submissions = 0;
        string? submittedTo = null;
        var native = DraftTool((to, _, _, _, _) =>
        {
            submissions++;
            submittedTo = Assert.Single(to);
            if (fail)
            {
                throw new IOException("uncertain transport outcome");
            }
            return JsonSerializer.SerializeToElement(new { structuredContent = new { id = "draft-123" }, content = Array.Empty<object>(), isError = false });
        });
        string response;
        using (AgentTurnContext.Enter(original))
        {
            var to = new[] { "recipient@example.com" };
            response = await fixture.Previews.CreateAsync(Owner, to, [], [], "Subject", "Plain body", TestContext.Current.CancellationToken,
                new(Target, Actor.PrincipalId), native);
            to[0] = "changed@example.com";
            fixture.Previews.ResponsePublished(original, response + " altered");
            Assert.Equal(0, submissions);
        }
        var confirmation = response.Split('\n')[^1];
        using var followup = AgentTurnContext.Enter(Turn());
        var rejected = await fixture.Previews.HandleAsync(confirmation, TestContext.Current.CancellationToken);
        Assert.Contains("unavailable", rejected, StringComparison.Ordinal);
        Assert.Equal(0, submissions);
        fixture.Previews.ResponsePublished(original, response);
        var first = await fixture.Previews.HandleAsync(confirmation, TestContext.Current.CancellationToken);
        var repeated = await fixture.Previews.HandleAsync(confirmation, TestContext.Current.CancellationToken);
        Assert.Equal(first, repeated);
        Assert.Equal(1, submissions);
        Assert.Equal("recipient@example.com", submittedTo);
        Assert.Contains(fail ? "could not be confirmed" : "draft created", first, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("actor")]
    [InlineData("chat")]
    [InlineData("same-command")]
    public async Task Draft_confirmation_refuses_changed_binding_actor_chat_or_originating_command(string change)
    {
        await using var fixture = new Fixture();
        await fixture.ConnectAsync(compose: true);
        using var actor = VerifiedActor.Enter(Actor);
        var original = Turn();
        var submissions = 0;
        var native = DraftTool((_, _, _, _, _) => { submissions++; return JsonSerializer.SerializeToElement(new { }); });
        string response;
        using (AgentTurnContext.Enter(original))
        {
            response = await fixture.Previews.CreateAsync(Owner, ["recipient@example.com"], [], [], "Subject", "Body", TestContext.Current.CancellationToken,
                new(Target, Actor.PrincipalId), native);
            fixture.Previews.ResponsePublished(original, response);
        }
        if (change == "account")
        {
            await fixture.ConnectAsync(compose: true);
        }
        var next = change switch
        {
            "chat" => Turn() with { Chat = new NeuronId("chat", Owner, "other-chat") },
            "actor" => Turn() with { Actor = new ActorContext(PrincipalId.New(), "other") },
            "same-command" => original,
            _ => Turn(),
        };
        using var currentActor = VerifiedActor.Enter(next.Actor);
        using var turn = AgentTurnContext.Enter(next);
        _ = await fixture.Previews.HandleAsync(response.Split('\n')[^1], TestContext.Current.CancellationToken);
        Assert.Equal(0, submissions);
    }

    [Fact]
    public async Task Expired_previews_are_not_submitted_and_capacity_recovers_after_expiry()
    {
        var clock = new PreviewClock();
        await using var fixture = new Fixture(time: clock);
        await fixture.ConnectAsync(compose: true);
        using var actor = VerifiedActor.Enter(Actor);
        var native = DraftTool((_, _, _, _, _) => throw new InvalidOperationException("Expired previews cannot submit."));
        string first = "";
        for (var index = 0; index < 128; index++)
        {
            var original = Turn();
            using var turn = AgentTurnContext.Enter(original);
            var response = await fixture.Previews.CreateAsync(Owner, ["recipient@example.com"], [], [], "Subject", "Body", TestContext.Current.CancellationToken,
                new(Target, Actor.PrincipalId), native);
            fixture.Previews.ResponsePublished(original, response);
            if (index == 0) { first = response; }
        }
        using var next = AgentTurnContext.Enter(Turn());
        await Assert.ThrowsAsync<McpOperationException>(() => fixture.Previews.CreateAsync(Owner, ["recipient@example.com"], [], [], "Subject", "Body", TestContext.Current.CancellationToken,
            new(Target, Actor.PrincipalId), native));
        clock.Advance(TimeSpan.FromMinutes(11));
        var result = await fixture.Previews.HandleAsync(first.Split('\n')[^1], TestContext.Current.CancellationToken);
        Assert.Contains("expired", result, StringComparison.Ordinal);
        var fresh = await fixture.Previews.CreateAsync(Owner, ["recipient@example.com"], [], [], "Subject", "Body", TestContext.Current.CancellationToken,
            new(Target, Actor.PrincipalId), native);
        Assert.Contains("draft preview", fresh, StringComparison.Ordinal);
    }

    private static AIFunction DraftTool(Func<string[], string[], string[], string, string, JsonElement> action)
        => AIFunctionFactory.Create((string[] to, string[] cc, string[] bcc, string subject, string body) => action(to, cc, bcc, subject, body),
            new AIFunctionFactoryOptions { Name = "create_draft" });

    private static AgentTurnContext Turn() => new(new NeuronId("chat", Owner, PrincipalPartition.InstanceName(Actor.PrincipalId, "main")), CommandId.New(), Actor);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();
        internal GmailConnections Connections { get; }
        internal GmailLogins Logins { get; }
        internal GmailMcp Mcp { get; }
        internal GmailDraftPreviews Previews { get; }
        internal GmailTools Tools { get; }

        internal Fixture(McpDiscoveredToolClient<GmailAgentIdentity>? client = null, TimeProvider? time = null,
            IUntrustedContentScreen? screen = null)
        {
            var configuration = new GmailOAuthConfiguration(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{GoogleModule.GmailOAuthConfigurationRoot}:ClientId"] = "fixture-client",
                [$"{GoogleModule.GmailOAuthConfigurationRoot}:ClientSecret"] = "fixture-secret",
                [$"{GoogleModule.GmailOAuthConfigurationRoot}:PublicOrigin"] = "http://localhost:5080",
            }).Build());
            Connections = new(configuration);
            Logins = new(configuration, Connections, _services);
            screen ??= new AcceptScreen();
            Mcp = new(Connections, screen, client);
            Previews = new(Connections, Logins, Mcp, screen, time);
            Tools = new(Mcp, Connections, Logins, Previews, screen);
        }

        internal Task ConnectAsync(bool compose = false) => Connections.AcceptAsync(Owner, Actor.PrincipalId, "fixture-subject", "me@example.com",
            "fixture-access", "fixture-refresh", $"openid email {GmailOAuthConfiguration.ReadScope}" + (compose ? $" {GmailOAuthConfiguration.ComposeScope}" : ""),
            "3600", compose, static commit => commit(), TestContext.Current.CancellationToken);

        public async ValueTask DisposeAsync()
        {
            await Mcp.DisposeAsync();
            Connections.Dispose();
            await _services.DisposeAsync();
        }
    }

    private static Tool NativeDefinition(string name) => new()
    {
        Name = name,
        Description = "Native fixture schema",
        InputSchema = JsonSerializer.Deserialize<JsonElement>(name == "create_draft"
            ? """{"type":"object","properties":{"to":{"type":"array","items":{"type":"string"}},"cc":{"type":"array","items":{"type":"string"}},"bcc":{"type":"array","items":{"type":"string"}},"subject":{"type":"string"},"body":{"type":"string"}}}"""
            : """{"type":"object","properties":{"query":{"type":"string"},"pageSize":{"type":"integer"},"includeTrash":{"type":"boolean"},"view":{"type":"string"}}}"""),
    };

    private static McpDiscoveredToolClient<GmailAgentIdentity> NativeClient(McpDiscoveredToolTests.FakeMcpServer server)
        => new(new McpStdioConnection { Name = "gmail", Command = "fixture", AllowedToolNames = [.. GmailMcp.NativeTools] },
            null, (_, cancellationToken) => server.ConnectAsync("gmail", cancellationToken));

    private sealed class NoRequests : IAgentRequests
    {
        public Task<AgentReply> RequestAsync<TAgent>(string instanceName, AgentRequest request, CancellationToken cancellationToken = default) where TAgent : IAgent
            => throw new InvalidOperationException("Gmail policy must not delegate.");
    }

    private sealed class AcceptScreen : IUntrustedContentScreen
    {
        public Task ScreenAsync(string content, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RejectConfirmationScreen : IUntrustedContentScreen
    {
        public Task ScreenAsync(string content, CancellationToken cancellationToken)
        {
            if (content.Contains("confirm gmail draft ", StringComparison.Ordinal))
            {
                throw new McpOperationException("Untrusted content cannot contain an application confirmation command.");
            }
            return Task.CompletedTask;
        }
    }

    private sealed class PreviewClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan duration) => _now += duration;
    }
}
