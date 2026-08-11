using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Modules.Sdk.Mcp;
using DigitalBrain.Tests.Harness;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DigitalBrain.Tests;

// S1.3-GREEN: flipped P0-1 / P0-5 pins — PKCE single mint, bounded one-shot state, principal binding.
public sealed class OAuthRailCompositionProofs
{
    [Fact]
    public void AuthorizationRailIsTheSoleStateMintAndLibraryCallbackDoesNotMint()
    {
        var rail = ReadRepoFile("src", "Modules", "SDK", "DigitalBrain.Modules.Sdk", "Mcp", "McpAuthorizationRail.cs");
        var callback = ReadRepoFile("src", "Modules", "SDK", "DigitalBrain.Modules.Sdk", "Mcp", "McpOAuthCallback.cs");

        Assert.Contains("OAuthPkce.CreateS256Pair", rail, StringComparison.Ordinal);
        Assert.Contains("BuildPkceAuthorizeUrl", rail, StringComparison.Ordinal);
        Assert.Contains("new BeginMcpAuthorization(", rail, StringComparison.Ordinal);

        // Library callback recovers the rail transaction — no Guid mint, no AuthorizationUri state mint.
        Assert.DoesNotContain("Guid.NewGuid().ToString(\"N\")", callback, StringComparison.Ordinal);
        Assert.DoesNotContain("QueryValue(context.AuthorizationUri, \"state\")", callback, StringComparison.Ordinal);
        Assert.Contains("Claim(session.CommandId", callback, StringComparison.Ordinal);
        Assert.Contains("does not mint state", callback, StringComparison.Ordinal);
    }

    [Fact]
    public void SalesforceAuthorizeUrlAlwaysCarriesPkceS256()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:Salesforce:ClientId"] = "sf-client-id",
                ["DigitalBrain:Salesforce:RedirectUri"] = "https://app.example/oauth/callback",
            })
            .Build();
        var server = new McpServerDefinition(
            "salesforce",
            "Salesforce",
            new Uri("https://example.com/mcp"),
            "DigitalBrain:Salesforce",
            ["api", "refresh_token"],
            requiresClientSecret: true);
        var state = Guid.NewGuid().ToString("N");
        var (_, challenge) = OAuthPkce.CreateS256Pair();

        var signInUrl = McpAuthorizationRail.BuildPkceAuthorizeUrl(configuration, server, state, challenge);

        Assert.Equal("login.salesforce.com", signInUrl.Host);
        Assert.Contains("response_type=code", signInUrl.Query, StringComparison.Ordinal);
        Assert.Contains($"state={Uri.EscapeDataString(state)}", signInUrl.Query, StringComparison.Ordinal);
        Assert.Contains("code_challenge=", signInUrl.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("code_challenge_method=S256", signInUrl.Query, StringComparison.OrdinalIgnoreCase);

        var rail = ReadRepoFile("src", "Modules", "SDK", "DigitalBrain.Modules.Sdk", "Mcp", "McpAuthorizationRail.cs");
        Assert.Contains("code_challenge", rail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OAuthPkce.ChallengeMethodS256", rail, StringComparison.Ordinal);
    }

    [Fact]
    public void CodeHubDropsUnknownStatesInsteadOfAccumulatingOrphans()
    {
        McpAuthorizationCodeHub.ResetForTests();
        Assert.Equal(0, McpAuthorizationCodeHub.CompletionsCountForTests);

        McpAuthorizationCodeHub.Complete("orphan-state-a", result: null);
        McpAuthorizationCodeHub.Complete("orphan-state-b", result: null);
        McpAuthorizationCodeHub.Complete("orphan-state-c", result: null);

        Assert.Equal(0, McpAuthorizationCodeHub.CompletionsCountForTests);

        McpAuthorizationCodeHub.ResetForTests();
    }

    [Fact]
    public void McpTokenPurposesKeyByPrincipalNotNeuronIdentity()
    {
        var actor = new ActorContext(PrincipalId.New(), "alice");
        var serverKey = "dev/salesforce";
        var purpose = McpTokenPresence.Purpose(serverKey, IntegrationScope.User, McpTokenPresence.SubjectKey(actor));

        Assert.Equal(
            $"integration/user/{serverKey}/{McpTokenPresence.SubjectKey(actor)}",
            purpose);
        Assert.Contains("integration/user/", purpose, StringComparison.Ordinal);
        Assert.DoesNotContain("mcp:dev/salesforce", purpose, StringComparison.Ordinal);

        var integration = McpTokenPresence.UserIntegration(serverKey, actor, ["api"]);
        Assert.Equal(IntegrationScope.User, integration.Scope);
        Assert.Equal(McpTokenPresence.SubjectKey(actor), integration.SubjectId);
        Assert.Equal(purpose, integration.ProtectedTokenReference);

        var mcpNeuron = ReadRepoFile("src", "Modules", "SDK", "DigitalBrain.Modules.Sdk", "Mcp", "McpServerNeuron.cs");
        Assert.Contains("PrincipalTokenSlot", mcpNeuron, StringComparison.Ordinal);
        Assert.Contains("McpTokenPresence.SubjectKey", mcpNeuron, StringComparison.Ordinal);
        Assert.Contains("mcp.gateway.oauth.principals", mcpNeuron, StringComparison.Ordinal);

        var rail = ReadRepoFile("src", "Modules", "SDK", "DigitalBrain.Modules.Sdk", "Mcp", "McpAuthorizationRail.cs");
        Assert.Contains("McpTokenPresence.UserIntegration", rail, StringComparison.Ordinal);
        Assert.Contains("ActorContext actor", rail, StringComparison.Ordinal);

        var pendingNames = typeof(PendingAuthorization)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Contains("Actor", pendingNames);
        Assert.Contains("ExpiresAt", pendingNames);
        Assert.Contains("Consumed", pendingNames);
        Assert.Contains("CodeChallenge", pendingNames);
    }

    [Fact]
    public void OAuthCallbackStaysAnonymousAtHttpAndResolvesOnlyThroughStateBinding()
    {
        var callback = ReadRepoFile("src", "Kernel", "DigitalBrain.Kernel", "MapOAuthCallback.cs");
        Assert.Contains("HttpSurfacePaths.McpOAuthCallbackPath", callback, StringComparison.Ordinal);
        Assert.Contains("DeliverMcpAuthorizationCallback", callback, StringComparison.Ordinal);
        Assert.Contains("AllowAnonymous", callback, StringComparison.Ordinal);
        Assert.DoesNotContain("ClaimsPrincipal", callback, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpActor", callback, StringComparison.Ordinal);

        var deliveryNames = typeof(DeliverMcpAuthorizationCallback)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["Code", "Error", "Iss", "State"], deliveryNames);

        Assert.Equal("/oauth/callback", HttpSurfacePaths.McpOAuthCallbackPath);
        Assert.Equal(OAuthCallbackPaths.RelativePath, HttpSurfacePaths.McpOAuthCallbackPath);

        // Principal binding lives on the pending record (mint-time), not the HTTP surface.
        Assert.Contains(
            "Actor",
            typeof(PendingAuthorization)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name));
    }

    private static string ReadRepoFile(params string[] relativeParts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, .. relativeParts]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate {string.Join('/', relativeParts)} from the test base directory.");
    }
}

