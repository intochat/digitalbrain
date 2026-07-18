using DigitalBrain.Features.EnrichSalesforce;
using DigitalBrain.Features.Sdk;
using DigitalBrain.Features.Testing;
using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Integrations.Salesforce.Contracts;
using DigitalBrain.Integrations.Web.Contracts;
using Reqnroll;
using Xunit;
using EnrichSalesforceImplementation = DigitalBrain.Features.EnrichSalesforce.EnrichSalesforceFeature;
namespace DigitalBrain.Features.EnrichSalesforce.Tests;

[Binding]
public sealed class EnrichSalesforceSteps(
    FeatureScenarioContext scenario,
    GeneratedFeatureScenario generatedScenario)
{
    private readonly GmailReader _gmail = new();
    private readonly WebReader _web = new();
    private readonly AccountSearcher _accounts = new();
    private readonly UpdateProposer _updates = new();

    [Given("the Gmail message is from {string} about {string}")]
    public void GivenTheGmailMessageIsFromAbout(string sender, string subject) =>
        _gmail.Message = new GmailMessage(
            "synthetic-demo-priya-northstar",
            "synthetic-thread-priya-northstar",
            DateTimeOffset.UnixEpoch,
            sender,
            subject,
            "Priya asks about the Northstar Robotics pilot rollout and next steps.");

    [Given("Salesforce has one account named {string}")]
    public void GivenSalesforceHasOneAccountNamed(string name) =>
        _accounts.Accounts =
        [
            new SalesforceAccountSummary(new SalesforceRecordReference("Account", "001000000000001AAA"), name)
        ];

    [Given("Salesforce has no matching account")]
    public void GivenSalesforceHasNoMatchingAccount() => _accounts.Accounts = [];

    [Given("Salesforce has two accounts named {string}")]
    public void GivenSalesforceHasTwoAccountsNamed(string name) =>
        _accounts.Accounts =
        [
            new SalesforceAccountSummary(new SalesforceRecordReference("Account", "001000000000001AAA"), name),
            new SalesforceAccountSummary(new SalesforceRecordReference("Account", "001000000000002AAA"), name)
        ];

    [Given("web search returns evidence {string}")]
    public void GivenWebSearchReturnsEvidence(string evidence) =>
        _web.Response = new WebSearchResponse(
        [
            new WebSearchResult("Northstar Robotics", "https://northstarrobotics.example/about", evidence)
        ]);

    [When("the Gmail received input is handled")]
    public Task WhenTheGmailReceivedInputIsHandled() =>
        scenario.ExecuteAsync(
            new EnrichSalesforceImplementation(_gmail, _web, _accounts, _updates),
            new FeatureInput(
                "synthetic-input-priya-northstar",
                "gmail.message.received.v1",
                DateTimeOffset.UnixEpoch,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["messageId"] = "synthetic-demo-priya-northstar",
                    ["threadId"] = "synthetic-thread-priya-northstar"
                }));

    [Then("exactly one Salesforce Description update is proposed for {string}")]
    public void ThenExactlyOneSalesforceDescriptionUpdateIsProposedFor(string name)
    {
        var proposal = Assert.Single(_updates.Proposals);
        Assert.Equal("Description", proposal.Field);
        Assert.Equal(name, Assert.Single(_accounts.Accounts).Name);
    }

    [Then("the proposal contains {string}")]
    public void ThenTheProposalContains(string evidence) =>
        Assert.Contains(evidence, Assert.Single(_updates.Proposals).NewValue.GetString(), StringComparison.Ordinal);

    [Then("no Salesforce update is proposed")]
    public void ThenNoSalesforceUpdateIsProposed() => Assert.Empty(_updates.Proposals);

    [BeforeScenario("generated-duplicate")]
    public void ConfigureGeneratedDuplicateScenario()
    {
        scenario.Reset();
        _gmail.Message = new GmailMessage(
            "synthetic-demo-priya-northstar",
            "synthetic-thread-priya-northstar",
            DateTimeOffset.UnixEpoch,
            "priya@northstarrobotics.example",
            "Pilot rollout",
            "Priya asks about the Northstar Robotics pilot rollout and next steps.");
        _accounts.Accounts =
        [
            new SalesforceAccountSummary(new SalesforceRecordReference("Account", "001000000000001AAA"), "Northstar Robotics")
        ];
        _web.Response = new WebSearchResponse(
        [
            new WebSearchResult(
                "Northstar Robotics",
                "https://northstarrobotics.example/about",
                "Northstar Robotics builds warehouse automation systems.")
        ]);
        generatedScenario.Configure(
            new EnrichSalesforceImplementation(_gmail, _web, _accounts, _updates),
            new FeatureInput(
                "synthetic-input-priya-northstar",
                "gmail.message.received.v1",
                DateTimeOffset.UnixEpoch,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["messageId"] = "synthetic-demo-priya-northstar",
                    ["threadId"] = "synthetic-thread-priya-northstar"
                }));
    }

    private sealed class GmailReader : IGmailMessageReader
    {
        public GmailMessage? Message { get; set; }
        public Task<GmailMessage> ReadAsync(GmailMessageReadRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Message ?? throw new InvalidOperationException("No Gmail message configured."));
    }

    private sealed class WebReader : IWebSearchReader
    {
        public WebSearchResponse? Response { get; set; }
        public Task<WebSearchResponse> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Response ?? throw new InvalidOperationException("No web evidence configured."));
    }

    private sealed class AccountSearcher : ISalesforceAccountSearcher
    {
        public IReadOnlyList<SalesforceAccountSummary> Accounts { get; set; } = [];
        public Task<SalesforceAccountSearchResponse> SearchAsync(SalesforceAccountSearchRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SalesforceAccountSearchResponse(Accounts));
    }

    private sealed class UpdateProposer : ISalesforceUpdateProposer
    {
        public List<SalesforceUpdateProposal> Proposals { get; } = [];
        public Task<SalesforceUpdateProposal> ProposeAsync(SalesforceUpdateProposalRequest request, CancellationToken cancellationToken = default)
        {
            var proposal = new SalesforceUpdateProposal(request.Record, request.Field, request.NewValue, request.LogicalOperationKey);
            Proposals.Add(proposal);
            return Task.FromResult(proposal);
        }
    }
}
