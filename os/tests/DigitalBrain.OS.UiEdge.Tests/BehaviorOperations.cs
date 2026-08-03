using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;
using DigitalBrain.Tasks;
using Xunit;

namespace DigitalBrain.OS.UiEdge.Tests;

public sealed class BehaviorOperations(UiEdgeFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact(DisplayName = "Stop/start lifecycle projects Running → Stopped → Running without rewriting revision")]
    public async Task StopStartLifecycleIsTruthful()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var app = await UiEdgeFixture.StartUiHttpAsync(test, cancellationToken);
        using var http = CreateClient(app);

        var active = await InstallAsync(http, cancellationToken);
        Assert.Equal(nameof(BehaviorRevisionStatus.Active), active.Status);
        Assert.Equal(nameof(BehaviorRunState.Running), active.RunState);
        Assert.True(active.ActivationGateOpen);
        var hash = active.ActiveArtifactHash;
        Assert.False(string.IsNullOrWhiteSpace(hash));

        using var stopResponse = await http.PostAsync(
            new Uri(Path(UiEdgeContract.BehaviorStopPath), UriKind.Relative),
            content: null,
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, stopResponse.StatusCode);
        var stopped = await stopResponse.Content.ReadFromJsonAsync<BehaviorEditorDocument>(Json, cancellationToken);
        Assert.NotNull(stopped);
        Assert.Equal(nameof(BehaviorRunState.Stopped), stopped.RunState);
        Assert.False(stopped.ActivationGateOpen);
        Assert.Equal(hash, stopped.ActiveArtifactHash);
        Assert.Equal(nameof(BehaviorRevisionStatus.Active), stopped.Status);

        using var startResponse = await http.PostAsync(
            new Uri(Path(UiEdgeContract.BehaviorStartPath), UriKind.Relative),
            content: null,
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        var started = await startResponse.Content.ReadFromJsonAsync<BehaviorEditorDocument>(Json, cancellationToken);
        Assert.NotNull(started);
        Assert.Equal(nameof(BehaviorRunState.Running), started.RunState);
        Assert.True(started.ActivationGateOpen);
        Assert.Equal(hash, started.ActiveArtifactHash);
    }

    [Fact(DisplayName = "Run once executes through the rail and returns outcome without auto-publishing red evidence")]
    public async Task RunOnceReturnsOutcome()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var app = await UiEdgeFixture.StartUiHttpAsync(test, cancellationToken);
        using var http = CreateClient(app);

        await InstallAsync(http, cancellationToken);

        using var response = await http.PostAsJsonAsync(
            Path(UiEdgeContract.BehaviorRunOncePath),
            new RunOnceBehaviorRequest(
                "EnrichTrigger",
                """{"MessageId":"m1","AccountId":"a1","GmailAccount":"g1"}"""),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RunOnceBehaviorResult>(Json, cancellationToken);
        Assert.NotNull(result);
        Assert.NotNull(result.Document);
        Assert.False(string.IsNullOrWhiteSpace(result.Outcome));
    }

    [Fact(DisplayName = "scenario-first change proposal returns feature diff before source generation")]
    public async Task ChangeProposalIsScenarioFirst()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var app = await UiEdgeFixture.StartUiHttpAsync(test, cancellationToken);
        using var http = CreateClient(app);

        using var proposeResponse = await http.PostAsJsonAsync(
            Path(UiEdgeContract.BehaviorChangeProposePath),
            new BehaviorChangeProposeRequest("also enrich phone numbers"),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, proposeResponse.StatusCode);
        var proposal = await proposeResponse.Content.ReadFromJsonAsync<BehaviorChangeProposalDocument>(
            Json,
            cancellationToken);
        Assert.NotNull(proposal);
        Assert.Equal("awaiting-scenario-approval", proposal.Status);
        Assert.Contains("Scenario:", proposal.ProposedFeatureText, StringComparison.Ordinal);
        Assert.DoesNotContain("class ", proposal.ProposedFeatureText, StringComparison.Ordinal);

        using var approveResponse = await http.PostAsJsonAsync(
            Path(UiEdgeContract.BehaviorChangeApprovePath),
            new BehaviorScenarioApprovalRequest(proposal.ProposalId, Approved: true),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        var document = await approveResponse.Content.ReadFromJsonAsync<BehaviorEditorDocument>(Json, cancellationToken);
        Assert.NotNull(document);
        Assert.Equal(nameof(BehaviorRevisionStatus.Proposed), document.Status);
        Assert.Contains("also enrich phone numbers", document.FeatureText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            AccountEnrichmentTestProgram.AuthoredProgramTypeName,
            document.ProgramSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("public sealed class Program {}", document.ProgramSource, StringComparison.Ordinal);
    }

    [Fact(DisplayName =
        "a scenario proposal outlives the UI edge process: a second edge instance approves the same proposal")]
    public async Task ProposalOutlivesTheEdgeThatDraftedIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);

        BehaviorChangeProposalDocument proposal;
        await using (var drafting = await UiEdgeFixture.StartUiHttpAsync(test, cancellationToken))
        {
            using var http = CreateClient(drafting);
            using var response = await http.PostAsJsonAsync(
                Path(UiEdgeContract.BehaviorChangeProposePath),
                new BehaviorChangeProposeRequest("also enrich phone numbers"),
                cancellationToken);
            response.EnsureSuccessStatusCode();
            proposal = (await response.Content.ReadFromJsonAsync<BehaviorChangeProposalDocument>(
                Json,
                cancellationToken))!;
        }

        await using var approving = await UiEdgeFixture.StartUiHttpAsync(test, cancellationToken);
        using var restarted = CreateClient(approving);
        using var approve = await restarted.PostAsJsonAsync(
            Path(UiEdgeContract.BehaviorChangeApprovePath),
            new BehaviorScenarioApprovalRequest(proposal.ProposalId, Approved: true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        var document = await approve.Content.ReadFromJsonAsync<BehaviorEditorDocument>(Json, cancellationToken);
        Assert.NotNull(document);
        Assert.Equal(nameof(BehaviorRevisionStatus.Proposed), document.Status);
        Assert.Contains(
            AccountEnrichmentTestProgram.AuthoredProgramTypeName,
            document.ProgramSource,
            StringComparison.Ordinal);
    }

    [Fact(DisplayName = "a rejected scenario proposal is forgotten, so approving it afterwards is not found")]
    public async Task RejectionForgetsTheProposal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var app = await UiEdgeFixture.StartUiHttpAsync(test, cancellationToken);
        using var http = CreateClient(app);

        using var propose = await http.PostAsJsonAsync(
            Path(UiEdgeContract.BehaviorChangeProposePath),
            new BehaviorChangeProposeRequest("stop enriching dormant accounts"),
            cancellationToken);
        propose.EnsureSuccessStatusCode();
        var proposal = (await propose.Content.ReadFromJsonAsync<BehaviorChangeProposalDocument>(
            Json,
            cancellationToken))!;

        using var reject = await http.PostAsJsonAsync(
            Path(UiEdgeContract.BehaviorChangeApprovePath),
            new BehaviorScenarioApprovalRequest(proposal.ProposalId, Approved: false),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        var rejected = await reject.Content.ReadFromJsonAsync<BehaviorChangeProposalDocument>(
            Json,
            cancellationToken);
        Assert.NotNull(rejected);
        Assert.Equal("rejected", rejected.Status);

        using var again = await http.PostAsJsonAsync(
            Path(UiEdgeContract.BehaviorChangeApprovePath),
            new BehaviorScenarioApprovalRequest(proposal.ProposalId, Approved: true),
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact(DisplayName = "binding enable/disable flips Enabled without deleting the registered binding")]
    public async Task BindingEnableDisablePreservesRegistration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var behavior = test.Neuron<IBehaviorNeuron>(UiEdgeContract.AccountEnrichmentBehaviorId);
        var task = test.Neuron<ITask>("studio-binding-task");
        var worker = test.Neuron<IWorker>("studio-binding-worker");
        await using var app = await UiEdgeFixture.StartUiHttpAsync(test, cancellationToken);
        using var http = CreateClient(app);

        var active = await InstallAsync(http, cancellationToken);
        var binding = BehaviorActivationBindings.ForExistingTask(
            task.Id,
            worker.Id,
            new BehaviorId(UiEdgeContract.AccountEnrichmentBehaviorId),
            new BehaviorRevisionId(active.ActiveArtifactHash!),
            contractVersion: "1",
            caseId: "case.EnrichTrigger",
            protectedPayload: new ProtectedPayloadReference(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")));

        await behavior.Reference.ActivateBound(
            new ActivateBoundBehavior(CommandId.New(), active.ActiveArtifactHash!, binding));

        var document = await http.GetFromJsonAsync<BehaviorEditorDocument>(
            Path(UiEdgeContract.BehaviorPath),
            Json,
            cancellationToken);
        Assert.NotNull(document);
        var registered = Assert.Single(document.Bindings);
        Assert.True(registered.Enabled);
        Assert.Equal("opaque", registered.ConfigurationHint);

        using var disable = await http.PostAsJsonAsync(
            Path(UiEdgeContract.BehaviorBindingPath).Replace("{bindingId}", registered.BindingId, StringComparison.Ordinal),
            new SetBehaviorBindingRequest(Enabled: false),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        var disabled = await disable.Content.ReadFromJsonAsync<BehaviorEditorDocument>(Json, cancellationToken);
        Assert.NotNull(disabled);
        Assert.False(Assert.Single(disabled.Bindings).Enabled);

        using var enable = await http.PostAsJsonAsync(
            Path(UiEdgeContract.BehaviorBindingPath).Replace("{bindingId}", registered.BindingId, StringComparison.Ordinal),
            new SetBehaviorBindingRequest(Enabled: true),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        var enabled = await enable.Content.ReadFromJsonAsync<BehaviorEditorDocument>(Json, cancellationToken);
        Assert.NotNull(enabled);
        Assert.True(Assert.Single(enabled.Bindings).Enabled);
    }

    private static async Task<BehaviorEditorDocument> InstallAsync(
        HttpClient http,
        CancellationToken cancellationToken)
    {
        using var propose = await http.PostAsJsonAsync(
            Path(UiEdgeContract.BehaviorProposePath),
            new ProposeBehaviorRequest(
                AccountEnrichmentTestProgram.ProgramSource,
                AccountEnrichmentTestProgram.FeatureText,
                AccountEnrichmentTestProgram.FeatureName,
                AccountEnrichmentTestProgram.DisplayName,
                AccountEnrichmentTestProgram.Description),
            cancellationToken);
        propose.EnsureSuccessStatusCode();
        var proposed = (await propose.Content.ReadFromJsonAsync<BehaviorEditorDocument>(Json, cancellationToken))!;

        using var tests = await http.PostAsJsonAsync(
            Path(UiEdgeContract.BehaviorTestsPath),
            new RunBehaviorTestsRequest(proposed.ProposedArtifactHash!),
            cancellationToken);
        tests.EnsureSuccessStatusCode();

        var approvalId = Guid.NewGuid().ToString("D");
        using var approve = await http.PostAsJsonAsync(
            Path(UiEdgeContract.BehaviorApprovePath),
            new ApproveBehaviorRequest(proposed.ProposedArtifactHash!, approvalId),
            cancellationToken);
        approve.EnsureSuccessStatusCode();

        using var activate = await http.PostAsJsonAsync(
            Path(UiEdgeContract.BehaviorActivatePath),
            new ActivateBehaviorRequest(proposed.ProposedArtifactHash!),
            cancellationToken);
        activate.EnsureSuccessStatusCode();
        return (await activate.Content.ReadFromJsonAsync<BehaviorEditorDocument>(Json, cancellationToken))!;
    }

    private static HttpClient CreateClient(WebApplication app)
        => new() { BaseAddress = new Uri(app.Urls.Single()) };

    private static string Path(string template)
        => template.Replace(
            "{behaviorId}",
            UiEdgeContract.AccountEnrichmentBehaviorId,
            StringComparison.Ordinal);
}
