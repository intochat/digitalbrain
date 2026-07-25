using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace DigitalBrain.Ui.Tests;

public sealed class LiveProductUiNorthbound
{
    [Fact(
        Explicit = true,
        DisplayName =
            "LIVE product Ui: POST open-scene Accepted and SSE projects scene-opened (requires aspire start product AppHost)")]
    public async Task PostOpenSceneAndSseProjectsSceneOpened()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var baseAddress = ResolveUiBaseAddress();
        var shellName = $"live-{Guid.NewGuid():N}"[..16];
        var sceneKey = "live-home";
        var title = "Live Home";

        using var http = new HttpClient
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(30),
        };

        using (var health = await http.GetAsync(new Uri("/health", UriKind.Relative), cancellationToken))
        {
            Assert.True(
                health.IsSuccessStatusCode,
                $"Product digitalbrain-ui /health not OK at {baseAddress}. Start: aspire start --project hosts/DigitalBrain.AppHost. Status={(int)health.StatusCode}.");
        }

        using var streamRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/shells/{shellName}/events?afterSequence=0");
        using var streamResponse = await http.SendAsync(
            streamRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, streamResponse.StatusCode);
        Assert.Equal("text/event-stream", streamResponse.Content.Headers.ContentType?.MediaType);

        await using var body = await streamResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(body);

        using var openResponse = await http.PostAsJsonAsync(
            $"/shells/{shellName}/scenes",
            new { sceneKey, title },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, openResponse.StatusCode);

        var payload = await ReadNextSceneOpenedPayloadAsync(reader, cancellationToken);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        Assert.Equal(sceneKey, root.GetProperty("sceneKey").GetString());
        Assert.Equal(title, root.GetProperty("title").GetString());
        Assert.True(root.GetProperty("sequence").GetInt64() > 0);
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("commandId").GetString()));
        Assert.Contains(shellName, root.GetProperty("shell").GetString(), StringComparison.Ordinal);
    }

    private static Uri ResolveUiBaseAddress()
    {
        var configured = Environment.GetEnvironmentVariable("DIGITALBRAIN_UI_BASE");
        if (string.IsNullOrWhiteSpace(configured))
        {
            return new Uri("http://localhost:5080");
        }

        return new Uri(configured.TrimEnd('/') + "/");
    }

    private static async Task<string> ReadNextSceneOpenedPayloadAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        string? dataLine = null;
        string? eventName = null;
        while (!timeout.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(timeout.Token);
            if (line is null)
            {
                break;
            }

            if (line.StartsWith(':'))
            {
                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventName = line["event:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                dataLine = line["data:".Length..].Trim();
                continue;
            }

            if (line.Length == 0 && dataLine is not null)
            {
                var name = eventName;
                var payload = dataLine;
                eventName = null;
                dataLine = null;

                if (name is not null
                    && !string.Equals(name, "scene-opened", StringComparison.Ordinal))
                {
                    continue;
                }

                if (payload.Contains("\"sceneKey\"", StringComparison.Ordinal)
                    && payload.Contains("\"sequence\"", StringComparison.Ordinal))
                {
                    return payload;
                }
            }
        }

        throw new TimeoutException(
            "Product SSE ended before scene-opened — product AppHost/ui/silo may be down or mid-restart.");
    }
}
