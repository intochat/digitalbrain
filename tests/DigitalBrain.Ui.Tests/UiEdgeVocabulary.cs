using System.Net;
using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using Xunit;

namespace DigitalBrain.Ui.Tests;

public sealed class UiEdgeVocabulary(UiFixture fixture)
{
    private const string OpenTelemetryMarker = "OpenTelemetry";

    [Fact(DisplayName =
        "Ui host public edge vocabulary is UiEdgeContract — health, shell/scene routes, scene-opened event")]
    public void PublicEdgeVocabularyIsUiEdgeContract()
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
            type => type.Name is "ShellEventFeed" or "UiHost" or "UiEndpoints"
                or "OwnerSessionJournal" or "UiEdgeServices"
                or "OpenSceneRequest" or "ActivateControlRequest" or "SceneOpenedEvent"
                or "IFlutter");

        Assert.Equal("/health", UiEdgeContract.HealthPath);
        Assert.Equal("healthy", UiEdgeContract.HealthResponse);
        Assert.Equal("/shells/{shellName}/scenes", UiEdgeContract.OpenScenePath);
        Assert.Equal("/shells/{shellName}/events", UiEdgeContract.ShellEventsPath);
        Assert.Equal(
            "/scenes/{sceneKey}/controls/{controlId}/activate",
            UiEdgeContract.ActivateControlPath);
        Assert.Equal("afterSequence", UiEdgeContract.AfterSequenceQuery);
        Assert.Equal("text/event-stream", UiEdgeContract.EventStreamContentType);
        Assert.Equal("no-cache", UiEdgeContract.CacheControlNoCache);
        Assert.Equal("scene-opened", UiEdgeContract.SceneOpenedEvent);
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

    [Fact(DisplayName = "MapUiHost serves UiEdgeContract health on the product edge composition path")]
    public async Task MapUiHostServesHealth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var app = await UiFixture.StartUiEdgeAsync(test, cancellationToken);

        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var health = await http.GetAsync(
            new Uri(UiEdgeContract.HealthPath, UriKind.Relative),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(
            $"\"{UiEdgeContract.HealthResponse}\"",
            (await health.Content.ReadAsStringAsync(cancellationToken)).Trim());
    }
}
