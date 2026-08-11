using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Aspire;
using DigitalBrain.Chat;
using DigitalBrain.Client;
using DigitalBrain.Kernel;
using DigitalBrain.Tests.Harness;
using DigitalBrain.UI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class IdentityBoundaryCompositionProofs
{
    [Fact]
    public void HostComposesExactlyOneDigitalBrainSingletonDefaultingToDev()
    {
        Assert.Equal("dev", DigitalBrainClientHostingExtensions.DefaultOwner);
        Assert.Equal("DigitalBrain:Owner", DigitalBrainClientHostingExtensions.OwnerConfigurationKey);

        var empty = new ConfigurationBuilder().Build();
        Assert.Equal("dev", DigitalBrainClientHostingExtensions.ResolveOwner(empty));

        var configured = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DigitalBrainResourceNames.OwnerConfigurationKey] = "other",
            })
            .Build();
        Assert.Equal("other", DigitalBrainClientHostingExtensions.ResolveOwner(configured));

        var services = new ServiceCollection();
        services.AddDigitalBrainOwner(empty, owner: null, activateOnStart: false);

        var brains = services.Where(descriptor => descriptor.ServiceType == typeof(IDigitalBrain)).ToList();
        Assert.Single(brains);
        Assert.Equal(ServiceLifetime.Singleton, brains[0].Lifetime);
    }

    [Fact]
    public void KernelHttpSurfaceRequiresAuthenticationExceptAuthOauthAndHealth()
    {
        Assert.Equal("/owner/commands", HttpSurfacePaths.OwnerCommandsPath);
        Assert.Equal("/chats/{chatName}/events", HttpSurfacePaths.ChatEventsPath);
        Assert.Equal("/surfaces/{surfaceName}/events", HttpSurfacePaths.SurfaceEventsPath);
        Assert.Equal("/brain/topology", HttpSurfacePaths.BrainTopologyPath);
        Assert.Equal("/graph/events", HttpSurfacePaths.GraphEventsPath);
        Assert.Equal("/authorizations/events", HttpSurfacePaths.AuthorizationEventsPath);
        Assert.Equal("/oauth/callback", HttpSurfacePaths.McpOAuthCallbackPath);
        Assert.Equal(OAuthCallbackPaths.RelativePath, HttpSurfacePaths.McpOAuthCallbackPath);
        Assert.Equal("/auth/bootstrap", HttpSurfacePaths.AuthBootstrapPath);
        Assert.Equal("/auth/login", HttpSurfacePaths.AuthLoginPath);
        Assert.Equal("/auth/logout", HttpSurfacePaths.AuthLogoutPath);
        Assert.Equal("/auth/me", HttpSurfacePaths.AuthMePath);
        Assert.Equal("/auth/users", HttpSurfacePaths.AuthUsersPath);

        var program = KernelProgramSource();
        Assert.Contains("MapOwnerCommands()", program, StringComparison.Ordinal);
        Assert.Contains("MapChatStreams()", program, StringComparison.Ordinal);
        Assert.Contains("MapSurfaceStreams()", program, StringComparison.Ordinal);
        Assert.Contains("MapAuthorizationStreams()", program, StringComparison.Ordinal);
        Assert.Contains("MapBrainTopology()", program, StringComparison.Ordinal);
        Assert.Contains("MapGraphStreams()", program, StringComparison.Ordinal);
        Assert.Contains("MapOAuthCallback()", program, StringComparison.Ordinal);
        Assert.Contains("MapAuth()", program, StringComparison.Ordinal);
        Assert.Contains("AddDigitalBrainAuth()", program, StringComparison.Ordinal);
        Assert.Contains("UseDigitalBrainAuth()", program, StringComparison.Ordinal);

        var authHosting = AuthHostingSource();
        Assert.Contains("UseAuthentication", authHosting, StringComparison.Ordinal);
        Assert.Contains("UseAuthorization", authHosting, StringComparison.Ordinal);
        Assert.Contains("AddAuthentication", authHosting, StringComparison.Ordinal);
        Assert.Contains("AddAuthorization", authHosting, StringComparison.Ordinal);
        Assert.Contains("RequireAuthenticatedUser", authHosting, StringComparison.Ordinal);
        Assert.Contains("HttpsStanceMiddleware", authHosting, StringComparison.Ordinal);
        Assert.Contains("AllowAnonymous", OAuthCallbackSource(), StringComparison.Ordinal);
        Assert.Contains("AllowAnonymous", AuthMapsSource(), StringComparison.Ordinal);

        var ownerCommands = ReadRepoFile("src", "Kernel", "DigitalBrain.Kernel", "MapOwnerCommands.cs");
        var surfaceStreams = ReadRepoFile("src", "Kernel", "DigitalBrain.Kernel", "MapShellStreams.cs");
        Assert.Contains("PrincipalSurface.InstanceName", ownerCommands, StringComparison.Ordinal);
        Assert.Contains("PrincipalSurface", surfaceStreams, StringComparison.Ordinal);
        Assert.Contains("HttpActor.TryGet", ownerCommands, StringComparison.Ordinal);
        Assert.Contains("HttpActor.TryGet", surfaceStreams, StringComparison.Ordinal);
    }

    [Fact]
    public void OwnerCommandRequestAcceptsClientChatNameButNeverClientActor()
    {
        var properties = typeof(OwnerCommandRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "Action",
                "ButtonId",
                "ChatName",
                "CommandId",
                "Kind",
                "OfferCommandId",
                "SurfaceKey",
                "SurfaceName",
                "Text",
                "Title",
            ],
            properties);

        AssertNoCallerIdentityProperty(typeof(OwnerCommandRequest));

        var request = new OwnerCommandRequest(
            Kind: HttpSurfacePaths.KindChatSend,
            ChatName: "main",
            Text: "hello from an authenticated client");
        Assert.Equal("main", request.ChatName);
        Assert.Equal(HttpSurfacePaths.KindChatSend, request.Kind);

        var principal = new PrincipalId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        Assert.Equal(
            "aaaaaaaabbbbccccddddeeeeeeeeeeee.main",
            PrincipalChat.InstanceName(principal, "main"));
        Assert.Equal(
            "aaaaaaaabbbbccccddddeeeeeeeeeeee.desk",
            PrincipalSurface.InstanceName(principal, "desk"));
        Assert.NotEqual(
            PrincipalSurface.InstanceName(principal, "desk"),
            PrincipalSurface.InstanceName(new PrincipalId(Guid.Parse("11111111-2222-3333-4444-555555555555")), "desk"));
    }

    [Fact]
    public void DurableChatCommandsAndJournalFactsCarryActorStampOnUserPath()
    {
        Assert.Contains("Actor", PropertyNames(typeof(SendMessage)));
        Assert.Contains("Actor", PropertyNames(typeof(UserMessaged)));
        Assert.Contains("Actor", PropertyNames(typeof(global::DigitalBrain.UI.Chat.OwnerCommand)));

        Assert.Equal(
            ["CommandId", "Text", "Actor"],
            PropertyNames(typeof(SendMessage)));
        Assert.Equal(
            ["CommandId", "Chat", "Text", "Actor"],
            PropertyNames(typeof(UserMessaged)));
        Assert.Equal(
            ["CommandId", "Text", "Actor"],
            PropertyNames(typeof(global::DigitalBrain.UI.Chat.OwnerCommand)));

        Assert.Contains("Author", PropertyNames(typeof(Responded)));
        Assert.Equal(
            ["FromUser", "Text", "Buttons", "Charts", "Timers"],
            PropertyNames(typeof(ChatTurn)));
    }

    private static string[] PropertyNames(Type type)
        => [.. type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)];

    private static void AssertNoCallerIdentityProperty(Type type)
    {
        foreach (var name in PropertyNames(type))
        {
            Assert.False(
                name.Contains("Actor", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Principal", StringComparison.OrdinalIgnoreCase)
                || name.Equals("UserId", StringComparison.OrdinalIgnoreCase)
                || name.Equals("User", StringComparison.OrdinalIgnoreCase)
                || name.Equals("CallerId", StringComparison.OrdinalIgnoreCase),
                $"{type.Name} unexpectedly carries caller-identity property '{name}'.");
        }
    }

    private static string KernelProgramSource() => ReadRepoFile("src", "Kernel", "DigitalBrain.Kernel", "Program.cs");

    private static string AuthHostingSource()
        => ReadRepoFile("src", "Kernel", "DigitalBrain.Kernel", "Auth", "AuthHostingExtensions.cs");

    private static string AuthMapsSource()
        => ReadRepoFile("src", "Kernel", "DigitalBrain.Kernel", "Auth", "MapAuth.cs");

    private static string OAuthCallbackSource()
        => ReadRepoFile("src", "Kernel", "DigitalBrain.Kernel", "MapOAuthCallback.cs");

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
public sealed class IdentityBoundaryChatProofs(BrainClusterFixture fixture)
{
    [Fact]
    public void SessionAndChatIdentitiesResolveUnderTheClientOwnerWithPrincipalScopedChatNames()
    {
        var brain = fixture.BrainFor("dev");
        Assert.Equal(new OwnerId("dev"), brain.Owner);

        var session = ISessionNeuron.ForOwner(brain.Owner);
        Assert.Equal(new OwnerId("dev"), session.Owner);
        Assert.Equal(ISessionNeuron.InstanceName, session.Name);
        Assert.Equal(ISessionNeuron.GrainTypeName, session.Type);

        var principal = new PrincipalId(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        var chatName = PrincipalChat.InstanceName(principal, "main");
        var chat = NeuronId.For<IChat>(brain.Owner, chatName);
        Assert.Equal(new OwnerId("dev"), chat.Owner);
        Assert.Equal(chatName, chat.Name);
        Assert.Equal("chat", chat.Type);
        Assert.Equal($"dev/{chatName}", chat.GrainKey);
    }

    [Fact]
    public async Task TwoPrincipalsSendingToTheSameConversationNameGetIsolatedTranscripts()
    {
        var owner = "p04-isolated-chat";
        var brain = fixture.BrainFor(owner);
        var principalA = new PrincipalId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var principalB = new PrincipalId(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var actorA = new ActorContext(principalA, "alice");
        var actorB = new ActorContext(principalB, "bob");
        var chatA = PrincipalChat.InstanceName(principalA, "main");
        var chatB = PrincipalChat.InstanceName(principalB, "main");
        var neuronA = NeuronId.For<IChat>(brain.Owner, chatA);
        var neuronB = NeuronId.For<IChat>(brain.Owner, chatB);
        var responderA = new NeuronId("scriptedagent", brain.Owner, "a");
        var responderB = new NeuronId("scriptedagent", brain.Owner, "b");

        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(ChatRoles.ResponderConnectionId(neuronA), neuronA, ChatRoles.Responder, responderA),
            TestContext.Current.CancellationToken);
        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(ChatRoles.ResponderConnectionId(neuronB), neuronB, ChatRoles.Responder, responderB),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForConnectionsAsync(brain, neuronA, ChatRoles.Responder);
        await Graphs.WaitForConnectionsAsync(brain, neuronB, ChatRoles.Responder);

        await brain.GetGrainProxy<IChat>(chatA)
            .Send(new SendMessage(CommandId.New(), "hello from alice", actorA));
        await brain.GetGrainProxy<IChat>(chatB)
            .Send(new SendMessage(CommandId.New(), "hello from bob", actorB));

        await Journals.WaitForAsync(
            brain, neuronA, JournalKind.Outgoing,
            delivery => delivery.Synapse is UserMessaged { Text: "hello from alice", Actor: not null } messaged
                && messaged.Actor!.PrincipalId == principalA);
        await Journals.WaitForAsync(
            brain, neuronB, JournalKind.Outgoing,
            delivery => delivery.Synapse is UserMessaged { Text: "hello from bob", Actor: not null } messaged
                && messaged.Actor!.PrincipalId == principalB);

        var fromA = await brain.GetGrainProxy<IChat>(chatA).Read();
        var fromB = await brain.GetGrainProxy<IChat>(chatB).Read();

        Assert.Contains(fromA.Turns, turn => turn.FromUser && turn.Text == "hello from alice");
        Assert.DoesNotContain(fromA.Turns, turn => turn.Text == "hello from bob");
        Assert.Contains(fromB.Turns, turn => turn.FromUser && turn.Text == "hello from bob");
        Assert.DoesNotContain(fromB.Turns, turn => turn.Text == "hello from alice");
    }
}
