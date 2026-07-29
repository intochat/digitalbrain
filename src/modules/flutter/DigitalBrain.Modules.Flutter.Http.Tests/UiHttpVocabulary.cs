using System.Net;
using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using Xunit;

namespace DigitalBrain.UI.Tests;

public sealed class UiHttpVocabulary(UIFixture fixture)
{
    private const string OpenTelemetryMarker = "OpenTelemetry";

    [Fact(DisplayName =
        "UiHttpContract is the closed public vocabulary — health, shell/scene routes, scene-opened event")]
    public void PublicVocabularyIsUiHttpContract()
    {
        var vocabulary = typeof(UiHttpContract).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace == typeof(UiHttpContract).Namespace)
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([nameof(UiHttpContract)], vocabulary);

        Assert.DoesNotContain(
            typeof(UiHttpContract).Assembly.GetExportedTypes(),
            type => type.Name is "ShellEventFeed" or "UIHost" or "UIEndpoints"
                or "OwnerSessionJournal" or "UiHttpServices"
                or "OpenSceneRequest" or "ActivateControlRequest" or "SceneOpenedEvent"
                or "IFlutter");

        Assert.Equal("/health", UiHttpContract.HealthPath);
        Assert.Equal("/shells/{shellName}/scenes", UiHttpContract.OpenScenePath);
        Assert.Equal("/shells/{shellName}/events", UiHttpContract.ShellEventsPath);
        Assert.Equal("/scenes/{sceneKey}/controls/{controlId}/activate", UiHttpContract.ActivateControlPath);
        Assert.Equal("afterSequence", UiHttpContract.AfterSequenceQuery);
        Assert.Equal("text/event-stream", UiHttpContract.EventStreamContentType);
        Assert.Equal("no-cache", UiHttpContract.CacheControlNoCache);
        Assert.Equal("scene-opened", UiHttpContract.SceneOpenedEvent);
        Assert.Equal("/brain/topology", UiHttpContract.BrainTopologyPath);
        Assert.Equal("/chats/{chatName}/messages/stream", UiHttpContract.StreamMessagePath);
        Assert.Equal("chat-delta", UiHttpContract.ChatDeltaEvent);
        Assert.Equal("/oauth/mcp/callback", UiHttpContract.McpOAuthCallbackPath);
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

        var watchShell = typeof(OwnerSessionJournal).GetMethod(
            nameof(OwnerSessionJournal.WatchShellOutgoingAsync),
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(watchShell);
        Assert.Contains(
            typeof(OwnerSessionJournal).GetFields(BindingFlags.NonPublic | BindingFlags.Instance),
            field => field.FieldType == typeof(ISessionNeuron));
        Assert.Contains(
            typeof(OwnerSessionJournal).GetFields(BindingFlags.NonPublic | BindingFlags.Instance),
            field => field.FieldType == typeof(IClusterClient));

        Assert.Equal(
            [
                nameof(IDigitalBrain.ActivateAsync),
                nameof(IDigitalBrain.EmitAsync),
                nameof(IDigitalBrain.Get),
                nameof(IDigitalBrain.SendAsync),
            ],
            typeof(IDigitalBrain)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName)
                .Select(method => method.Name)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    [Fact(DisplayName = "MapUIHost serves UiHttpContract health on the product composition path")]
    public async Task MapUIHostServesHealth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var app = await UIFixture.StartUiHttpAsync(test, cancellationToken);

        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var health = await http.GetAsync(new Uri(UiHttpContract.HealthPath, UriKind.Relative), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }
}