[Collection(BrainCollection.Name)]
public sealed class OAuthRailNeuronProofs(BrainClusterFixture fixture)
{
    private static ActorContext TestActor(string name = "alice")
        => new(PrincipalId.New(), name);

    [Fact]
    public async Task CompletedAuthorizationCodeIsOneShotAndReplayIsRefused()
    {
        McpAuthorizationCodeHub.ResetForTests();
        var brain = fixture.BrainFor("oauth-replay");
        var authorization = brain.GetGrainProxy<IMcpAuthorization>(IMcpAuthorization.DefaultInstanceName);
        var command = CommandId.New();
        const string state = "replay-state-1";
        const string code = "auth-code-once";
        var actor = TestActor();

        await authorization.Begin(
            new BeginMcpAuthorization(
                command,
                "salesforce",
                "Salesforce",
                new Uri("https://login.salesforce.com/services/oauth2/authorize?state=replay-state-1&code_challenge=abc&code_challenge_method=S256"),
                state,
                actor,
                CodeChallenge: "abc",
                CodeVerifier: "verifier-1"),
            TestContext.Current.CancellationToken);

        var first = await authorization.DeliverCallback(
            new DeliverMcpAuthorizationCallback(state, code, null, null),
            TestContext.Current.CancellationToken);
        Assert.True(first.Accepted);
        Assert.True(first.Completed);
        Assert.False(first.Denied);

        var takenOnce = await authorization.TakeCompletedCode(state, TestContext.Current.CancellationToken);
        Assert.NotNull(takenOnce);
        Assert.Equal(code, takenOnce!.Code);
        Assert.Equal("verifier-1", takenOnce.CodeVerifier);
        Assert.Equal(actor.PrincipalId, takenOnce.Actor!.PrincipalId);

        // Replay the same completed state — refused (one-shot).
        var second = await authorization.DeliverCallback(
            new DeliverMcpAuthorizationCallback(state, "different-code", null, null),
            TestContext.Current.CancellationToken);
        Assert.False(second.Accepted);
        Assert.False(second.Completed);
        Assert.False(second.Denied);

        var takenAgain = await authorization.TakeCompletedCode(state, TestContext.Current.CancellationToken);
        Assert.Null(takenAgain);

        McpAuthorizationCodeHub.ResetForTests();
    }

