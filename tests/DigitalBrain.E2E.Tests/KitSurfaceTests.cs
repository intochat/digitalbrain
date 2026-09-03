using System.Net;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Excel;
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
        var payload = await response.Content.ReadFromJsonAsync<ChartState>(WireJson, cancellationToken);
        Assert.NotNull(payload);
        Assert.Equal("Sales", payload!.Title);
        Assert.Equal("bar", payload.ChartKind);
        Assert.Contains(payload.Points, point => point.Label == "Q1");
    }

    [Fact]
    public async Task SpreadsheetStateIsReadableOverHttp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var brain = fixture.BrainFor(DigitalBrainNames.DefaultOwner);
        var instance = PrincipalScoped.InstanceName(HttpActor.Current.PrincipalId, "sheet-e2e");
        await brain.GetEntity<IExcel>(instance).Load(new ExcelState(
            "Yesterday",
            "Sheet1",
            ["Item", "Qty"],
            [new ExcelRow(["Shoes", "2"])]));

        using var http = fixture.CreateHttpClient("kernel");
        var response = await http.GetAsync("/kit/spreadsheets/sheet-e2e", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ExcelState>(WireJson, cancellationToken);
        Assert.NotNull(payload);
        Assert.Equal("Yesterday", payload!.Title);
        Assert.Equal("Shoes", payload.Rows[0].Cells[0]);
    }

    [Theory]
    [InlineData("/kit/charts/no-such-chart")]
    [InlineData("/kit/images/no-such-image")]
    [InlineData("/kit/images/no-such-image/content")]
    [InlineData("/kit/spreadsheets/no-such-sheet")]
    public async Task UnknownKitEntityNameReturnsNotFound(string route)
    {
        using var http = fixture.CreateHttpClient("kernel");
        var response = await http.GetAsync(route, TestContext.Current.CancellationToken);

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
        // The one deliberate wire-level negative: blobName is storage-internal and must never
        // appear in the serialized image JSON under any name or casing.
        Assert.DoesNotContain("blobName", body, StringComparison.OrdinalIgnoreCase);
        var payload = JsonSerializer.Deserialize<KitImageStateResponse>(body, WireJson);
        Assert.NotNull(payload);
        Assert.Equal("a red fox", payload!.Prompt);
        Assert.Equal("image/png", payload.MediaType);
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
        var payload = await chart.Content.ReadFromJsonAsync<ChartState>(WireJson, cancellationToken);
        Assert.Equal("Test chart", payload?.Title);
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
        var payload = await image.Content.ReadFromJsonAsync<KitImageStateResponse>(WireJson, cancellationToken);
        Assert.Equal("image/png", payload?.MediaType);

        var content = await http.GetAsync($"/kit/images/{cardName}/content", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal("image/png", content.Content.Headers.ContentType?.MediaType);
        var bytes = await content.Content.ReadAsByteArrayAsync(cancellationToken);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public async Task AskingForASpreadsheetProducesACardOnTheTurnStream()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var http = fixture.CreateHttpClient("kernel");
        const string ChatName = "sheet-turn-e2e";

        var send = await http.PostAsJsonAsync(
            HttpSurfacePaths.OwnerCommandsPath,
            new { kind = "chat.send", chatName = ChatName, text = "show me yesterday's excel file" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);

        using var turnStreamBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        turnStreamBudget.CancelAfter(TimeSpan.FromSeconds(30));
        var cardName = await ReadCardNameFromTurnStreamAsync(
            http, ChatName, KitCardKinds.Spreadsheet, turnStreamBudget.Token);

        var sheet = await http.GetAsync($"/kit/spreadsheets/{cardName}", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, sheet.StatusCode);
        var payload = await sheet.Content.ReadFromJsonAsync<ExcelState>(WireJson, cancellationToken);
        Assert.Equal("Yesterday", payload?.Title);
        Assert.Equal("Shoes", payload?.Rows[0].Cells[0]);
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

                    var turn = JsonSerializer.Deserialize<TurnPayload>(sseEvent.Data, WireJson);
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

    // Deliberately case-SENSITIVE camelCase (not JsonSerializerDefaults.Web, whose
    // case-insensitive matching would let a wire-casing change slip through): the Flutter
    // shell parses these exact keys, so the tests must pin them.
    private static readonly JsonSerializerOptions WireJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed record TurnPayload(TurnCard[]? Cards);

    private sealed record TurnCard(string Kind, string Name, string Caption);
}
