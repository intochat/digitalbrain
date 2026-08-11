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
    // PIN-DEFECT(P0-3): host composes exactly one IDigitalBrain; default owner constant is "dev"
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
    // PIN-DEFECT(P0-3): every Kernel HTTP surface path is unauthenticated (no auth middleware registered)
    public void KernelHttpSurfaceIsTheUnauthenticatedInventoryWithoutAuthMiddleware()
    {
        Assert.Equal("/owner/commands", HttpSurfacePaths.OwnerCommandsPath);
        Assert.Equal("/chats/{chatName}/events", HttpSurfacePaths.ChatEventsPath);
        Assert.Equal("/surfaces/{surfaceName}/events", HttpSurfacePaths.SurfaceEventsPath);
        Assert.Equal("/brain/topology", HttpSurfacePaths.BrainTopologyPath);
        Assert.Equal("/graph/events", HttpSurfacePaths.GraphEventsPath);
        Assert.Equal("/authorizations/events", HttpSurfacePaths.AuthorizationEventsPath);
        Assert.Equal("/oauth/callback", HttpSurfacePaths.McpOAuthCallbackPath);
        Assert.Equal(OAuthCallbackPaths.RelativePath, HttpSurfacePaths.McpOAuthCallbackPath);

        var program = KernelProgramSource();
        Assert.Contains("MapOwnerCommands()", program, StringComparison.Ordinal);
        Assert.Contains("MapChatStreams()", program, StringComparison.Ordinal);
        Assert.Contains("MapSurfaceStreams()", program, StringComparison.Ordinal);
        Assert.Contains("MapAuthorizationStreams()", program, StringComparison.Ordinal);
        Assert.Contains("MapBrainTopology()", program, StringComparison.Ordinal);
        Assert.Contains("MapGraphStreams()", program, StringComparison.Ordinal);
        Assert.Contains("MapOAuthCallback()", program, StringComparison.Ordinal);

        Assert.DoesNotContain("UseAuthentication", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AddAuthentication", program, StringComparison.Ordinal);
        Assert.DoesNotContain("UseAuthorization", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AddAuthorization", program, StringComparison.Ordinal);
        Assert.DoesNotContain("RequireAuthorization", program, StringComparison.Ordinal);
    }

    [Fact]
    // PIN-DEFECT(P0-4): owner-command body trusts client ChatName and carries no principal/actor
    public void OwnerCommandRequestAcceptsClientChatNameAndCarriesNoActor()
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
            Text: "hello from an unauthenticated client");
        Assert.Equal("main", request.ChatName);
        Assert.Equal(HttpSurfacePaths.KindChatSend, request.Kind);
    }

    [Fact]
    // Actor absence: durable chat command/fact shapes carry no actor principal today
    public void DurableChatCommandsAndJournalFactsCarryNoActorIdentity()
    {
        AssertNoCallerIdentityProperty(typeof(SendMessage));
        Assert.Equal(
            ["CommandId", "Text"],
            PropertyNames(typeof(SendMessage)));

        AssertNoCallerIdentityProperty(typeof(UserMessaged));
        Assert.Equal(
            ["CommandId", "Chat", "Text"],
            PropertyNames(typeof(UserMessaged)));

        AssertNoCallerIdentityProperty(typeof(ChatTurn));
        Assert.Equal(
            ["FromUser", "Text", "Buttons", "Charts", "Timers"],
            PropertyNames(typeof(ChatTurn)));

        AssertNoCallerIdentityProperty(typeof(Responded));
        Assert.Equal(
            ["CommandId", "Chat", "Text", "Buttons", "Charts", "Timers", "Author"],
            PropertyNames(typeof(Responded)));

        AssertNoCallerIdentityProperty(typeof(global::DigitalBrain.UI.Chat.OwnerCommand));
        Assert.Equal(
            ["CommandId", "Text"],
            PropertyNames(typeof(global::DigitalBrain.UI.Chat.OwnerCommand)));
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

    private static string KernelProgramSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Kernel", "DigitalBrain.Kernel", "Program.cs");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate src/Kernel/DigitalBrain.Kernel/Program.cs from the test base directory.");
    }
}

[Collection(BrainCollection.Name)]
public sealed class IdentityBoundaryChatProofs(BrainClusterFixture fixture)
{
    [Fact]
    // PIN-DEFECT(P0-3): session + chat grains resolve under the single composed owner
    public void SessionAndChatIdentitiesResolveUnderTheClientOwner()
    {
        var brain = fixture.BrainFor("dev");
        Assert.Equal(new OwnerId("dev"), brain.Owner);

        var session = ISessionNeuron.ForOwner(brain.Owner);
        Assert.Equal(new OwnerId("dev"), session.Owner);
        Assert.Equal(ISessionNeuron.InstanceName, session.Name);
        Assert.Equal(ISessionNeuron.GrainTypeName, session.Type);

        var chat = NeuronId.For<IChat>(brain.Owner, "main");
        Assert.Equal(new OwnerId("dev"), chat.Owner);
        Assert.Equal("main", chat.Name);
        Assert.Equal("chat", chat.Type);
        Assert.Equal("dev/main", chat.GrainKey);
    }

    [Fact]
    // PIN-DEFECT(P0-4): client-supplied chatName is the routing key — two clients share one transcript
    public async Task TwoClientsSendingToTheSameChatNameShareOneTranscript()
    {
        var owner = "p04-shared-chat";
        var clientA = fixture.BrainFor(owner);
        var clientB = fixture.BrainFor(owner);
        const string chatName = "main";
        var chat = NeuronId.For<IChat>(clientA.Owner, chatName);
        var responder = new NeuronId("scriptedagent", clientA.Owner, "shared");

        await clientA.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(ChatRoles.ResponderConnectionId(chat), chat, ChatRoles.Responder, responder),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForConnectionsAsync(clientA, chat, ChatRoles.Responder);

        await clientA.GetGrainProxy<IChat>(chatName)
            .Send(new SendMessage(CommandId.New(), "hello from client A"));
        await clientB.GetGrainProxy<IChat>(chatName)
            .Send(new SendMessage(CommandId.New(), "hello from client B"));

        await Journals.WaitForAsync(
            clientA, chat, JournalKind.Outgoing,
            delivery => delivery.Synapse is UserMessaged { Text: "hello from client A" });
        await Journals.WaitForAsync(
            clientB, chat, JournalKind.Outgoing,
            delivery => delivery.Synapse is UserMessaged { Text: "hello from client B" });

        var fromA = await clientA.GetGrainProxy<IChat>(chatName).Read();
        var fromB = await clientB.GetGrainProxy<IChat>(chatName).Read();

        Assert.Contains(fromA.Turns, turn => turn.FromUser && turn.Text == "hello from client A");
        Assert.Contains(fromA.Turns, turn => turn.FromUser && turn.Text == "hello from client B");
        Assert.Equal(fromA.Turns.Count, fromB.Turns.Count);
        Assert.Contains(fromB.Turns, turn => turn.FromUser && turn.Text == "hello from client A");
        Assert.Contains(fromB.Turns, turn => turn.FromUser && turn.Text == "hello from client B");
    }
}
