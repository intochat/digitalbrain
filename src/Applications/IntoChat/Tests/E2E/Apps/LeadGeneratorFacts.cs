using DigitalBrain.Apps;
using DigitalBrain.Automations;
using DigitalBrain.Discovery;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Inbox;
using DigitalBrain.Receipts;
using IntoChat.Apps;
using IntoChat.Workspace;
using Xunit;

namespace IntoChat.Tests.E2E.Apps;

// J3: "Find new dental clinics in Berlin" proposes LeadGenerator through discovery, the consent
// sheet is shown and approved, a Leads window opens with company-level fields only, the daily
// automation lands in Inbox, and the receipt is shadow-priced rather than charged.
public sealed class LeadGeneratorFacts
{
    private const string AppId = "intochat.leadgenerator";

    [Fact(Timeout = 300_000)]
    public async Task LeadGeneratorIsProposedConsentedInstalledAndRunsWithAShadowReceipt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.StartAsync(ct);
        var scope = WorkspaceScope.Create("owner", "leadgenerator").Id;

        var catalog = brain.Get<ICapabilityCatalog>("catalog");
        var discovered = await catalog.Search("Find new dental clinics in Berlin", scope, 5);
        Assert.Contains(discovered.Hits, hit => string.Equals(hit.Id, AppId, StringComparison.Ordinal));

        var consent = brain.Get<IAppConsent>(scope);
        var sheet = await consent.Review(AppId);
        Assert.Equal(AppId, sheet.AppId);
        Assert.Contains(sheet.Examples, example => example.Contains("dental clinics in Berlin", StringComparison.Ordinal));
        Assert.NotEmpty(sheet.DataTypes);
        Assert.Contains(sheet.Meters, meter => meter.MeterId == "search.request");
        Assert.True(sheet.EstimatedCompute > 0m, "The consent sheet must show a shadow Compute estimate.");
        Assert.False(sheet.Approved);

        var approved = await consent.Approve(AppId);
        Assert.True(approved.Approved);
        Assert.True(await consent.IsApproved(AppId));
        Assert.NotNull(await brain.Get<IAppCatalog>(scope).Read(AppId));

        var leads = await brain.Get<ILeadGeneratorLeads>(scope).Sweep("Find new dental clinics in Berlin");
        Assert.NotEmpty(leads.Leads);
        Assert.All(leads.Leads, lead =>
        {
            Assert.False(string.IsNullOrWhiteSpace(lead.Company));
            Assert.False(string.IsNullOrWhiteSpace(lead.Address));
            Assert.False(string.IsNullOrWhiteSpace(lead.Website));
            Assert.False(string.IsNullOrWhiteSpace(lead.Phone));
            Assert.False(string.IsNullOrWhiteSpace(lead.Category));
        });
        Assert.DoesNotContain(typeof(LeadRecord).GetProperties(), property =>
            property.Name.Contains("linkedin", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("email", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("contact", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("person", StringComparison.OrdinalIgnoreCase));

        var workspace = brain.Get<IWorkspace>(scope);
        var window = (await workspace.Read()).Windows.Single(candidate => candidate.Id == "leadgen-leads");
        Assert.True(window.IsOpen);
        Assert.Equal("Leads", window.Title);

        var operation = await new FirstPartyAutomationActionCatalog().FindAsync(AppId, "Find", ct);
        Assert.NotNull(operation);
        Assert.Equal("search.request", operation!.MeterId);

        var automation = brain.Get<IAutomation>("leadgen-daily");
        var saved = await automation.Save(new AutomationDefinition
        {
            Id = "leadgen-daily",
            WorkspaceId = scope,
            Name = "Daily lead pull",
            Trigger = new AutomationTrigger { Kind = AutomationTriggerKind.Schedule, IntervalSeconds = 86_400 },
            Action = new AutomationAction { AppId = AppId, Operation = "Find", EstimatedCompute = 1m },
            ComputeBudget = 100m,
        });
        Assert.True(saved.Valid);
        var run = await automation.RunNow(DateTimeOffset.UtcNow);
        Assert.Equal(JobOutcome.Succeeded, run.Outcome);

        var inbox = await brain.Get<IInboxFeed>(scope).Read();
        Assert.Contains(inbox.Items, item =>
            item.AppId == AppId && item.Kind == InboxItemKind.AutomationResult);

        var receipt = await brain.Get<IReceipt>(run.IntentId).Read();
        Assert.NotNull(receipt);
        Assert.Equal(ReceiptOutcome.Succeeded, receipt!.Content.Outcome);
        Assert.True(receipt.Content.ShadowPriced);
        Assert.Equal(0m, receipt.Content.ActualCompute);
    }
}
