using System.Net;
using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using Xunit;

namespace DigitalBrain.OS.UiEdge.Tests;

public sealed class UiHttpVocabulary(UiEdgeFixture fixture)
{
    private const string OpenTelemetryMarker = "OpenTelemetry";

    [Fact(DisplayName =
        "UiEdgeContract is the only exported type in its namespace — no internal type leaks")]
    public void PublicVocabularyIsUiHttpContract()
    {
        var vocabulary = typeof(UiEdgeContract).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace == typeof(UiEdgeContract).Namespace)
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([nameof(UiEdgeContract)], vocabulary);

        Assert.DoesNotContain(
            typeof(UiEdgeContract).Assembly.GetExportedTypes(),
            type => type.Name is "ShellEventFeed" or "UiEdgeHost" or "UiEdgeEndpoints"
                or "OwnerSessionJournal" or "UiEdgeServices"
                or "OpenSceneRequest" or "ActivateControlRequest" or "SceneOpenedEvent");
    }

    [Fact(DisplayName =
        "SSE shell feed uses host-private OwnerSessionJournal watch — not IDigitalBrain journal, not OpenTelemetry")]
    public void ShellSseProjectionIsEncapsulatedSessionJournalNotProductClientWatch()
    {
        var watch = typeof(ShellEventFeed).GetMethod(
            nameof(ShellEventFeed.WatchSceneOpenedAsync),
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(watch);

        var parameters = watch.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        Assert.Contains(typeof(OwnerSessionJournal), parameters);
        Assert.Contains(typeof(string), parameters);
        Assert.Contains(typeof(long), parameters);
        Assert.DoesNotContain(typeof(IGrainFactory), parameters);
        Assert.DoesNotContain(typeof(IDigitalBrain), parameters);
        Assert.DoesNotContain(typeof(OwnerId), parameters);
        Assert.DoesNotContain(
            parameters,
            type => (type.FullName ?? type.Name).Contains(OpenTelemetryMarker, StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(System.Diagnostics.Activity), parameters);
        Assert.DoesNotContain(typeof(System.Diagnostics.ActivitySource), parameters);
    }

    [Fact(DisplayName = "MapUiEdgeHost serves UiEdgeContract health on the product composition path")]
    public async Task MapUIHostServesHealth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var app = await UiEdgeFixture.StartUiHttpAsync(test, cancellationToken);

        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var health = await http.GetAsync(new Uri(UiEdgeContract.HealthPath, UriKind.Relative), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact(DisplayName = "legacy OAuth callback path /oauth/mcp/callback is no longer mapped")]
    public async Task LegacyMcpOAuthCallbackPathIsNotMapped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var app = await UiEdgeFixture.StartUiHttpAsync(test, cancellationToken);

        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var legacy = await http.GetAsync(
            new Uri("/oauth/mcp/callback?state=x&code=y", UriKind.Relative),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, legacy.StatusCode);
        Assert.Equal("/oauth/callback", UiEdgeContract.McpOAuthCallbackPath);
    }
}
