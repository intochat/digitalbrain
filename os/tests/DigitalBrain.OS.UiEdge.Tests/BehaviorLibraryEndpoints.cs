using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Behaviors;
using Xunit;

namespace DigitalBrain.OS.UiEdge.Tests;

public sealed class BehaviorLibraryEndpoints(UiEdgeFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact(DisplayName = "GET /behaviors lists a proposed behavior with its authored overview and scenarios")]
    public async Task ListIncludesProposedBehavior()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var app = await UiEdgeFixture.StartUiHttpAsync(test, cancellationToken);
        using var http = CreateClient(app);

        await ProposeAsync(http, cancellationToken);

        var library = await http.GetFromJsonAsync<BehaviorLibraryDocument>(
            UiEdgeContract.BehaviorsPath,
            Json,
            cancellationToken);

        Assert.NotNull(library);
        var item = Assert.Single(
            library.Items,
            entry => entry.BehaviorId == UiEdgeContract.AccountEnrichmentBehaviorId);
        Assert.Equal(AccountEnrichmentTestProgram.DisplayName, item.DisplayName);
        Assert.Equal(nameof(BehaviorRevisionStatus.Proposed), item.Status);
        Assert.Equal(nameof(BehaviorRunState.Idle), item.RunState);
        Assert.Equal("pending", item.Health);
        Assert.False(string.IsNullOrWhiteSpace(item.Overview));
        Assert.NotEmpty(item.ScenarioTitles);
    }

    [Fact(DisplayName = "GET /behaviors/{id} projects run state, overview, scenarios, and revisions")]
    public async Task DetailProjectsStudioFields()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var app = await UiEdgeFixture.StartUiHttpAsync(test, cancellationToken);
        using var http = CreateClient(app);

        await ProposeAsync(http, cancellationToken);

        var document = await http.GetFromJsonAsync<BehaviorEditorDocument>(
            Path(UiEdgeContract.BehaviorPath),
            Json,
            cancellationToken);

        Assert.NotNull(document);
        Assert.Equal(nameof(BehaviorRunState.Idle), document.RunState);
        Assert.False(document.ActivationGateOpen);
        Assert.NotEmpty(document.Overview);
        Assert.NotEmpty(document.Scenarios);
        Assert.Empty(document.Bindings);
        var revision = Assert.Single(document.Revisions);
        Assert.Equal(nameof(BehaviorRevisionStatus.Proposed), revision.Status);
        Assert.False(revision.IsActive);
        Assert.Equal(0, document.ActiveTaskCount);
    }

    private static async Task ProposeAsync(HttpClient http, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(
            Path(UiEdgeContract.BehaviorProposePath),
            new ProposeBehaviorRequest(
                AccountEnrichmentTestProgram.ProgramSource,
                AccountEnrichmentTestProgram.FeatureText,
                AccountEnrichmentTestProgram.FeatureName,
                AccountEnrichmentTestProgram.DisplayName,
                AccountEnrichmentTestProgram.Description),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static HttpClient CreateClient(WebApplication app)
        => new() { BaseAddress = new Uri(app.Urls.Single()) };

    private static string Path(string template)
        => template.Replace(
            "{behaviorId}",
            UiEdgeContract.AccountEnrichmentBehaviorId,
            StringComparison.Ordinal);
}
