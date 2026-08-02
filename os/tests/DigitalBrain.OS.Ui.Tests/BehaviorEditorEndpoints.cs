using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;
using DigitalBrain.Shell;
using Xunit;

namespace DigitalBrain.Flutter.Http.Tests;

public sealed class BehaviorEditorEndpoints(FlutterHttpFixture fixture)
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
        await using var app = await FlutterHttpFixture.StartUiHttpAsync(test, cancellationToken);
        using var http = CreateClient(app);

        var document = await http.GetFromJsonAsync<BehaviorEditorDocument>(
            BehaviorPath(FlutterHttpContract.AccountEnrichmentBehaviorId),
            Json,
            cancellationToken);

        Assert.NotNull(document);
        Assert.Equal(FlutterHttpContract.AccountEnrichmentBehaviorId, document.BehaviorId);
        Assert.Equal(nameof(BehaviorRevisionStatus.Empty), document.Status);
        Assert.Equal(nameof(BehaviorRunState.Idle), document.RunState);
        Assert.False(document.ActivationGateOpen);
        Assert.Empty(document.ProgramSource);
        Assert.Empty(document.FeatureText);
        Assert.Equal("install", document.FeatureName);
        Assert.Equal(FlutterHttpContract.AccountEnrichmentBehaviorId, document.DisplayName);
    }

    [Fact(DisplayName =
        "POST propose creates a rail proposal and never mutates the active revision")]
    public async Task ProposeCreatesRailProposalWithoutActivating()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var rail = test.Neuron<IBehaviorNeuron>(FlutterHttpContract.AccountEnrichmentBehaviorId);
        await using var app = await FlutterHttpFixture.StartUiHttpAsync(test, cancellationToken);
        using var http = CreateClient(app);

        var proposedWait = rail.Outgoing.NextAsync<BehaviorRevisionProposed>(cancellationToken);
        using var response = await http.PostAsJsonAsync(
            ProposePath(FlutterHttpContract.AccountEnrichmentBehaviorId),
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
        Assert.Equal(FlutterHttpContract.AccountEnrichmentBehaviorId, proposed.Behavior.Value);
    }

    [Fact(DisplayName =
        "POST tests runs the BDD gate for a proposed artifact hash through the rail")]
    public async Task RunTestsExercisesRailBddGate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var app = await FlutterHttpFixture.StartUiHttpAsync(test, cancellationToken);
        using var http = CreateClient(app);

        var proposed = await ProposeAsync(http, cancellationToken);
        using var response = await http.PostAsJsonAsync(
            TestsPath(FlutterHttpContract.AccountEnrichmentBehaviorId),
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
        var shell = test.Neuron<IShell>(FlutterHttpFixture.DefaultShellName);
        await using var app = await FlutterHttpFixture.StartUiHttpAsync(test, cancellationToken);
        using var http = CreateClient(app);

        var openedWait = shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        using var response = await http.GetAsync(
            new Uri(
                $"{FlutterHttpContract.BehaviorEditorSurfacePath}?behaviorId={FlutterHttpContract.AccountEnrichmentBehaviorId}&shell={FlutterHttpFixture.DefaultShellName}",
                UriKind.Relative),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("behavior-editor", html, StringComparison.Ordinal);
        Assert.Contains(FlutterHttpContract.AccountEnrichmentBehaviorId, html, StringComparison.Ordinal);
        Assert.Contains("/monaco/behavior-editor.js", html, StringComparison.Ordinal);

        var opened = (await openedWait).Synapse;
        Assert.Equal(FlutterHttpContract.BehaviorEditorSceneKey, opened.SceneKey);
        Assert.Equal(FlutterHttpContract.BehaviorEditorSceneTitle, opened.Title);
    }

    private static async Task<BehaviorEditorDocument> ProposeAsync(
        HttpClient http,
        CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(
            ProposePath(FlutterHttpContract.AccountEnrichmentBehaviorId),
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
        => FlutterHttpContract.BehaviorPath.Replace("{behaviorId}", behaviorId, StringComparison.Ordinal);

    private static string ProposePath(string behaviorId)
        => FlutterHttpContract.BehaviorProposePath.Replace("{behaviorId}", behaviorId, StringComparison.Ordinal);

    private static string TestsPath(string behaviorId)
        => FlutterHttpContract.BehaviorTestsPath.Replace("{behaviorId}", behaviorId, StringComparison.Ordinal);
}
