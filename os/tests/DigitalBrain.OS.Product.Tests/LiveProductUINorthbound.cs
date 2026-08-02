using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter.Aspire.Hosting;
using DigitalBrain.Flutter.Http;
using Xunit;

namespace DigitalBrain.ProductTests;

[Collection("live product")]
public sealed class LiveProductUINorthbound
{
    private static readonly Uri DefaultUIBaseAddress = new("http://localhost:5080/");

    private static readonly JsonSerializerOptions SceneOpenedJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact(
        Explicit = true,
        DisplayName =
            "LIVE product UI: POST open-scene Accepted and SSE projects scene-opened (requires aspire start product AppHost)")]
    public async Task PostOpenSceneAndSseProjectsSceneOpened()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var baseAddress = ResolveProductUIBaseAddress();
        var shellName = $"live-{Guid.NewGuid():N}"[..16];
        var sceneKey = "live-home";
        var title = "Live Home";

        using var http = new HttpClient
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(30),
        };

        using (var health = await http.GetAsync(new Uri(FlutterHttpContract.HealthPath, UriKind.Relative), cancellationToken))
        {
            Assert.True(
                health.IsSuccessStatusCode,
                $"Product {FlutterHostingExtensions.DefaultUIResourceName} {FlutterHttpContract.HealthPath} not OK at {baseAddress}. Start: aspire start --project os/DigitalBrain.OS.AppHost. Status={(int)health.StatusCode}. Override with {FlutterHostingExtensions.UIBaseEnvironmentVariable}.");
        }

        var shellEventsPath = FlutterHttpContract.ShellEventsPath.Replace("{shellName}", shellName, StringComparison.Ordinal)
            + $"?{FlutterHttpContract.AfterSequenceQuery}=0";
        using var streamRequest = new HttpRequestMessage(HttpMethod.Get, shellEventsPath);
        using var streamResponse = await http.SendAsync(streamRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, streamResponse.StatusCode);
        Assert.Equal(FlutterHttpContract.EventStreamContentType, streamResponse.Content.Headers.ContentType?.MediaType);

        await using var body = await streamResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(body);

        var openScenePath = FlutterHttpContract.OpenScenePath.Replace("{shellName}", shellName, StringComparison.Ordinal);
        using var openResponse = await http.PostAsJsonAsync(openScenePath, new { sceneKey, title }, cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, openResponse.StatusCode);

        var projected = await ReadNextSceneOpenedAsync(reader, timeout: TimeSpan.FromSeconds(20), cancellationToken);

        Assert.Equal(sceneKey, projected.SceneKey);
        Assert.Equal(title, projected.Title);
        Assert.True(projected.Sequence > 0);
        Assert.False(string.IsNullOrWhiteSpace(projected.CommandId));
        Assert.Contains(shellName, projected.Shell, StringComparison.Ordinal);
    }

    private static Uri ResolveProductUIBaseAddress()
    {
        var configured = Environment.GetEnvironmentVariable(FlutterHostingExtensions.UIBaseEnvironmentVariable);
        return string.IsNullOrWhiteSpace(configured)
            ? DefaultUIBaseAddress
            : new Uri(configured.TrimEnd('/') + "/");
    }

    private static async Task<SceneOpenedProjection> ReadNextSceneOpenedAsync(
        StreamReader reader,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);

        string? dataLine = null;
        string? eventName = null;
        while (!linked.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(linked.Token);
            if (line is null)
            {
                break;
            }

            if (line.StartsWith(':', StringComparison.Ordinal))
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

            if (line.Length != 0 || dataLine is null)
            {
                continue;
            }

            var name = eventName;
            var payload = dataLine;
            eventName = null;
            dataLine = null;

            if (name is not null && !string.Equals(name, FlutterHttpContract.SceneOpenedEvent, StringComparison.Ordinal))
            {
                continue;
            }

            if (!payload.Contains("\"sceneKey\"", StringComparison.Ordinal)
                || !payload.Contains("\"sequence\"", StringComparison.Ordinal))
            {
                continue;
            }

            var projected = JsonSerializer.Deserialize<SceneOpenedProjection>(payload, SceneOpenedJsonOptions);
            if (projected is null || string.IsNullOrWhiteSpace(projected.SceneKey) || projected.Sequence <= 0)
            {
                throw new InvalidOperationException(
                    "SSE scene-opened payload did not deserialize to a valid SceneOpenedEvent.");
            }

            return projected;
        }

        throw new TimeoutException(
            "SSE stream ended before a SceneOpened projection arrived — brain may have moved while UI projection is dead.");
    }

    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "Instantiated by JsonSerializer.Deserialize via reflection, invisible to the analyzer.")]
    private sealed record SceneOpenedProjection(long Sequence, string SceneKey, string Title, string CommandId, string Shell);
}
