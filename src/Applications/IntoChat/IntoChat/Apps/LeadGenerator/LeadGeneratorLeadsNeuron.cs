using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;

namespace IntoChat.Apps;

[GenerateSerializer, Alias("intochat.leadgenerator-state")]
internal sealed record LeadGeneratorState
{
    [Id(0)] public List<LeadRecord> Leads { get; init; } = [];
    [Id(1)] public string Query { get; init; } = "";
    [Id(2)] public int MeteredRequests { get; init; }
}

// Keyed by the workspace scope id. Web search and company lookup both run through IWebResearch, so
// each request is metered as search.request against the app caller. The leads are the app's own data,
// and a sweep opens the Leads window on the app's own surface in this workspace.
[GrainType("intochat.leadgenerator-leads")]
internal sealed class LeadGeneratorLeadsNeuron(
    [PersistentState("leads", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<LeadGeneratorState> store)
    : Neuron<LeadGeneratorState>(store), ILeadGeneratorLeads
{
    internal const string WindowId = "leadgen-leads";

    private IWebResearch Web => ServiceProvider.GetRequiredService<IWebResearch>();
    private AppSurfaceComposer Surfaces => ServiceProvider.GetRequiredService<AppSurfaceComposer>();

    private string WorkspaceId => this.GetPrimaryKeyString();

    public async Task<LeadSweepResult> Sweep(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) { throw new ArgumentException("A lead query is required.", nameof(query)); }
        var caller = new CallerContext
        {
            PrincipalId = "assistant",
            AccountId = "default",
            WorkspaceId = WorkspaceId,
            Kind = CallerKind.Platform,
            StampedBy = TrustedEdge.Platform,
            AppId = "intochat.leadgenerator",
            IntentId = "leadgenerator:" + Guid.NewGuid().ToString("n"),
        };

        var search = await Web.SearchAsync(new WebResearchQuery(query), caller).ConfigureAwait(true);
        var leads = new List<LeadRecord>();
        var requests = 1;
        foreach (var hit in search.Hits)
        {
            var lookup = await Web.LookupCompanyAsync(new CompanyLookup(hit.Title), caller).ConfigureAwait(true);
            requests++;
            var company = lookup.Hits.Count > 0 ? lookup.Hits[0] : hit;
            leads.Add(new LeadRecord(CompanyName(hit.Title), "Company address on file", company.Url, "Company phone on file", Category(query)));
        }

        var next = new LeadGeneratorState { Leads = leads, Query = query, MeteredRequests = requests };
        await Save(next, new LeadGeneratorSwept(WorkspaceId, query, leads.Count));
        await OpenWindowAsync(leads).ConfigureAwait(true);
        return new LeadSweepResult(leads, query, requests, WindowId);
    }

    public Task<LeadSweepResult> Read()
    {
        var state = Snapshot;
        return Task.FromResult(new LeadSweepResult([.. state.Leads], state.Query, state.MeteredRequests, WindowId));
    }

    private async Task OpenWindowAsync(IReadOnlyList<LeadRecord> leads)
    {
        var surface = await Surfaces.Leads(WorkspaceId + "/apps/leadgenerator", leads).ConfigureAwait(true);
        var workspace = GrainFactory.GetGrain<IWorkspace>(WorkspaceId);
        var reference = WindowReference.For(surface);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var state = await workspace.Read().ConfigureAwait(true);
            if (state.Windows.Any(window => window.Id == WindowId && window.IsOpen && window.Reference == reference)) { return; }
            try
            {
                await workspace.OpenSurface(new(Guid.NewGuid().ToString(), WindowId, "Leads", reference, state.Revision)).ConfigureAwait(true);
                return;
            }
            catch (WorkspaceRevisionConflictException) when (attempt < 2)
            {
                // Retry on a fresh revision.
            }
        }
    }

    private static string CompanyName(string title)
    {
        var separator = title.IndexOf(": ", StringComparison.Ordinal);
        return separator >= 0 ? title[(separator + 2)..] : title;
    }

    private static string Category(string query)
    {
        if (query.Contains("dental", StringComparison.OrdinalIgnoreCase)) { return "dental"; }
        if (query.Contains("clinic", StringComparison.OrdinalIgnoreCase)) { return "clinic"; }
        return "company";
    }
}
