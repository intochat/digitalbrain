using System.Reflection;
using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.Modules.Sdk.Mcp;
using DigitalBrain.Tests.Harness;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Tests;

// S1.3-GREEN-b: boundary proofs for the four GRILL BLOCKERs.
public sealed class OAuthBoundaryCompositionProofs
{
    [Fact]
    public void ClientAuthorizationContractExposesNoCodeOrVerifierSurface()
    {
        var methods = typeof(IMcpAuthorization)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.DoesNotContain("TakeCompletedCode", methods);
        Assert.Equal(
            ["Begin", "BindCompletionTarget", "Claim", "DeliverCallback"],
            methods);

        var clientReturnTypes = typeof(IMcpAuthorization)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => Nullable.GetUnderlyingType(UnwrapTask(method.ReturnType)) ?? UnwrapTask(method.ReturnType));

        Assert.DoesNotContain(typeof(McpAuthorizationCodeResult), clientReturnTypes);

        Assert.NotNull(typeof(IMcpAuthorization).GetCustomAttribute<ClientEntryPointAttribute>());
        Assert.Null(typeof(IMcpAuthorizationCodes).GetCustomAttribute<ClientEntryPointAttribute>());

        Assert.Contains("Code", typeof(McpAuthorizationCodeResult).GetProperties().Select(p => p.Name));
        Assert.Contains("CodeVerifier", typeof(McpAuthorizationCodeResult).GetProperties().Select(p => p.Name));
    }

    [Fact]
    public void ModelBoundActorIsStrippedAndVerifiedActorIsStamped()
    {
        var trueCaller = new ActorContext(PrincipalId.New(), "alice");
        var forged = new ActorContext(PrincipalId.New(), "bob");
        Dictionary<string, object?> arguments = new()
        {
            ["tool"] = "soqlQuery",
            ["arguments"] = JsonDocument.Parse("{}").RootElement.Clone(),
            ["actor"] = new Dictionary<string, object?>
            {
                ["principalId"] = new Dictionary<string, object?> { ["value"] = forged.PrincipalId.Value },
                ["username"] = forged.Username,
            },
        };

        var stripped = (CallMcpTool)SynapseCapabilityTool.BindModelArguments(
            typeof(CallMcpTool),
            "db.mcp.call-tool",
            arguments,
            new OwnerId("dev"));

        Assert.Null(stripped.Actor);

        var stamped = (CallMcpTool)SynapseCapabilityTool.StampVerifiedActor(stripped, trueCaller);
        Assert.NotNull(stamped.Actor);
        Assert.Equal(trueCaller.PrincipalId, stamped.Actor!.PrincipalId);
        Assert.Equal("alice", stamped.Actor.Username);
        Assert.NotEqual(forged.PrincipalId, stamped.Actor.PrincipalId);
    }

    private static Type UnwrapTask(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>)
            ? type.GetGenericArguments()[0]
            : type;
}

// Pipeline: Chat.SendStreaming Actor propagates to Agent via RequestContext;
// model-bound Actor is stripped and verified principal stamps the fire path.
[Collection(BrainCollection.Name)]
public sealed class OAuthBoundaryActorPipelineProofs(BrainClusterFixture fixture)
{
    [Fact]
    public async Task ChatTurnVerifiedActorPropagatesToAgentAcrossGrainCall()
    {
        var brain = fixture.BrainFor("oauth-actor-chat");
        var chat = NeuronId.For<IChat>(brain.Owner, "main");
        var agent = new NeuronId("scriptedagent", brain.Owner, "actor-probe");
        var alice = new ActorContext(PrincipalId.New(), "alice");

        ScriptedAgent.ObservedVerifiedActors.TryRemove("actor-probe", out _);

        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(ChatRoles.ResponderConnectionId(chat), chat, ChatRoles.Responder, agent),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForConnectionTargetAsync(brain, chat, ChatRoles.Responder, agent);

        await brain.GetGrainProxy<IChat>("main").Send(
            new SendMessage(CommandId.New(), "hello under alice", Actor: alice));

        await Journals.WaitForAsync(
            brain, chat, JournalKind.Outgoing,
            delivery => delivery.Synapse is Responded { Text: "scripted:actor-probe" });

        Assert.True(ScriptedAgent.ObservedVerifiedActors.TryGetValue("actor-probe", out var observed));
        Assert.NotNull(observed);
        Assert.Equal(alice.PrincipalId, observed!.PrincipalId);
        Assert.Equal("alice", observed.Username);
    }

