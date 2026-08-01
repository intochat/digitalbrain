using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Behaviors;
using Xunit;

namespace DigitalBrain.Flutter.Http.Tests;

public sealed class BehaviorLibraryEndpoints(FlutterHttpFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact(DisplayName = "GET /behaviors lists the seeded AccountEnrichment draft for the owner")]
    public async Task ListIncludesSeededAccountEnrichment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var app = await FlutterHttpFixture.StartUiHttpAsync(test, cancellationToken);
        using var http = CreateClient(app);

        var library = await http.GetFromJsonAsync<BehaviorLibraryDocument>(
            FlutterHttpContract.BehaviorsPath,
            Json,
            cancellationToken);

        Assert.NotNull(library);
        var item = Assert.Single(
            library.Items,
            entry => entry.BehaviorId == FlutterHttpContract.AccountEnrichmentBehaviorId);
        Assert.Equal(AccountEnrichmentEditorSeed.DisplayName, item.DisplayName);
        Assert.Equal(nameof(BehaviorRevisionStatus.Empty), item.Status);
        Assert.Equal(nameof(BehaviorRunState.Idle), item.RunState);
        Assert.Equal("draft", item.Health);
        Assert.False(string.IsNullOrWhiteSpace(item.Overview));
        Assert.NotEmpty(item.ScenarioTitles);
    }

    [Fact(DisplayName = "GET /behaviors/{id} projects run state, overview, scenarios, and revisions")]
    public async Task DetailProjectsStudioFields()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var app = await FlutterHttpFixture.StartUiHttpAsync(test, cancellationToken);
        using var http = CreateClient(app);

        var document = await http.GetFromJsonAsync<BehaviorEditorDocument>(
            Path(FlutterHttpContract.BehaviorPath),
            Json,
            cancellationToken);

        Assert.NotNull(document);
        Assert.Equal(nameof(BehaviorRunState.Idle), document.RunState);
        Assert.False(document.ActivationGateOpen);
        Assert.NotEmpty(document.Overview);
        Assert.NotEmpty(document.Scenarios);
        Assert.Empty(document.Bindings);
        Assert.Empty(document.Revisions);
        Assert.Equal(0, document.ActiveTaskCount);
    }

    private static HttpClient CreateClient(WebApplication app)
        => new() { BaseAddress = new Uri(app.Urls.Single()) };

    private static string Path(string template)
        => template.Replace(
            "{behaviorId}",
            FlutterHttpContract.AccountEnrichmentBehaviorId,
            StringComparison.Ordinal);
}
