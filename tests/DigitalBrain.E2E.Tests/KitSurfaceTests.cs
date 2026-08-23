using System.Net;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Kernel;
using DigitalBrain.Testing.E2E;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.E2E.Tests;

// Kit entities (chart, image) are created by the AI tools under the caller's principal
// partition (KitToolSource, Task 7) and read back over HTTP by MapKitEntities (Task 8).
// Seeding here goes straight through IDigitalBrain.GetEntity under HttpActor's fixed
// principal -- the same resolution the kernel endpoint performs -- rather than driving a
// real chat turn, so this proves the entity-key round trip without depending on the AI
// pipeline actually calling a tool.
[Collection(E2ECollection.Name)]
public sealed class KitSurfaceTests(AppHostFixture fixture)
{
    [Fact]
    public async Task ChartStateIsReadableOverHttp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var brain = fixture.BrainFor(DigitalBrainNames.DefaultOwner);
        var instance = PrincipalScoped.InstanceName(HttpActor.Current.PrincipalId, "chart-e2e");
        var points = new[] { new ChartPoint("Q1", 10), new ChartPoint("Q2", 20) };
        await brain.GetEntity<IChart>(instance).Render(new ChartState("Sales", "bar", points));

        using var http = fixture.CreateHttpClient("kernel");
        var response = await http.GetAsync("/kit/charts/chart-e2e", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("\"title\":\"Sales\"", body, StringComparison.Ordinal);
        Assert.Contains("\"label\":\"Q1\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownChartNameReturnsNotFound()
    {
        using var http = fixture.CreateHttpClient("kernel");
        var response = await http.GetAsync(
            "/kit/charts/no-such-chart", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ImageStateIsReadableOverHttpAndOmitsTheBlobName()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var brain = fixture.BrainFor(DigitalBrainNames.DefaultOwner);
        var instance = PrincipalScoped.InstanceName(HttpActor.Current.PrincipalId, "image-e2e");
        await brain.GetEntity<IImage>(instance)
            .Describe(new ImageState("a red fox", "gpt-image-1", "image/png", "image-e2e-blob.png"));

        using var http = fixture.CreateHttpClient("kernel");
        var response = await http.GetAsync("/kit/images/image-e2e", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("\"prompt\":\"a red fox\"", body, StringComparison.Ordinal);
        Assert.Contains("\"mediaType\":\"image/png\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("blobName", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownImageNameReturnsNotFound()
    {
        using var http = fixture.CreateHttpClient("kernel");
        var response = await http.GetAsync(
            "/kit/images/no-such-image", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ImageContentForAnUnknownNameReturnsNotFound()
    {
        using var http = fixture.CreateHttpClient("kernel");
        var response = await http.GetAsync(
            "/kit/images/no-such-image/content", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // PrincipalPartition.InstanceName rejects whitespace in the local name (IdentityPart's
    // rule, not just the caller's own trimming), so a whitespace-only route segment is the
    // cheapest way to exercise the 400 branch of TryPrincipalResource over real HTTP.
    [Fact]
    public async Task WhitespaceChartNameReturnsBadRequest()
    {
        using var http = fixture.CreateHttpClient("kernel");
        var response = await http.GetAsync(
            "/kit/charts/%20%20", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // UC1 end to end at the API level: TestChatClient (testing mode) scripts a render_chart
    // tool call for any "chart" message, KitToolSource runs it for real against the running
    // silo, and the resulting card rides the SAME chat-turn SSE stream the Flutter shell reads
    // (sse_chat_frames.dart). Proves the tool-calling round trip, not just the entity read side
    // ChartStateIsReadableOverHttp above already covers.
    [Fact]
    public async Task AskingForAChartProducesACardOnTheTurnStream()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var http = fixture.CreateHttpClient("kernel");
        const string ChatName = "chart-turn-e2e";

        var send = await http.PostAsJsonAsync(
            HttpSurfacePaths.OwnerCommandsPath,
            new { kind = "chat.send", chatName = ChatName, text = "show me a chart" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);

        using var turnStreamBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        turnStreamBudget.CancelAfter(TimeSpan.FromSeconds(30));
        var cardName = await ReadCardNameFromTurnStreamAsync(
            http, ChatName, KitCardKinds.Chart, turnStreamBudget.Token);

        var chart = await http.GetAsync($"/kit/charts/{cardName}", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, chart.StatusCode);
        var body = await chart.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("\"title\":\"Test chart\"", body, StringComparison.Ordinal);
    }

    // UC2 end to end, closing the image-content 200 coverage gap deferred from Task 8: the
    // scripted generate_image tool call runs for real (TestImageGeneration + MemoryKitImageStore
    // in testing mode), the resulting card's name comes off the same turn stream as the chart
    // case above, and both kit image endpoints are proven live -- state (JSON) and content
    // (the actual PNG bytes), not just one or the other.
    [Fact]
    public async Task AskingForAnImageProducesACardWithReadableContent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var http = fixture.CreateHttpClient("kernel");
        const string ChatName = "image-turn-e2e";

        var send = await http.PostAsJsonAsync(
            HttpSurfacePaths.OwnerCommandsPath,
            new { kind = "chat.send", chatName = ChatName, text = "generate an image" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);

        using var turnStreamBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        turnStreamBudget.CancelAfter(TimeSpan.FromSeconds(30));
        var cardName = await ReadCardNameFromTurnStreamAsync(
            http, ChatName, KitCardKinds.Image, turnStreamBudget.Token);

        var image = await http.GetAsync($"/kit/images/{cardName}", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        var body = await image.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("\"mediaType\":\"image/png\"", body, StringComparison.Ordinal);

        var content = await http.GetAsync($"/kit/images/{cardName}/content", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal("image/png", content.Content.Headers.ContentType?.MediaType);
        var bytes = await content.Content.ReadAsByteArrayAsync(cancellationToken);
        Assert.NotEmpty(bytes);
    }

    // Reads /chats/{chatName}/events (MapChatStreams) exactly like the Flutter shell's
    // sse_chat_frames.dart: every item on that stream is a "chat-turn" event carrying a
    // ChatTurnEvent JSON payload; watch until one turn's "cards" carries the requested kind,
    // then return that card's name. Bounded entirely by the caller's cancellationToken --
    // this stream never closes on its own (MapChatStreams is a live tail).
    private static async Task<string> ReadCardNameFromTurnStreamAsync(
        HttpClient http, string chatName, string expectedKind, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(
            $"/chats/{chatName}/events", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using (stream.ConfigureAwait(false))
        {
            try
            {
                await foreach (var sseEvent in SseParser.Create(stream).EnumerateAsync(cancellationToken)
                    .ConfigureAwait(false))
                {
                    if (!string.Equals(sseEvent.EventType, "chat-turn", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var turn = JsonSerializer.Deserialize<TurnPayload>(sseEvent.Data, EventJson);
                    var card = turn?.Cards?.FirstOrDefault(
                        c => string.Equals(c.Kind, expectedKind, StringComparison.Ordinal));
                    if (card is not null)
                    {
                        return card.Name;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Assert.Fail($"No '{expectedKind}' card observed on the '{chatName}' chat turn stream in time.");
            }
        }

        Assert.Fail($"The chat turn stream for '{chatName}' ended before a '{expectedKind}' card appeared.");
        return null!; // Unreachable: Assert.Fail throws.
    }

    private static readonly JsonSerializerOptions EventJson = new(JsonSerializerDefaults.Web);

    private sealed record TurnPayload(TurnCard[]? Cards);

    private sealed record TurnCard(string Kind, string Name, string Caption);
}
