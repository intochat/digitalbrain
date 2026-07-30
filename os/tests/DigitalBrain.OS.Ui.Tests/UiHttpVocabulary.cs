using System.Net;
using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using Xunit;

namespace DigitalBrain.Flutter.Http.Tests;

public sealed class UiHttpVocabulary(FlutterHttpFixture fixture)
{
    private const string OpenTelemetryMarker = "OpenTelemetry";

    [Fact(DisplayName =
        "FlutterHttpContract is the closed public vocabulary — health, shell/scene routes, scene-opened event")]
    public void PublicVocabularyIsUiHttpContract()
    {
        var vocabulary = typeof(FlutterHttpContract).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace == typeof(FlutterHttpContract).Namespace)
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([nameof(FlutterHttpContract)], vocabulary);

        Assert.DoesNotContain(
            typeof(FlutterHttpContract).Assembly.GetExportedTypes(),
            type => type.Name is "ShellEventFeed" or "FlutterHttpHost" or "FlutterHttpEndpoints"
                or "OwnerSessionJournal" or "FlutterHttpServices"
                or "OpenSceneRequest" or "ActivateControlRequest" or "SceneOpenedEvent"
                or "IFlutter");

        Assert.Equal("/health", FlutterHttpContract.HealthPath);
        Assert.Equal("/shells/{shellName}/scenes", FlutterHttpContract.OpenScenePath);
        Assert.Equal("/shells/{shellName}/events", FlutterHttpContract.ShellEventsPath);
        Assert.Equal("/scenes/{sceneKey}/controls/{controlId}/activate", FlutterHttpContract.ActivateControlPath);
        Assert.Equal("afterSequence", FlutterHttpContract.AfterSequenceQuery);
        Assert.Equal("text/event-stream", FlutterHttpContract.EventStreamContentType);
        Assert.Equal("no-cache", FlutterHttpContract.CacheControlNoCache);
        Assert.Equal("scene-opened", FlutterHttpContract.SceneOpenedEvent);
        Assert.Equal("/brain/topology", FlutterHttpContract.BrainTopologyPath);
        Assert.Equal("/chats/{chatName}/messages/stream", FlutterHttpContract.StreamMessagePath);
        Assert.Equal("chat-delta", FlutterHttpContract.ChatDeltaEvent);
        Assert.Equal("/oauth/mcp/callback", FlutterHttpContract.McpOAuthCallbackPath);
        Assert.Equal("/authorizations/events", FlutterHttpContract.AuthorizationEventsPath);
        Assert.Equal("authorization", FlutterHttpContract.AuthorizationEvent);
        Assert.Equal("/behaviors/{behaviorId}", FlutterHttpContract.BehaviorPath);
        Assert.Equal("/behaviors/{behaviorId}/propose", FlutterHttpContract.BehaviorProposePath);
        Assert.Equal("/behaviors/{behaviorId}/tests", FlutterHttpContract.BehaviorTestsPath);
        Assert.Equal("/behaviors/{behaviorId}/approve", FlutterHttpContract.BehaviorApprovePath);
        Assert.Equal("/surfaces/behavior-editor", FlutterHttpContract.BehaviorEditorSurfacePath);
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

    [Fact(DisplayName = "MapFlutterHttpHost serves FlutterHttpContract health on the product composition path")]
    public async Task MapUIHostServesHealth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var app = await FlutterHttpFixture.StartUiHttpAsync(test, cancellationToken);

        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var health = await http.GetAsync(new Uri(FlutterHttpContract.HealthPath, UriKind.Relative), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }
}
