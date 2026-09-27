using System.ComponentModel;
using System.Reflection;
using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Specs;
using IntoChat.Packages;
using ModelContextProtocol.Server;

namespace IntoChat.Marketplace;

internal sealed record AppStepView(int Line, string Step, string Verdict, string? Message);
internal sealed record AppScenarioView(string Name, string Verdict, IReadOnlyList<AppStepView> Steps);
internal sealed record AppDraftSummary(string DraftId, string Status, string Name, string Title, string Runtime, string Spec,
    IReadOnlyList<string> UnboundSteps, IReadOnlyList<AppDraftAttempt> Attempts, string? Published, string Error);
internal sealed record AppSpecSummary(string Package, string Revision, string Runtime, bool? Green, IReadOnlyList<AppScenarioView> Scenarios);
internal sealed record AppAnswer(string Status, string? Answer, string? Error, IReadOnlyList<string> Discussion);

// The marketplace as MCP tools, so an agent can do what a person does in the Apps screen: describe an
// app, read and adjust its scenarios, build it, then install and use it. Scoped to the route's workspace.
[McpServerToolType]
internal sealed class AppTools(IDigitalBrain brain, PackageService packages, MarketplaceService marketplace, IHttpContextAccessor http)
{
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromMinutes(10);

    public static readonly MethodInfo[] Methods = [.. typeof(AppTools).GetMethods()
        .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)];

    private string WorkspaceId => http.HttpContext?.Request.RouteValues["workspaceId"]?.ToString()
        ?? throw new ArgumentException("Workspace is required.");

    [McpServerTool(Name = "apps_list"), Description("List the apps published in the marketplace: package id (owner/name), title and description.")]
    public async Task<IReadOnlyList<object>> List()
        => [.. (await packages.List()).Select(listing => new { package = listing.Package.ToString(), listing.Title, listing.Description })];

    [McpServerTool(Name = "apps_spec"), Description("Read an app's scenarios with the verdict of every step from its last verification. package is owner/name.")]
    public async Task<AppSpecSummary> Spec(string package)
    {
        var spec = await marketplace.Spec(PackageId.Parse(package), null);
        return new(package, spec.Revision.Revision, spec.Runtime, spec.Verification?.Green, Scenarios(spec.Feature, spec.Verification?.Run));
    }

    [McpServerTool(Name = "apps_draft"), Description("Start creating a new app from a plain-language description. The Author agent writes its scenarios; read them in the result. Returns a draftId for apps_revise, apps_edit_spec and apps_build.")]
    public async Task<AppDraftSummary> Draft(string description)
    {
        var draftId = Guid.NewGuid().ToString("N");
        return Summary(draftId, await DraftNeuron(draftId).Draft(description));
    }

    [McpServerTool(Name = "apps_revise"), Description("Ask the Author agent to change a draft's scenarios, for example 'also handle an empty question'.")]
    public async Task<AppDraftSummary> Revise(string draftId, string instruction) => Summary(draftId, await DraftNeuron(draftId).Revise(instruction));

    [McpServerTool(Name = "apps_edit_spec"), Description("Replace a draft's scenarios with your own Gherkin text. Steps must use phrasings the brain understands; unbound steps are listed in the result.")]
    public async Task<AppDraftSummary> EditSpec(string draftId, string spec) => Summary(draftId, await DraftNeuron(draftId).EditSpec(spec));

    [McpServerTool(Name = "apps_build"), Description("Build a draft: the Builder agent implements it, the scenarios run against a scratch installation, failures go back to the Builder (up to 3 attempts), and a green app is published under your name. Takes minutes.")]
    public async Task<AppDraftSummary> Build(string draftId) => Summary(draftId, await DraftNeuron(draftId).Build());

    [McpServerTool(Name = "apps_install"), Description("Install a published app (owner/name) into this workspace with its default settings.")]
    public async Task<string> Install(string package)
    {
        var installed = await packages.Install(WorkspaceId, PackageId.Parse(package), new InstallPackageRequest());
        return $"{package} is installed at revision {installed.App.Revision?.Revision[..12]}.";
    }

    [McpServerTool(Name = "apps_ask"), Description("Ask an installed app a question and wait for its answer. For a group chat app the discussion's turns are included.")]
    public async Task<AppAnswer> Ask(string package, string question, CancellationToken ct)
    {
        var id = PackageId.Parse(package);
        var invocation = await packages.Invoke(WorkspaceId, id, new InvokePackageRequest("ask", question));
        var deadline = DateTimeOffset.UtcNow + AnswerTimeout;
        while (invocation.Status == InvocationStatus.Pending && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            invocation = await packages.ReadInvocation(WorkspaceId, id, invocation.Id);
        }
        var discussion = await marketplace.Discussion(packages.AppKey(WorkspaceId, id), invocation.Id);
        return new(invocation.Status.ToString(), invocation.Output, invocation.Error,
            [.. discussion.Turns.Select(turn => $"{turn.Speaker} (round {turn.Round}): {turn.Text}")]);
    }

    private IAppDraft DraftNeuron(string draftId)
    {
        if (!Guid.TryParse(draftId, out var id)) { throw new ArgumentException("A draftId is the id apps_draft returned."); }
        return brain.Get<IAppDraft>($"{CallerContextStamper.Require().PrincipalId}/drafts/{id:N}");
    }

    private static AppDraftSummary Summary(string draftId, AppDraftView view)
    {
        var draft = view.Draft;
        var unbound = view.Feature is { } feature
            ? feature.Background.Concat(feature.Scenarios.SelectMany(scenario => scenario.Steps)).Where(step => !step.Bound).Select(step => $"line {step.Line}: {step.Keyword} {step.Text}").ToArray()
            : [];
        if (view.Feature?.Problem is { } problem) { unbound = [$"line {problem.Line}: {problem.Message}", .. unbound]; }
        return new(draftId, draft.Status.ToString(), draft.Name, draft.Title, draft.Runtime, draft.Spec, unbound, draft.Attempts,
            draft.Published is { } published ? published.Package.ToString() : null, draft.Error);
    }

    private static IReadOnlyList<AppScenarioView> Scenarios(FeatureSnapshot? feature, FeatureRun? run)
    {
        if (feature is null) { return []; }
        return [.. feature.Scenarios.Select(scenario =>
        {
            var result = run?.Scenarios.FirstOrDefault(item => item.Line == scenario.Line);
            return new AppScenarioView(scenario.Name, result?.Verdict.ToString() ?? "NotRun", [.. scenario.Steps.Select(step =>
            {
                var stepResult = result?.Steps.FirstOrDefault(item => item.Line == step.Line);
                return new AppStepView(step.Line, $"{step.Keyword} {step.Text}", stepResult?.Verdict.ToString() ?? (step.Bound ? "NotRun" : "Unbound"), stepResult?.Message);
            })]);
        })];
    }
}
