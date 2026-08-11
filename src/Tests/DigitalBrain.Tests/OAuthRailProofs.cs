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

    [Theory]
    [InlineData(
        "salesforce",
        "Salesforce",
        "DigitalBrain:Salesforce",
        "login.salesforce.com",
        "api",
        "refresh_token")]
    [InlineData(
        "google.gmail",
        "Gmail",
        "DigitalBrain:Google:Gmail",
        "accounts.google.com",
        "https://www.googleapis.com/auth/gmail.readonly",
        "https://www.googleapis.com/auth/gmail.compose")]
    public void ProviderAuthorizeUrlAlwaysCarriesPkceS256(
        string serverKey,
        string displayName,
        string configurationRoot,
        string expectedHost,
        string scopeA,
        string scopeB)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{configurationRoot}:ClientId"] = $"{serverKey}-client-id",
                [$"{configurationRoot}:RedirectUri"] = "https://app.example/oauth/callback",
            })
            .Build();
        var server = new McpServerDefinition(
            serverKey,
            displayName,
            new Uri("https://example.com/mcp"),
            configurationRoot,
            [scopeA, scopeB],
            requiresClientSecret: true);
        var state = Guid.NewGuid().ToString("N");
        var (_, challenge) = OAuthPkce.CreateS256Pair();

        var signInUrl = McpAuthorizationRail.BuildPkceAuthorizeUrl(configuration, server, state, challenge);

        Assert.Equal(expectedHost, signInUrl.Host);
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

    [Theory]
    [InlineData("salesforce")]
    [InlineData("google.gmail")]
    public void McpTokenPurposesKeyByPrincipalNotNeuronIdentity(string serverKey)
    {
        var actor = new ActorContext(PrincipalId.New(), "alice");
        var purpose = McpTokenPresence.Purpose(serverKey, IntegrationScope.User, McpTokenPresence.SubjectKey(actor));

        Assert.Equal(
            $"integration/user/{serverKey}/{McpTokenPresence.SubjectKey(actor)}",
            purpose);
        Assert.Contains("integration/user/", purpose, StringComparison.Ordinal);
        Assert.DoesNotContain($"mcp:{serverKey}", purpose, StringComparison.Ordinal);
        Assert.DoesNotContain("google/oauth/", purpose, StringComparison.Ordinal);

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

    [Theory]
    [InlineData("salesforce", "Salesforce", "https://login.salesforce.com/services/oauth2/authorize")]
    [InlineData("google.gmail", "Gmail", "https://accounts.google.com/o/oauth2/v2/auth")]
    public async Task CompletedAuthorizationCodeIsOneShotAndReplayIsRefused(
        string serverKey,
        string displayName,
        string authorizeBase)
    {
        McpAuthorizationCodeHub.ResetForTests();
        var brain = fixture.BrainFor($"oauth-replay-{serverKey.Replace('.', '-')}");
        var authorization = brain.GetGrainProxy<IMcpAuthorization>(IMcpAuthorization.DefaultInstanceName);
        var command = CommandId.New();
        var state = $"replay-state-{serverKey}";
        const string code = "auth-code-once";
        var actor = TestActor();

        await authorization.Begin(
            new BeginMcpAuthorization(
                command,
                serverKey,
                displayName,
                new Uri($"{authorizeBase}?state={Uri.EscapeDataString(state)}&code_challenge=abc&code_challenge_method=S256"),
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

        // ClientEntryPoint surface has no TakeCompletedCode — one-shot is proved by
        // refusing a second Deliver and completing the claim (host rail consumes codes).
        var second = await authorization.DeliverCallback(
            new DeliverMcpAuthorizationCallback(state, "different-code", null, null),
            TestContext.Current.CancellationToken);
        Assert.False(second.Accepted);
        Assert.False(second.Completed);
        Assert.False(second.Denied);

        var claim = await authorization.Claim(command, actor, TestContext.Current.CancellationToken);
        Assert.Equal(McpAuthorizationClaimKind.Completed, claim.Kind);

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

    [Theory]
    [InlineData("salesforce", "Salesforce", "https://login.salesforce.com/services/oauth2/authorize")]
    [InlineData("google.gmail", "Gmail", "https://accounts.google.com/o/oauth2/v2/auth")]
    public async Task AuthorizationPendingBindsTheLocalUserPrincipal(
        string serverKey,
        string displayName,
        string authorizeBase)
    {
        var brain = fixture.BrainFor($"oauth-principal-{serverKey.Replace('.', '-')}");
        var authorization = brain.GetGrainProxy<IMcpAuthorization>(IMcpAuthorization.DefaultInstanceName);
        var command = CommandId.New();
        var state = $"principal-state-{serverKey}";
        var actor = TestActor("bob");

        var required = await authorization.Begin(
            new BeginMcpAuthorization(
                command,
                serverKey,
                displayName,
                new Uri($"{authorizeBase}?state={Uri.EscapeDataString(state)}&code_challenge=x&code_challenge_method=S256"),
                state,
                actor,
                CodeChallenge: "x",
                CodeVerifier: "v"),
            TestContext.Current.CancellationToken);

        Assert.Equal(command, required.CommandId);
        Assert.Equal(serverKey, required.ServerKey);
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

    [Theory]
    [InlineData("salesforce", "Salesforce", "https://login.salesforce.com/services/oauth2/authorize")]
    [InlineData("google.gmail", "Gmail", "https://accounts.google.com/o/oauth2/v2/auth")]
    public async Task PrincipalTokenSlotsIsolateUserAFromUserB(
        string serverKey,
        string displayName,
        string authorizeBase)
    {
        var alice = new ActorContext(PrincipalId.New(), "alice");
        var bob = new ActorContext(PrincipalId.New(), "bob");
        var alicePurpose = McpTokenPresence.UserIntegration(serverKey, alice, ["api"]).ProtectedTokenReference;
        var bobPurpose = McpTokenPresence.UserIntegration(serverKey, bob, ["api"]).ProtectedTokenReference;

        Assert.NotEqual(alicePurpose, bobPurpose);
        Assert.Contains(McpTokenPresence.SubjectKey(alice), alicePurpose, StringComparison.Ordinal);
        Assert.DoesNotContain(McpTokenPresence.SubjectKey(alice), bobPurpose, StringComparison.Ordinal);
        Assert.Contains(McpTokenPresence.SubjectKey(bob), bobPurpose, StringComparison.Ordinal);

        // Live grain: Begin for alice; bob cannot claim alice's state via a different Begin on same state.
        var brain = fixture.BrainFor($"oauth-isolation-{serverKey.Replace('.', '-')}");
        var authorization = brain.GetGrainProxy<IMcpAuthorization>(IMcpAuthorization.DefaultInstanceName);
        var state = $"iso-state-{serverKey}";
        await authorization.Begin(
            new BeginMcpAuthorization(
                CommandId.New(),
                serverKey,
                displayName,
                new Uri($"{authorizeBase}?state={Uri.EscapeDataString(state)}&code_challenge=c&code_challenge_method=S256"),
                state,
                alice,
                "c",
                "v"),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await authorization.Begin(
                new BeginMcpAuthorization(
                    CommandId.New(),
                    serverKey,
                    displayName,
                    new Uri($"{authorizeBase}?state={Uri.EscapeDataString(state)}&code_challenge=c2&code_challenge_method=S256"),
                    state,
                    bob,
                    "c2",
                    "v2"),
                TestContext.Current.CancellationToken));
    }
}
