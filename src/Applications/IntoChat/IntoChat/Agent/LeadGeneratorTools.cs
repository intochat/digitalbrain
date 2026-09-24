using System.ComponentModel;
using DigitalBrain.AI.Agents;
using DigitalBrain.Apps;
using DigitalBrain.Apps.Manifests;
using DigitalBrain.Contracts;
using IntoChat.Apps;
using Microsoft.Extensions.AI;

namespace IntoChat.Agent;

// The LeadGenerator app tools: the assistant proposes the app with its consent sheet, then, on the
// owner's approval, approves the consent, runs the sweep and opens the Leads window.
internal sealed class LeadGeneratorTools(IDigitalBrain brain) : IAgentToolFactory
{
    internal const string LeadGeneratorAppId = "intochat.leadgenerator";

    public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context)
    {
        async Task<object> ProposeApp(
            [Description("The capability id to propose, from find_capability.")] string appId,
            CancellationToken ct)
        {
            var trusted = context();
            try { return await ProposeAsync(trusted.ScopeId, appId, ct); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException)
            { return Failure(error); }
        }

        async Task<object> RunLeadGenerator(
            [Description("What companies to find, in the owner's words.")] string query,
            [Description("True only when the owner approved the consent sheet; false otherwise.")] bool approved,
            CancellationToken ct)
        {
            var trusted = context();
            try { return await RunAsync(trusted.ScopeId, query, approved, ct); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException)
            { return Failure(error); }
        }

        return
        [
            AIFunctionFactory.Create(ProposeApp, "propose_app", "Propose a capability with its consent sheet: examples, data types, meters and a shadow Compute estimate. The owner approves before any run."),
            AIFunctionFactory.Create(RunLeadGenerator, "run_leadgenerator", "After the owner approves, approve the LeadGenerator consent and run the company sweep. Returns the Leads window."),
        ];
    }

    private async Task<object> ProposeAsync(string scope, string appId, CancellationToken ct)
    {
        if (!string.Equals(appId, LeadGeneratorAppId, StringComparison.Ordinal))
        { throw new ArgumentException($"'{appId}' is not a proposable app.", nameof(appId)); }
        var sheet = await brain.Get<IAppConsent>(scope).Review(appId).WaitAsync(ct);
        return new
        {
            appId = sheet.AppId,
            name = sheet.Name,
            version = sheet.Version,
            estimatedCompute = sheet.EstimatedCompute,
            approved = sheet.Approved,
            _ui = ConsentCard(sheet),
        };
    }

    private async Task<object> RunAsync(string scope, string query, bool approved, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) { throw new ArgumentException("A lead query is required.", nameof(query)); }
        var consent = brain.Get<IAppConsent>(scope);
        if (!approved)
        {
            var pending = await consent.Review(LeadGeneratorAppId).WaitAsync(ct);
            return new { requiresApproval = true, appId = LeadGeneratorAppId, _ui = ConsentCard(pending) };
        }

        if (!await consent.IsApproved(LeadGeneratorAppId).WaitAsync(ct)) { await consent.Approve(LeadGeneratorAppId).WaitAsync(ct); }
        var result = await brain.Get<ILeadGeneratorLeads>(scope).Sweep(query).WaitAsync(ct);
        return new
        {
            windowId = result.WindowId,
            leads = result.Leads.Count,
            meteredRequests = result.MeteredRequests,
            _ui = new { kind = "leads", windowId = result.WindowId, leads = result.Leads.Count },
        };
    }

    private static object ConsentCard(AppConsentSheet sheet) => new
    {
        kind = "consent-sheet",
        appId = sheet.AppId,
        name = sheet.Name,
        description = sheet.DescriptionForPeople,
        examples = sheet.Examples,
        dataTypes = sheet.DataTypes.Select(type => new { id = type.SemanticTypeId, reason = type.Reason }).ToArray(),
        permissions = PermissionsOf(sheet.AppId),
        meters = sheet.Meters.Select(meter => new { meterId = meter.MeterId, unit = meter.Unit, price = meter.ProposedPriceInCompute }).ToArray(),
        estimate = sheet.EstimatedCompute,
        approved = sheet.Approved,
    };

    private static IReadOnlyList<object> PermissionsOf(string appId)
    {
        try
        {
            return [.. FirstPartyApps.Get(appId).Permissions.Select(permission => (object)new
            {
                id = permission.SemanticTypeId,
                reason = permission.Reason,
                write = permission.Write,
            })];
        }
        catch (KeyNotFoundException)
        {
            return [];
        }
    }

    private static object Failure(Exception error) => new { isError = true, message = error.Message };
}