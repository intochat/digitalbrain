using System.Net;
using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using Xunit;

namespace DigitalBrain.UI.Tests;

public sealed class UIEdgeVocabulary(UIFixture fixture)
{
    private const string OpenTelemetryMarker = "OpenTelemetry";

    [Fact(DisplayName =
        "UI host public edge vocabulary is UIEdgeContract — health, shell/scene routes, scene-opened event")]
    public void PublicEdgeVocabularyIsUIEdgeContract()
    {
        var vocabulary = typeof(UIEdgeContract).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace == typeof(UIEdgeContract).Namespace)
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([nameof(UIEdgeContract)], vocabulary);

        Assert.DoesNotContain(
            typeof(UIEdgeContract).Assembly.GetExportedTypes(),
            type => type.Name is "ShellEventFeed" or "UIHost" or "UIEndpoints"
                or "OwnerSessionJournal" or "UIEdgeServices"
                or "OpenSceneRequest" or "ActivateControlRequest" or "SceneOpenedEvent"
                or "IFlutter");

        Assert.Equal("/health", UIEdgeContract.HealthPath);
        Assert.Equal("/shells/{shellName}/scenes", UIEdgeContract.OpenScenePath);
        Assert.Equal("/shells/{shellName}/events", UIEdgeContract.ShellEventsPath);
        Assert.Equal("/scenes/{sceneKey}/controls/{controlId}/activate", UIEdgeContract.ActivateControlPath);
        Assert.Equal("afterSequence", UIEdgeContract.AfterSequenceQuery);
        Assert.Equal("text/event-stream", UIEdgeContract.EventStreamContentType);
        Assert.Equal("no-cache", UIEdgeContract.CacheControlNoCache);
        Assert.Equal("scene-opened", UIEdgeContract.SceneOpenedEvent);
        Assert.Equal("/brain/topology", UIEdgeContract.BrainTopologyPath);
        Assert.Equal("/chats/{chatName}/messages/stream", UIEdgeContract.StreamMessagePath);
        Assert.Equal("chat-delta", UIEdgeContract.ChatDeltaEvent);
    }

    [Fact(DisplayName =
        "SSE shell feed uses host-private OwnerSessionJournal — not IDigitalBrain journal, not OpenTelemetry")]
    public void ShellSseProjectionIsEncapsulatedSessionJournalNotProductClientWatch()
    {
        var write = typeof(ShellEventFeed).GetMethod(
            nameof(ShellEventFeed.WriteSceneOpenedSseAsync),
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(write);

        var parameters = write.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
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

        var readShell = typeof(OwnerSessionJournal).GetMethod(
            nameof(OwnerSessionJournal.ReadShellOutgoingAsync),
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(readShell);
        Assert.Equal(typeof(Task<JournalRead>), readShell.ReturnType);
        Assert.Contains(
            typeof(OwnerSessionJournal).GetFields(BindingFlags.NonPublic | BindingFlags.Instance),
            field => field.FieldType == typeof(ISessionNeuron));

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

    [Fact(DisplayName = "MapUIHost serves UIEdgeContract health on the product edge composition path")]
    public async Task MapUIHostServesHealth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var app = await UIFixture.StartUIEdgeAsync(test, cancellationToken);

        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var health = await http.GetAsync(new Uri(UIEdgeContract.HealthPath, UriKind.Relative), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }
}
