using System.Net;
using DigitalBrain.Abstractions;
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
}
