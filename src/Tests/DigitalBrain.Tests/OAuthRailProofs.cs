using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Modules.Sdk.Mcp;
using DigitalBrain.Tests.Harness;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DigitalBrain.Tests;

// Characterization of today's OAuth/MCP authorization rail (S1.3-RED).
// Defect pins carry // PIN-DEFECT(P0-x); only the seam session that fixes P0-x may flip them.
public sealed class OAuthRailCompositionProofs
{
    [Fact]
    public void ManualRailAndLibraryCallbackMintSeparateOAuthStates()
    {
        // PIN-DEFECT(P0-1): dual state — McpAuthorizationRail mints its own state for the
        // human sign-in URL; the MCP client library later mints a different state that
        // McpOAuthCallback extracts from AuthorizationUri. They are not the same flow/value.
        var rail = ReadRepoFile("src", "Modules", "SDK", "DigitalBrain.Modules.Sdk", "Mcp", "McpAuthorizationRail.cs");
        var callback = ReadRepoFile("src", "Modules", "SDK", "DigitalBrain.Modules.Sdk", "Mcp", "McpOAuthCallback.cs");
        var sessions = ReadRepoFile("src", "Modules", "SDK", "DigitalBrain.Modules.Sdk", "Mcp", "McpClientSessions.cs");

        Assert.Contains("var state = Guid.NewGuid().ToString(\"N\");", rail, StringComparison.Ordinal);
        Assert.Contains("SignInUrl(configuration, server, state)", rail, StringComparison.Ordinal);
        Assert.Contains("new BeginMcpAuthorization(", rail, StringComparison.Ordinal);

        Assert.Contains("McpOAuthOptions.Create", sessions, StringComparison.Ordinal);
        Assert.Contains("McpClient", sessions, StringComparison.Ordinal);
        Assert.Contains("CreateAsync", sessions, StringComparison.Ordinal);

        Assert.Contains("QueryValue(context.AuthorizationUri, \"state\")", callback, StringComparison.Ordinal);
        Assert.Contains("McpAuthorizationCodeHub.RegisterSession(state, session)", callback, StringComparison.Ordinal);
        Assert.Contains("new BeginMcpAuthorization(", callback, StringComparison.Ordinal);

        // Distinct mint sites: rail synthesizes Guid state; callback consumes library URI state.
        Assert.DoesNotContain("Guid.NewGuid().ToString(\"N\")", callback, StringComparison.Ordinal);
        Assert.DoesNotContain("context.AuthorizationUri", rail, StringComparison.Ordinal);
    }

