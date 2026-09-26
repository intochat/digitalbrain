using DigitalBrain.Apps;
using DigitalBrain.Discovery;
using DigitalBrain.Flutter.Workspace;
using IntoChat.Apps;
using IntoChat.Workspace;
using Xunit;

namespace IntoChat.Tests.E2E.Apps;

// J3: "Find new dental clinics in Berlin" proposes LeadGenerator through discovery, the consent
// sheet is shown and approved, and a Leads window opens with company-level fields only.
public sealed class LeadGeneratorFacts
{
    private const string AppId = "intochat.leadgenerator";

    [Fact(Timeout = 300_000)]
    public async Task LeadGeneratorIsProposedConsentedInstalledAndOpensLeads()
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

    }
}