    [Fact]
    public async Task UnknownCallbackStatesAreRejectedAndDoNotFillTheCodeHub()
    {
        McpAuthorizationCodeHub.ResetForTests();
        var brain = fixture.BrainFor("oauth-unknown-state");
        var authorization = brain.GetGrainProxy<IMcpAuthorization>(IMcpAuthorization.DefaultInstanceName);
        var before = McpAuthorizationCodeHub.CompletionsCountForTests;

        var delivery = await authorization.DeliverCallback(
            new DeliverMcpAuthorizationCallback("never-begun-state", "code", null, null),
            TestContext.Current.CancellationToken);

        Assert.False(delivery.Accepted);
        Assert.False(delivery.Completed);
        Assert.False(delivery.Denied);
        Assert.Equal(before, McpAuthorizationCodeHub.CompletionsCountForTests);

        McpAuthorizationCodeHub.ResetForTests();
    }

    [Fact]
    public async Task AuthorizationPendingBindsTheLocalUserPrincipal()
    {
        var brain = fixture.BrainFor("oauth-principal");
        var authorization = brain.GetGrainProxy<IMcpAuthorization>(IMcpAuthorization.DefaultInstanceName);
        var command = CommandId.New();
        const string state = "principal-state";
        var actor = TestActor("bob");

        var required = await authorization.Begin(
            new BeginMcpAuthorization(
                command,
                "salesforce",
                "Salesforce",
                new Uri("https://login.salesforce.com/services/oauth2/authorize?state=principal-state&code_challenge=x&code_challenge_method=S256"),
                state,
                actor,
                CodeChallenge: "x",
                CodeVerifier: "v"),
            TestContext.Current.CancellationToken);

        Assert.Equal(command, required.CommandId);
        Assert.Equal("salesforce", required.ServerKey);
        Assert.Equal(state, required.State);
        Assert.NotNull(required.Actor);
        Assert.Equal(actor.PrincipalId, required.Actor!.PrincipalId);
        Assert.Equal("bob", required.Actor.Username);

        var names = typeof(AuthorizationRequired)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Contains("Actor", names);
    }

    [Fact]
    public async Task PrincipalTokenSlotsIsolateUserAFromUserB()
    {
        var alice = new ActorContext(PrincipalId.New(), "alice");
        var bob = new ActorContext(PrincipalId.New(), "bob");
        var alicePurpose = McpTokenPresence.UserIntegration("salesforce", alice, ["api"]).ProtectedTokenReference;
        var bobPurpose = McpTokenPresence.UserIntegration("salesforce", bob, ["api"]).ProtectedTokenReference;

        Assert.NotEqual(alicePurpose, bobPurpose);
        Assert.Contains(McpTokenPresence.SubjectKey(alice), alicePurpose, StringComparison.Ordinal);
        Assert.DoesNotContain(McpTokenPresence.SubjectKey(alice), bobPurpose, StringComparison.Ordinal);
        Assert.Contains(McpTokenPresence.SubjectKey(bob), bobPurpose, StringComparison.Ordinal);

        // Live grain: Begin for alice; bob cannot claim alice's state via a different Begin on same state.
        var brain = fixture.BrainFor("oauth-isolation");
        var authorization = brain.GetGrainProxy<IMcpAuthorization>(IMcpAuthorization.DefaultInstanceName);
        const string state = "iso-state";
        await authorization.Begin(
            new BeginMcpAuthorization(
                CommandId.New(),
                "salesforce",
                "Salesforce",
                new Uri("https://login.salesforce.com/services/oauth2/authorize?state=iso-state&code_challenge=c&code_challenge_method=S256"),
                state,
                alice,
                "c",
                "v"),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await authorization.Begin(
                new BeginMcpAuthorization(
                    CommandId.New(),
                    "salesforce",
                    "Salesforce",
                    new Uri("https://login.salesforce.com/services/oauth2/authorize?state=iso-state&code_challenge=c2&code_challenge_method=S256"),
                    state,
                    bob,
                    "c2",
                    "v2"),
                TestContext.Current.CancellationToken));
    }
}
