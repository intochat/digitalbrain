using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Core.Ui;
using DigitalBrain.Core.UiKit;
using DigitalBrain.Google;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Config;
using DigitalBrain.TestKit;
using DigitalBrain.UiKit;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.Ino;

public class InoNeuronChatSurfaceTests : NeuronTestBase
{
    [Fact]
    public async Task InoRequest_Emits_Assistant_Reply_Surface_To_FlutterUi()
    {
        var ino = Grain<IInoNeuron>("ino-main");
        await ino.FireAsync(new InoRequest("hello, what can you do?", "session-1"));

        var flutter = Grain<IFlutterUiNeuron>("flutter-ui");
        var timeline = await flutter.GetIncomingTimelineAsync();

        var surface = Assert.Single(timeline.OfType<UiSurface>());
        Assert.Equal(UiSurface.WidgetTreeKind, surface.Kind);
        Assert.Equal("session-1", surface.Props["sessionId"]);
        Assert.Equal("assistant", surface.Props["role"]);

        var tree = Assert.IsType<UiWidgetTree>(surface.Props["tree"]);
        Assert.Equal(UiKitVocabulary.Text, tree.Type);
        Assert.Contains("no-llm", tree.Props["text"]!.ToString());
    }

    [Fact]
    public async Task GmailIntent_WithoutGoogleCredential_Emits_Auth_Button_Surface()
    {
        var ino = Grain<IInoNeuron>("ino-main");
        await ino.FireAsync(new InoRequest("Get my last gmail", "session-gmail-auth"));

        var response = Assert.Single((await ino.GetOutgoingTimelineAsync()).OfType<InoResponse>());
        Assert.Equal("Get my last gmail", response.Prompt);
        Assert.Contains("Google authentication", response.Response);
        Assert.DoesNotContain("manual", response.Response, StringComparison.OrdinalIgnoreCase);

        var flutter = Grain<IFlutterUiNeuron>("flutter-ui");
        var surface = Assert.Single((await flutter.GetIncomingTimelineAsync()).OfType<UiSurface>());
        Assert.Equal(UiSurface.WidgetTreeKind, surface.Kind);
        Assert.Equal("session-gmail-auth", surface.Props["sessionId"]);
        Assert.Equal("assistant", surface.Props["role"]);
        Assert.Equal(UiSurfaceKinds.AuthButton, surface.Props["surfaceKind"]);

        var tree = Assert.IsType<UiWidgetTree>(surface.Props["tree"]);
        var button = FindNode(tree, UiKitVocabulary.Button);
        Assert.NotNull(button);
        Assert.Equal("Authenticate Google", button!.Props["label"]);
        Assert.Equal("gmail", button.Props["icon"]);
        Assert.Equal(GoogleSignals.AuthRequested, button.Props["synapseType"]);
    }

    private static UiWidgetTree? FindNode(UiWidgetTree tree, string type)
    {
        if (tree.Type == type)
            return tree;

        foreach (var child in tree.Children ?? [])
        {
            var found = FindNode(child, type);
            if (found is not null)
                return found;
        }

        return null;
    }
}

internal sealed class QueuedInoChatClient : IChatClient
{
    public static readonly Queue<string> Replies = new();

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var reply = Replies.Count > 0 ? Replies.Dequeue() : "direct fallback";
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Streaming not used.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

public sealed class InoNeuronActionDirectiveTests : NeuronTestBase
{
    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services => services.AddSingleton<IChatClient, QueuedInoChatClient>());

    [Fact]
    public async Task Plain_chat_does_not_surface_branch_directive_as_the_reply()
    {
        QueuedInoChatClient.Replies.Clear();
        QueuedInoChatClient.Replies.Enqueue("BRANCH: CreateJokeBranch");
        QueuedInoChatClient.Replies.Enqueue("Here is a small joke.");

        var ino = Grain<IInoNeuron>("ino-main");
        await ino.FireAsync(new InoRequest("tell me a joke", "session-1"));

        var response = (await ino.GetOutgoingTimelineAsync()).OfType<InoResponse>().Last();
        Assert.Equal("Here is a small joke.", response.Response);
        Assert.Empty(response.UsedTaskIds);

        var flutter = Grain<IFlutterUiNeuron>("flutter-ui");
        var surface = (await flutter.GetIncomingTimelineAsync()).OfType<UiSurface>().Single();
        var tree = Assert.IsType<UiWidgetTree>(surface.Props["tree"]);
        Assert.Equal("Here is a small joke.", tree.Props["text"]);
    }
}