    [Fact]
    public void ManuallyBuiltSalesforceAuthorizeUrlCarriesNoPkceChallenge()
    {
        // PIN-DEFECT(P0-1): manual URL lacks PKCE (no code_challenge / code_challenge_method).
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

        var signInUrl = InvokeSignInUrl(configuration, server, state);

        Assert.Equal("login.salesforce.com", signInUrl.Host);
        Assert.Contains("response_type=code", signInUrl.Query, StringComparison.Ordinal);
        Assert.Contains($"state={Uri.EscapeDataString(state)}", signInUrl.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("code_challenge", signInUrl.Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("code_challenge_method", signInUrl.Query, StringComparison.OrdinalIgnoreCase);

        var rail = ReadRepoFile("src", "Modules", "SDK", "DigitalBrain.Modules.Sdk", "Mcp", "McpAuthorizationRail.cs");
        Assert.DoesNotContain("code_challenge", rail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CodeHubCompletionsAccumulateUnknownStatesWithoutExpiryOrEviction()
    {
        // PIN-DEFECT(P0-1): McpAuthorizationCodeHub.Completions is an unbounded static
        // dictionary; Complete(state, null) for unknown/orphaned states never expires them.
        McpAuthorizationCodeHub.ResetForTests();
        Assert.Equal(0, McpAuthorizationCodeHub.CompletionsCountForTests);

        McpAuthorizationCodeHub.Complete("orphan-state-a", result: null);
        McpAuthorizationCodeHub.Complete("orphan-state-b", result: null);
        McpAuthorizationCodeHub.Complete("orphan-state-c", result: null);

        Assert.Equal(3, McpAuthorizationCodeHub.CompletionsCountForTests);

        // Still held — no background sweep, no TTL, no eviction on Complete alone.
        Assert.Equal(3, McpAuthorizationCodeHub.CompletionsCountForTests);

        McpAuthorizationCodeHub.ResetForTests();
        Assert.Equal(0, McpAuthorizationCodeHub.CompletionsCountForTests);
    }

    [Fact]
    public void McpAndGmailTokenPurposesKeyByServerAndNeuronIdentityNotPrincipal()
    {
        // PIN-DEFECT(P0-5): tokens stored under server key + durable neuron identity, not a
        // verified local user principal.
        var serverKey = "dev/salesforce";
        var durableIdentity = new NeuronId("mcp", new OwnerId("dev"), "salesforce").ToString();
        Assert.Equal("mcp:dev/salesforce", durableIdentity);

        var purpose = McpTokenPresence.Purpose(serverKey, durableIdentity);
        Assert.Equal($"mcp/oauth/{serverKey}/{durableIdentity}", purpose);
        Assert.DoesNotContain("principal", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user", purpose, StringComparison.OrdinalIgnoreCase);

        var mcpNeuron = ReadRepoFile("src", "Modules", "SDK", "DigitalBrain.Modules.Sdk", "Mcp", "McpServerNeuron.cs");
        Assert.Contains("_durableIdentity = Id.ToString();", mcpNeuron, StringComparison.Ordinal);
        Assert.Contains("McpAuthorizationRail.EnsureAuthorizedAsync", mcpNeuron, StringComparison.Ordinal);
        Assert.Contains("McpClientSessions.RunAsync", mcpNeuron, StringComparison.Ordinal);
        Assert.Contains("_durableIdentity", mcpNeuron, StringComparison.Ordinal);

        var rail = ReadRepoFile("src", "Modules", "SDK", "DigitalBrain.Modules.Sdk", "Mcp", "McpAuthorizationRail.cs");
        Assert.Contains("McpTokenPresence.Purpose(server.Key, durableIdentity)", rail, StringComparison.Ordinal);

        var sessions = ReadRepoFile("src", "Modules", "SDK", "DigitalBrain.Modules.Sdk", "Mcp", "McpClientSessions.cs");
        Assert.Contains("McpTokenPresence.Purpose(server.Key, durableIdentity)", sessions, StringComparison.Ordinal);

        var gmail = ReadRepoFile("src", "Modules", "Google", "Google", "Gmail", "Gmail.cs");
        Assert.Contains("_durableIdentity = Id.ToString();", gmail, StringComparison.Ordinal);
        Assert.Contains("_userKey = Id.Name;", gmail, StringComparison.Ordinal);

        var googleStore = ReadRepoFile(
            "src", "Modules", "Google", "Google", "Auth", "DurableGoogleTokenStore.cs");
        Assert.Contains(
            "google/oauth/{connectionName}/{durableIdentity}",
            googleStore,
            StringComparison.Ordinal);

        // PendingAuthorization durable shape carries no principal stamp.
        var pendingNames = typeof(PendingAuthorization)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [
                "Code",
                "CommandId",
                "CompletionNotified",
                "CompletionTarget",
                "Iss",
                "Outcome",
                "RequestingNeuron",
                "ServerDisplayName",
                "ServerKey",
                "SignInUrl",
                "State",
            ],
            pendingNames);
        foreach (var name in pendingNames)
        {
            Assert.False(
                name.Contains("Principal", StringComparison.OrdinalIgnoreCase)
                || name.Equals("UserId", StringComparison.OrdinalIgnoreCase)
                || name.Equals("User", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Actor", StringComparison.OrdinalIgnoreCase),
                $"PendingAuthorization unexpectedly carries caller-identity property '{name}'.");
        }
    }

    [Fact]
    public void OAuthCallbackDeliveryIgnoresAuthenticatedPrincipal()
    {
        // PIN-DEFECT(P0-5): /oauth/callback does not bind state to any local user identity
        // (AllowAnonymous; delivery carries only State/Code/Error/Iss).
        var callback = ReadRepoFile("src", "Kernel", "DigitalBrain.Kernel", "MapOAuthCallback.cs");
        Assert.Contains("HttpSurfacePaths.McpOAuthCallbackPath", callback, StringComparison.Ordinal);
        Assert.Contains("DeliverMcpAuthorizationCallback", callback, StringComparison.Ordinal);
        Assert.Contains("AllowAnonymous", callback, StringComparison.Ordinal);
        Assert.DoesNotContain("ClaimsPrincipal", callback, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpContext.User", callback, StringComparison.Ordinal);
        Assert.DoesNotContain("PrincipalId", callback, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpActor", callback, StringComparison.Ordinal);
        Assert.DoesNotContain("User.Identity", callback, StringComparison.Ordinal);

        var deliveryNames = typeof(DeliverMcpAuthorizationCallback)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["Code", "Error", "Iss", "State"], deliveryNames);

        Assert.Equal("/oauth/callback", HttpSurfacePaths.McpOAuthCallbackPath);
        Assert.Equal(OAuthCallbackPaths.RelativePath, HttpSurfacePaths.McpOAuthCallbackPath);
    }

    private static Uri InvokeSignInUrl(IConfiguration configuration, McpServerDefinition server, string state)
    {
        var method = typeof(McpAuthorizationRail).GetMethod(
            "SignInUrl",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("McpAuthorizationRail.SignInUrl is missing.");
        return (Uri)method.Invoke(null, [configuration, server, state])!;
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
    [Fact]
    public async Task CompletedAuthorizationCodeCanBeReplayedWithoutRefusal()
    {
        // PIN-DEFECT(P0-1): completed state/code stays replayable — second DeliverCallback
        // on a Completed pending entry is Accepted+Completed and TakeCompletedCode still yields.
        McpAuthorizationCodeHub.ResetForTests();
        var brain = fixture.BrainFor("oauth-replay");
        var authorization = brain.GetGrainProxy<IMcpAuthorization>(IMcpAuthorization.DefaultInstanceName);
        var command = CommandId.New();
        const string state = "replay-state-1";
        const string code = "auth-code-once";

        await authorization.Begin(
            new BeginMcpAuthorization(
                command,
                "salesforce",
                "Salesforce",
                new Uri("https://login.salesforce.com/services/oauth2/authorize?state=replay-state-1"),
                state),
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

        // Replay the same completed state (even with a different presented code).
        var second = await authorization.DeliverCallback(
            new DeliverMcpAuthorizationCallback(state, "different-code", null, null),
            TestContext.Current.CancellationToken);
        Assert.True(second.Accepted);
        Assert.True(second.Completed);
        Assert.False(second.Denied);

        var takenAgain = await authorization.TakeCompletedCode(state, TestContext.Current.CancellationToken);
        Assert.NotNull(takenAgain);
        Assert.Equal(code, takenAgain!.Code);

        McpAuthorizationCodeHub.ResetForTests();
    }

    [Fact]
    public async Task UnknownCallbackStatesAreRejectedAtTheGrainButStillFillTheCodeHub()
    {
        // PIN-DEFECT(P0-1): DeliverCallback refuses unknown states at the grain, yet still
        // parks them in the static Completions dictionary (no expiry).
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
        Assert.Equal(before + 1, McpAuthorizationCodeHub.CompletionsCountForTests);

        McpAuthorizationCodeHub.ResetForTests();
    }

    [Fact]
    public async Task AuthorizationPendingShapeHasNoLocalUserBinding()
    {
        // PIN-DEFECT(P0-5): Begin records server/state only — no principal on the emitted fact.
        var brain = fixture.BrainFor("oauth-no-principal");
        var authorization = brain.GetGrainProxy<IMcpAuthorization>(IMcpAuthorization.DefaultInstanceName);
        var command = CommandId.New();
        const string state = "no-principal-state";

        var required = await authorization.Begin(
            new BeginMcpAuthorization(
                command,
                "salesforce",
                "Salesforce",
                new Uri("https://login.salesforce.com/services/oauth2/authorize?state=no-principal-state"),
                state),
            TestContext.Current.CancellationToken);

        Assert.Equal(command, required.CommandId);
        Assert.Equal("salesforce", required.ServerKey);
        Assert.Equal(state, required.State);
        Assert.Equal(
            ["CommandId", "ServerDisplayName", "ServerKey", "SignInUrl", "State"],
            typeof(AuthorizationRequired)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }
}
