using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;
using DigitalBrain.Shell;
using Xunit;

namespace DigitalBrain.OS.UiEdge.Tests;

public sealed class BehaviorEditorEndpoints(UiEdgeFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact(DisplayName =
        "GET /behaviors/{id} projects Empty status and blank editor fields for a behavior with no revisions")]
    public async Task ReadReturnsEmptyDocumentForUninstalledBehavior()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var app = await UiEdgeFixture.StartUiHttpAsync(test, cancellationToken);
        using var http = CreateClient(app);

        var document = await http.GetFromJsonAsync<BehaviorEditorDocument>(
            BehaviorPath(UiEdgeContract.AccountEnrichmentBehaviorId),
            Json,
            cancellationToken);

        Assert.NotNull(document);
        Assert.Equal(UiEdgeContract.AccountEnrichmentBehaviorId, document.BehaviorId);
        Assert.Equal(nameof(BehaviorRevisionStatus.Empty), document.Status);
        Assert.Equal(nameof(BehaviorRunState.Idle), document.RunState);
        Assert.False(document.ActivationGateOpen);
        Assert.Empty(document.ProgramSource);
        Assert.Empty(document.FeatureText);
        Assert.Equal("install", document.FeatureName);
        Assert.Equal(UiEdgeContract.AccountEnrichmentBehaviorId, document.DisplayName);
    }

    [Fact(DisplayName =
        "POST propose creates a rail proposal and never mutates the active revision")]
    public async Task ProposeCreatesRailProposalWithoutActivating()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var rail = test.Neuron<IBehaviorNeuron>(UiEdgeContract.AccountEnrichmentBehaviorId);
        await using var app = await UiEdgeFixture.StartUiHttpAsync(test, cancellationToken);
        using var http = CreateClient(app);

        var proposedWait = rail.Outgoing.NextAsync<BehaviorRevisionProposed>(cancellationToken);
        using var response = await http.PostAsJsonAsync(
            ProposePath(UiEdgeContract.AccountEnrichmentBehaviorId),
            new ProposeBehaviorRequest(
                AccountEnrichmentTestProgram.ProgramSource,
                AccountEnrichmentTestProgram.FeatureText,
                AccountEnrichmentTestProgram.FeatureName,
                AccountEnrichmentTestProgram.DisplayName,
                AccountEnrichmentTestProgram.Description),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<BehaviorEditorDocument>(Json, cancellationToken);
        Assert.NotNull(document);
        Assert.Equal(nameof(BehaviorRevisionStatus.Proposed), document.Status);
        Assert.False(string.IsNullOrWhiteSpace(document.ProposedArtifactHash));
        Assert.Null(document.ActiveArtifactHash);
        Assert.Contains("AccountEnrichmentProgram", document.ProgramSource, StringComparison.Ordinal);

        var proposed = (await proposedWait).Synapse;
        Assert.Equal(document.ProposedArtifactHash, proposed.ArtifactHash);
        Assert.Equal(UiEdgeContract.AccountEnrichmentBehaviorId, proposed.Behavior.Value);
    }

    [Fact(DisplayName =
        "POST tests runs the BDD gate for a proposed artifact hash through the rail")]
    public async Task RunTestsExercisesRailBddGate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var app = await UiEdgeFixture.StartUiHttpAsync(test, cancellationToken);
        using var http = CreateClient(app);

        var proposed = await ProposeAsync(http, cancellationToken);
        using var response = await http.PostAsJsonAsync(
            TestsPath(UiEdgeContract.AccountEnrichmentBehaviorId),
            new RunBehaviorTestsRequest(proposed.ProposedArtifactHash!),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<BehaviorEditorDocument>(Json, cancellationToken);
        Assert.NotNull(document);
        Assert.Equal(nameof(BehaviorRevisionStatus.TestsPassed), document.Status);
        Assert.True(document.TestsPassed);
    }

    [Fact(DisplayName =
        "GET behavior-editor surface journals SceneOpened and serves the HTML shell")]
    public async Task BehaviorEditorSurfaceJournalsSceneOpened()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>(UiEdgeFixture.DefaultShellName);
        await using var app = await UiEdgeFixture.StartUiHttpAsync(test, cancellationToken);
        using var http = CreateClient(app);

        var openedWait = shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        using var response = await http.GetAsync(
            new Uri(
                $"{UiEdgeContract.BehaviorEditorSurfacePath}?behaviorId={UiEdgeContract.AccountEnrichmentBehaviorId}&shell={UiEdgeFixture.DefaultShellName}",
                UriKind.Relative),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("behavior-editor", html, StringComparison.Ordinal);
        Assert.Contains(UiEdgeContract.AccountEnrichmentBehaviorId, html, StringComparison.Ordinal);
        Assert.Contains("/monaco/behavior-editor.js", html, StringComparison.Ordinal);

        var opened = (await openedWait).Synapse;
        Assert.Equal(UiEdgeContract.BehaviorEditorSceneKey, opened.SceneKey);
        Assert.Equal(UiEdgeContract.BehaviorEditorSceneTitle, opened.Title);
    }

    private static async Task<BehaviorEditorDocument> ProposeAsync(
        HttpClient http,
        CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(
            ProposePath(UiEdgeContract.AccountEnrichmentBehaviorId),
            new ProposeBehaviorRequest(
                AccountEnrichmentTestProgram.ProgramSource,
                AccountEnrichmentTestProgram.FeatureText,
                AccountEnrichmentTestProgram.FeatureName,
                AccountEnrichmentTestProgram.DisplayName,
                AccountEnrichmentTestProgram.Description),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BehaviorEditorDocument>(Json, cancellationToken))!;
    }

    private static HttpClient CreateClient(WebApplication app)
        => new() { BaseAddress = new Uri(app.Urls.Single()) };

    private static string BehaviorPath(string behaviorId)
        => UiEdgeContract.BehaviorPath.Replace("{behaviorId}", behaviorId, StringComparison.Ordinal);

    private static string ProposePath(string behaviorId)
        => UiEdgeContract.BehaviorProposePath.Replace("{behaviorId}", behaviorId, StringComparison.Ordinal);

    private static string TestsPath(string behaviorId)
        => UiEdgeContract.BehaviorTestsPath.Replace("{behaviorId}", behaviorId, StringComparison.Ordinal);
}
