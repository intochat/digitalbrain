using System.Net;
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
        var baseAddress = UiFixture.ResolveProductUiBaseAddress();
        var shellName = $"live-{Guid.NewGuid():N}"[..16];
        var sceneKey = "live-home";
        var title = "Live Home";

        using var http = new HttpClient
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(30),
        };

        using (var health = await http.GetAsync(
                   new Uri(UiEdgeContract.HealthPath, UriKind.Relative),
                   cancellationToken))
        {
            Assert.True(
                health.IsSuccessStatusCode,
                $"Product {UiFixture.DefaultUiResourceName} {UiEdgeContract.HealthPath} not OK at {baseAddress}. Start: aspire start --project hosts/DigitalBrain.AppHost. Status={(int)health.StatusCode}. Override with {UiFixture.UiBaseEnvironmentVariable}.");
        }

        using var streamRequest = new HttpRequestMessage(
            HttpMethod.Get,
            UiEdgeSse.ShellEvents(shellName, afterSequence: 0));
        using var streamResponse = await http.SendAsync(
            streamRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, streamResponse.StatusCode);
        Assert.Equal(
            UiEdgeContract.EventStreamContentType,
            streamResponse.Content.Headers.ContentType?.MediaType);

        await using var body = await streamResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(body);

        using var openResponse = await http.PostAsJsonAsync(
            UiEdgeSse.OpenScene(shellName),
            new OpenSceneRequest(sceneKey, title),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, openResponse.StatusCode);

        var projected = await UiEdgeSse.ReadNextSceneOpenedAsync(
            reader,
            cancellationToken,
            timeout: TimeSpan.FromSeconds(20));

        Assert.Equal(sceneKey, projected.SceneKey);
        Assert.Equal(title, projected.Title);
        Assert.True(projected.Sequence > 0);
        Assert.False(string.IsNullOrWhiteSpace(projected.CommandId));
        Assert.Contains(shellName, projected.Shell, StringComparison.Ordinal);
    }
}
