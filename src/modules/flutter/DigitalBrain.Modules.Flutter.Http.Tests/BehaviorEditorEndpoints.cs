using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;
using DigitalBrain.Flutter;
using Xunit;

namespace DigitalBrain.UI.Tests;

public sealed class BehaviorEditorEndpoints(UIFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact(DisplayName =
        "GET /behaviors/{id} returns AccountEnrichment seed source and Empty status through the rail")]
    public async Task ReadReturnsSeededAccountEnrichmentDocument()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var app = await UIFixture.StartUiHttpAsync(test, cancellationToken);
        using var http = CreateClient(app);

        var document = await http.GetFromJsonAsync<BehaviorEditorDocument>(
            BehaviorPath(UiHttpContract.AccountEnrichmentBehaviorId),
            Json,
            cancellationToken);

        Assert.NotNull(document);
        Assert.Equal(UiHttpContract.AccountEnrichmentBehaviorId, document.BehaviorId);
        Assert.Equal(nameof(BehaviorRevisionStatus.Empty), document.Status);
        Assert.Contains("AccountEnrichmentProgram", document.ProgramSource, StringComparison.Ordinal);
        Assert.Contains("Feature: account enrichment", document.FeatureText, StringComparison.Ordinal);
        Assert.Equal(AccountEnrichmentEditorSeed.FeatureName, document.FeatureName);
    }

    [Fact(DisplayName =
        "POST propose creates a rail proposal and never mutates the active revision")]
    public async Task ProposeCreatesRailProposalWithoutActivating()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var rail = test.Neuron<IBehaviorNeuron>(UiHttpContract.AccountEnrichmentBehaviorId);
        await using var app = await UIFixture.StartUiHttpAsync(test, cancellationToken);
        using var http = CreateClient(app);

        var proposedWait = rail.Outgoing.NextAsync<BehaviorRevisionProposed>(cancellationToken);
        using var response = await http.PostAsJsonAsync(
            ProposePath(UiHttpContract.AccountEnrichmentBehaviorId),
            new ProposeBehaviorRequest(
                AccountEnrichmentEditorSeed.ProgramSource,
                AccountEnrichmentEditorSeed.FeatureText,
                AccountEnrichmentEditorSeed.FeatureName,
                AccountEnrichmentEditorSeed.DisplayName,
                AccountEnrichmentEditorSeed.Description),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<BehaviorEditorDocument>(Json, cancellationToken);
        Assert.NotNull(document);
        Assert.Equal(nameof(BehaviorRevisionStatus.Proposed), document.Status);
        Assert.False(string.IsNullOrWhiteSpace(document.ProposedArtifactHash));
        Assert.Null(document.ActiveArtifactHash);

        var proposed = (await proposedWait).Synapse;
        Assert.Equal(document.ProposedArtifactHash, proposed.ArtifactHash);
        Assert.Equal(UiHttpContract.AccountEnrichmentBehaviorId, proposed.Behavior.Value);
    }

    [Fact(DisplayName =
        "POST tests runs the BDD gate for a proposed artifact hash through the rail")]
    public async Task RunTestsExercisesRailBddGate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var app = await UIFixture.StartUiHttpAsync(test, cancellationToken);
        using var http = CreateClient(app);

        var proposed = await ProposeAsync(http, cancellationToken);
        using var response = await http.PostAsJsonAsync(
            TestsPath(UiHttpContract.AccountEnrichmentBehaviorId),
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
        var shell = test.Neuron<IShell>(UIFixture.DefaultShellName);
        await using var app = await UIFixture.StartUiHttpAsync(test, cancellationToken);
        using var http = CreateClient(app);

        var openedWait = shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        using var response = await http.GetAsync(
            new Uri(
                $"{UiHttpContract.BehaviorEditorSurfacePath}?behaviorId={UiHttpContract.AccountEnrichmentBehaviorId}&shell={UIFixture.DefaultShellName}",
                UriKind.Relative),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("behavior-editor", html, StringComparison.Ordinal);
        Assert.Contains(UiHttpContract.AccountEnrichmentBehaviorId, html, StringComparison.Ordinal);
        Assert.Contains("/monaco/behavior-editor.js", html, StringComparison.Ordinal);

        var opened = (await openedWait).Synapse;
        Assert.Equal(UiHttpContract.BehaviorEditorSceneKey, opened.SceneKey);
        Assert.Equal(UiHttpContract.BehaviorEditorSceneTitle, opened.Title);
    }

    private static async Task<BehaviorEditorDocument> ProposeAsync(
        HttpClient http,
        CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(
            ProposePath(UiHttpContract.AccountEnrichmentBehaviorId),
            new ProposeBehaviorRequest(
                AccountEnrichmentEditorSeed.ProgramSource,
                AccountEnrichmentEditorSeed.FeatureText,
                AccountEnrichmentEditorSeed.FeatureName,
                AccountEnrichmentEditorSeed.DisplayName,
                AccountEnrichmentEditorSeed.Description),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BehaviorEditorDocument>(Json, cancellationToken))!;
    }

    private static HttpClient CreateClient(WebApplication app)
        => new() { BaseAddress = new Uri(app.Urls.Single()) };

    private static string BehaviorPath(string behaviorId)
        => UiHttpContract.BehaviorPath.Replace("{behaviorId}", behaviorId, StringComparison.Ordinal);

    private static string ProposePath(string behaviorId)
        => UiHttpContract.BehaviorProposePath.Replace("{behaviorId}", behaviorId, StringComparison.Ordinal);

    private static string TestsPath(string behaviorId)
        => UiHttpContract.BehaviorTestsPath.Replace("{behaviorId}", behaviorId, StringComparison.Ordinal);
}