public sealed class InoNeuronAuthenticatedGmailTests : NeuronTestBase
{
    private readonly RecordingGmailApiClient _gmail = new();

    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.AddPackConfigStore(blobsForKeyRing: null);
            services.AddSingleton<IGmailApiClient>(_gmail);
        });

    [Fact]
    public async Task GmailIntent_WithGoogleCredential_Calls_GmailNeuron_And_Renders_Messages()
    {
        var config = Grain<IGoogleConfigWriter>("google-config-writer");
        await config.StoreGoogleCredentialAsync();

        var ino = Grain<IInoNeuron>("ino-main");
        await ino.FireAsync(new InoRequest("Get my last gmail", "session-gmail-ready"));

        Assert.Single(_gmail.ListCalls);
        Assert.Equal("", _gmail.ListCalls[0].Query);
        Assert.Equal(1, _gmail.ListCalls[0].MaxResults);
        Assert.Equal(["fake-message-1"], _gmail.ReadMessageIds);

        var response = Assert.Single((await ino.GetOutgoingTimelineAsync()).OfType<InoResponse>());
        Assert.Contains("Latest Gmail message", response.Response);
        Assert.Contains("Quarterly planning", response.Response);

        var signals = (await ino.GetOutgoingTimelineAsync()).OfType<Signal>().ToList();
        Assert.Contains(signals, signal => signal.Name == GoogleSignals.GmailFetchRequested);
        Assert.Contains(signals, signal => signal.Name == GoogleSignals.GmailMessagesReady);

        var flutter = Grain<IFlutterUiNeuron>("flutter-ui");
        var surface = Assert.Single((await flutter.GetIncomingTimelineAsync()).OfType<UiSurface>());
        Assert.Equal("session-gmail-ready", surface.Props["sessionId"]);
        Assert.Equal("Gmail", surface.Props[UiSurfaceKeys.Title]);

        var tree = Assert.IsType<UiWidgetTree>(surface.Props["tree"]);
        Assert.Contains("Quarterly planning", FlattenText(tree));
    }

    private static string FlattenText(UiWidgetTree tree)
    {
        var values = new List<string>();
        Collect(tree);
        return string.Join("\n", values);

        void Collect(UiWidgetTree node)
        {
            if (node.Props.TryGetValue("text", out var text) && text is not null)
                values.Add(text.ToString()!);

            foreach (var child in node.Children ?? [])
                Collect(child);
        }
    }
}

public interface IGoogleConfigWriter : INeuron
{
    Task StoreGoogleCredentialAsync();
}

[GrainType("digitalbrain.test.google-config-writer")]
public sealed class GoogleConfigWriter(Microsoft.Extensions.Logging.ILogger<GoogleConfigWriter> logger, NeuronJournals journals)
    : Neuron(logger, journals), IGoogleConfigWriter
{
    public Task StoreGoogleCredentialAsync() =>
        ServiceProvider.GetRequiredService<IPackConfigStore>().SetAsync("default", "google", new Dictionary<string, string>
        {
            ["client_id"] = "client-id",
            ["client_secret"] = "client-secret",
            ["refresh_token"] = "refresh-token",
        });
}

internal sealed class RecordingGmailApiClient : IGmailApiClient
{
    public List<(string Query, int MaxResults)> ListCalls { get; } = [];
    public List<string> ReadMessageIds { get; } = [];

    public Task<string[]> ListMessagesAsync(string query, int maxResults, CancellationToken ct)
    {
        ListCalls.Add((query, maxResults));
        return Task.FromResult(new[] { "fake-message-1", "fake-message-2" });
    }

    public Task<string> ReadMessageAsync(string messageId, CancellationToken ct)
    {
        ReadMessageIds.Add(messageId);
        return Task.FromResult("Quarterly planning moved to 3 PM. Bring the launch notes.");
    }

    public Task SendMessageAsync(string to, string subject, string body, CancellationToken ct) =>
        throw new NotSupportedException("Send is not used by INO Gmail read tests.");
}
