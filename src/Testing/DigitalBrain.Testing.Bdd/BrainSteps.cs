using System.Net;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Brain;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Chat;
using DigitalBrain.Client;
using DigitalBrain.Memory;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Microsoft.Extensions.AI;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Testing.Bdd;

// The section-9 vocabulary at Tier 3: every step crosses the kernel's real HTTP edge or the
// cross-process facade — no in-memory shortcuts. Route, payload, and SSE event literals
// mirror the kernel's frozen shell wire (HttpSurfacePaths.cs / MapOwnerCommands.cs; the
// constants there are internal, so the literals are duplicated rather than linked).
[Binding]
public sealed class BrainSteps : IDisposable
{
    private static readonly TimeSpan TurnTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private HttpClient? _kernel;
    private PrincipalId _principal;
    private string _chatName = null!;
    private string _replyText = null!;
    private IDigitalBrain _brain = null!;

    [Given("an activated owner session")]
    public async Task GivenAnActivatedOwnerSession()
    {
        _kernel = BddBrainHost.Fixture.CreateHttpClient("kernel");
        using var login = await _kernel
            .PostAsJsonAsync("/auth/login", new { username = "owner", password = "ownerowner" })
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var me = await login.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        _principal = new PrincipalId(Guid.Parse(me.GetProperty("principalId").GetString()!));

        // The kernel serves exactly one owner: AppHost.cs stamps DigitalBrain__Owner with
        // ShellHostingExtensions.DefaultOwner ("dev" — the same value as
        // DigitalBrainNames.DefaultOwner), and the kernel's request-scoped IDigitalBrain binds
        // that owner via DigitalBrainClientHostingExtensions.ResolveOwner. The facade must
        // watch the same owner's grains as the HTTP principal's chat writes to.
        _brain = BddBrainHost.Fixture.BrainFor(DigitalBrainNames.DefaultOwner);
        await _brain.ActivateAsync().ConfigureAwait(false);
        _chatName = $"bdd-{Guid.NewGuid():N}"[..12];
    }

    [When("the user chats {string}")]
    public async Task WhenTheUserChats(string text)
    {
        using var turnBudget = new CancellationTokenSource(TurnTimeout);

        // The frozen shell wire: POST /owner/commands with the kind-discriminated payload;
        // chat.send answers with an SSE stream that carries one chat-delta per assistant
        // update (ChatResponseUpdate serialized with AIJsonUtilities' options) and closes
        // when the turn's Responded lands.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/owner/commands")
        {
            Content = JsonContent.Create(new { kind = "chat.send", chatName = _chatName, text }),
        };
        using var response = await _kernel!
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, turnBudget.Token)
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var reply = new StringBuilder();
        var stream = await response.Content.ReadAsStreamAsync(turnBudget.Token).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            await foreach (var sseEvent in SseParser.Create(stream).EnumerateAsync(turnBudget.Token).ConfigureAwait(false))
            {
                if (string.Equals(sseEvent.EventType, "chat-delta", StringComparison.Ordinal))
                {
                    var update = JsonSerializer.Deserialize<ChatResponseUpdate>(
                        sseEvent.Data, AIJsonUtilities.DefaultOptions);
                    reply.Append(update?.Text);
                }
            }
        }

        _replyText = reply.ToString();

        // The reply rode Responded; the turn's memory fact settles before
        // TurnLifecycle(Completed) (the ordering ChatTurnTests pins), so anchoring "the flow
        // completed" here lets every Then step read settled state. The wait matches any
        // settling status so a Failed/Cancelled turn (whose SSE stream closes with no delta)
        // fails fast with its detail instead of burning the full timeout. The chat instance
        // is the principal-partitioned name MapOwnerCommands.TryPrincipalResource derives.
        var chatInstance = PrincipalPartition.InstanceName(_principal, _chatName);
        var chatId = NeuronId.For<IChat>(_brain.Owner, chatInstance);
        var settled = await JournalWait.ForAsync(
            _brain,
            chatId,
            JournalKind.Outgoing,
            static delivery => delivery.Synapse is TurnLifecycle
            {
                Status: ChatTurnStatus.Completed or ChatTurnStatus.Failed or ChatTurnStatus.Cancelled,
            },
            TurnTimeout).ConfigureAwait(false);

        var lifecycle = (TurnLifecycle)settled.Synapse;
        if (lifecycle.Status != ChatTurnStatus.Completed)
        {
            Assert.Fail(
                $"The chat turn settled as {lifecycle.Status} instead of Completed."
                + $" Detail: {lifecycle.Detail ?? "(none)"}");
        }
    }

    [Then("the assistant replies {string}")]
    public void ThenTheAssistantReplies(string expected) => Assert.Equal(expected, _replyText);

    [Then("the chart {string} holds {int} points")]
    public async Task ThenTheChartHoldsPoints(string chart, int count)
    {
        // The entity write drains asynchronously behind the reply (ChatTurnTests polls the
        // same state), so poll for the exact count and then assert it exactly.
        var deadline = DateTimeOffset.UtcNow + PollTimeout;
        ChartState? state;
        while (true)
        {
            state = await _brain.GetEntity<IChart>(chart).Read().ConfigureAwait(false);
            if (state?.Points.Count == count || DateTimeOffset.UtcNow >= deadline)
            {
                break;
            }

            await Task.Delay(PollInterval).ConfigureAwait(false);
        }

        Assert.NotNull(state);
        Assert.Equal(count, state!.Points.Count);
    }

    [Then("the memory holds the story fact {string}")]
    public async Task ThenTheMemoryHoldsTheFact(string text)
    {
        // Mirrors ChatTurnTests' fact assertion, minus the Correlation filter: the completing
        // turn's CommandId was minted inside the kernel's SSE handler and never crosses the
        // wire, so the read filters by kind alone — this boot's single turn stored exactly
        // one chat.responded fact. The wait above already observed TurnLifecycle(Completed),
        // which the turn only emits after its StoreFact settled, so one bounded read suffices.
        var facts = await _brain.Get<IFactMemory>(IFactMemory.InstanceName)
            .FireAsync<FactsRead>(
                new ReadFacts(CommandId.New(), Kind: "chat.responded", Correlation: null, Limit: 10))
            .WaitAsync(PollTimeout).ConfigureAwait(false);

        var fact = Assert.Single(facts.Facts);
        Assert.Equal("chat.responded", fact.Kind);
        Assert.Equal(text, fact.Text);
    }

    [Then("resolving {string} finds {string}")]
    public async Task ThenResolvingFinds(string hint, string expectedName)
    {
        // Registration of the renderer-written chart reaches the brain graph asynchronously
        // (UIRendererTests polls the same edge), so poll until something resolves, then
        // assert the exact name.
        var deadline = DateTimeOffset.UtcNow + PollTimeout;
        BrainReference? resolved;
        while (true)
        {
            resolved = await _brain.ResolveAsync(hint).ConfigureAwait(false);
            if (resolved is not null || DateTimeOffset.UtcNow >= deadline)
            {
                break;
            }

            await Task.Delay(PollInterval).ConfigureAwait(false);
        }

        Assert.NotNull(resolved);
        Assert.Equal(expectedName, resolved!.Name);
    }

    public void Dispose()
    {
        _kernel?.Dispose();
        _kernel = null;
    }
}