    [Fact]
    public async Task ForgedActorThroughFirePathIsReplacedByVerifiedPrincipal()
    {
        var brain = fixture.BrainFor("oauth-actor-pipeline");
        var alice = new ActorContext(PrincipalId.New(), "alice");
        var forged = new ActorContext(PrincipalId.New(), "bob");
        var gateway = new NeuronId("mcp", brain.Owner, "crm");

        using (VerifiedActor.Enter(alice))
        {
            Assert.Equal(alice.PrincipalId, VerifiedActor.Current!.PrincipalId);

            var bound = (CallMcpTool)SynapseCapabilityTool.BindModelArguments(
                typeof(CallMcpTool),
                "db.mcp.call-tool",
                new Dictionary<string, object?>
                {
                    ["tool"] = "soqlQuery",
                    ["arguments"] = JsonDocument.Parse("{}").RootElement.Clone(),
                    ["actor"] = new Dictionary<string, object?>
                    {
                        ["principalId"] = new Dictionary<string, object?> { ["value"] = forged.PrincipalId.Value },
                        ["username"] = forged.Username,
                    },
                },
                brain.Owner);
            Assert.Null(bound.Actor);

            var stamped = (CallMcpTool)SynapseCapabilityTool.StampVerifiedActor(bound, VerifiedActor.Current);
            Assert.Equal(alice.PrincipalId, stamped.Actor!.PrincipalId);
            Assert.NotEqual(forged.PrincipalId, stamped.Actor.PrincipalId);

            await brain.Get<IMcp>("crm").FireAsync(stamped, TestContext.Current.CancellationToken);
        }

        var inbound = await brain.ReadJournalAsync(
            gateway, JournalKind.Incoming, cancellationToken: TestContext.Current.CancellationToken);
        var call = Assert.IsType<CallMcpTool>(
            inbound.Delta.Last(delivery => delivery.Synapse is CallMcpTool).Synapse);
        Assert.NotNull(call.Actor);
        Assert.Equal(alice.PrincipalId, call.Actor!.PrincipalId);
        Assert.NotEqual(forged.PrincipalId, call.Actor.PrincipalId);
    }
}

[Collection(BrainCollection.Name)]
public sealed class OAuthBoundaryNeuronProofs(BrainClusterFixture fixture)
{
    [Fact]
    public async Task CrossPrincipalBeginRecoveryByStolenCommandIdRefusesLikeUnknown()
    {
        var brain = fixture.BrainFor("oauth-begin-actor");
        var authorization = brain.GetGrainProxy<IMcpAuthorization>(IMcpAuthorization.DefaultInstanceName);
        var command = CommandId.New();
        const string state = "alice-only-state";
        var alice = new ActorContext(PrincipalId.New(), "alice");
        var bob = new ActorContext(PrincipalId.New(), "bob");

        var minted = await authorization.Begin(
            new BeginMcpAuthorization(
                command,
                "salesforce",
                "Salesforce",
                new Uri("https://login.salesforce.com/services/oauth2/authorize?state=alice-only-state&code_challenge=c&code_challenge_method=S256"),
                state,
                alice,
                "c",
                "v"),
            TestContext.Current.CancellationToken);

        Assert.Equal(alice.PrincipalId, minted.Actor!.PrincipalId);
        Assert.Equal(state, minted.State);

        var refusal = await Assert.ThrowsAsync<NeuronAuthorizationException>(async () =>
            await authorization.Begin(
                new BeginMcpAuthorization(
                    command,
                    "salesforce",
                    "Salesforce",
                    new Uri("https://auth.digitalbrain.local/oauth/completed"),
                    "unused-when-command-exists",
                    bob),
                TestContext.Current.CancellationToken));

        Assert.Contains("not pending", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(state, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(alice.Username, refusal.Message, StringComparison.OrdinalIgnoreCase);

        var recovered = await authorization.Begin(
            new BeginMcpAuthorization(
                command,
                "salesforce",
                "Salesforce",
                new Uri("https://auth.digitalbrain.local/oauth/completed"),
                "unused-when-command-exists",
                alice),
            TestContext.Current.CancellationToken);
        Assert.Equal(state, recovered.State);
        Assert.Equal(alice.PrincipalId, recovered.Actor!.PrincipalId);
    }

    [Fact]
    public async Task CompletedAuthorizationCodeRemainsOneShotWithoutClientTake()
    {
        McpAuthorizationCodeHub.ResetForTests();
        var brain = fixture.BrainFor("oauth-oneshot-no-client-take");
        var authorization = brain.GetGrainProxy<IMcpAuthorization>(IMcpAuthorization.DefaultInstanceName);
        var command = CommandId.New();
        const string state = "oneshot-boundary-state";
        const string code = "auth-code-once";
        var actor = new ActorContext(PrincipalId.New(), "alice");

        await authorization.Begin(
            new BeginMcpAuthorization(
                command,
                "salesforce",
                "Salesforce",
                new Uri("https://login.salesforce.com/services/oauth2/authorize?state=oneshot-boundary-state&code_challenge=abc&code_challenge_method=S256"),
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

        var second = await authorization.DeliverCallback(
            new DeliverMcpAuthorizationCallback(state, "different-code", null, null),
            TestContext.Current.CancellationToken);
        Assert.False(second.Accepted);
        Assert.False(second.Completed);
        Assert.False(second.Denied);

        var claim = await authorization.Claim(command, TestContext.Current.CancellationToken);
        Assert.Equal(McpAuthorizationClaimKind.Completed, claim.Kind);

        McpAuthorizationCodeHub.ResetForTests();
    }
}
