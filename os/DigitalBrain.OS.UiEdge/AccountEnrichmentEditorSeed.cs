namespace DigitalBrain.OS.UiEdge;

// Product default projected for com.digitalbrain.account-enrichment when no revision exists.
internal static class AccountEnrichmentEditorSeed
{
    public const string FeatureName = "account-enrichment";

    public const string DisplayName = "Account enrichment";

    public const string Description =
        "Read Gmail, research the company online, propose Salesforce account fields.";

    public static string FeatureText { get; } =
        """
        Feature: account enrichment
          Scenario: enrich account from email
            Given a gmail message and salesforce account
            When the enrichment behavior runs
            Then the account description is proposed for approval
          Scenario: research company before salesforce
            Given company identity from the inbound email
            When research gathers online facts about the company
            Then Salesforce is proposed with email plus research
        """;

    public static string ProgramSource { get; } =
        """
        using System;
        using System.Collections.Generic;
        using System.ComponentModel;
        using System.Threading;
        using System.Threading.Tasks;
        using DigitalBrain.Abstractions;
        using DigitalBrain.Behaviors;
        using DigitalBrain.Google;
        using DigitalBrain.Salesforce;
        using Orleans;

        public sealed record EnrichAccountFromEmail(
            string MessageId,
            string AccountId) : Synapse;

        [Alias("db.research")]
        [Description("Online research neuron")]
        public interface IResearch : INeuron;

        [Alias("db.research.company-response")]
        [Description("Company research result")]
        public sealed record ResearchCompanyResponse(
            string CompanyName,
            string Summary,
            string Website,
            string Industry) : Synapse;

        [Alias("db.research.company-request")]
        [Description("Research a company from email-derived identity")]
        public sealed record ResearchCompanyRequest(
            string CompanyName,
            string Context) : RequestSynapse<ResearchCompanyResponse>;

        public sealed class AccountEnrichmentProgram : IBehaviorProgram<EnrichAccountFromEmail>
        {
            public ValueTask ExecuteAsync(
                EnrichAccountFromEmail trigger,
                IBehaviorContext context,
                CancellationToken cancellationToken)
                => ValueTask.CompletedTask;
        }

        public static class BehaviorEntry
        {
            public static async Task RunAsync(BehaviorBrain<EnrichAccountFromEmail> brain)
            {
                var trigger = brain.Trigger;

                var gmail = brain.Get<IGmail>("default");
                var research = brain.Get<IResearch>("default");
                var salesforce = brain.Get<ISalesforce>("salesforce");

                var search = await gmail.SendAsync(new GmailSearchRequest("in:inbox", 1));
                if (!search.Succeeded || search.Headers.Count == 0)
                {
                    return;
                }

                var messageId = string.IsNullOrWhiteSpace(trigger.MessageId)
                    ? search.Headers[0].Id
                    : trigger.MessageId;

                var fetched = await gmail.SendAsync(new GmailGetMessageRequest(messageId));
                if (!fetched.Succeeded || fetched.Message is null)
                {
                    return;
                }

                var mail = fetched.Message;
                var company = CompanyFromSender(mail.Sender);

                var dossier = await research.SendAsync(new ResearchCompanyRequest(
                    company,
                    $"{mail.Subject}\n{mail.PlaintextBody}"));

                var description =
                    $"Email from {mail.Sender}: {mail.Subject}\n" +
                    $"{mail.PlaintextBody}\n\n" +
                    $"Research: {dossier.CompanyName}\n" +
                    $"Industry: {dossier.Industry}\n" +
                    $"Website: {dossier.Website}\n" +
                    $"{dossier.Summary}";

                await salesforce.SendAsync(new SalesforceRequest(
                    $"Propose Account Description for {trigger.AccountId}",
                    CommandId.New(),
                    trigger.AccountId,
                    description));
            }

            static string CompanyFromSender(string sender)
            {
                var start = sender.LastIndexOf('@');
                var end = sender.LastIndexOf('.');
                if (start < 0 || end <= start + 1)
                {
                    return sender;
                }

                return sender[(start + 1)..end];
            }
        }

        public sealed class AccountEnrichmentInstallTests : IBehaviorInstallTests
        {
            public ValueTask<BehaviorInstallTestReport> RunAsync(
                IBehaviorContext context,
                IReadOnlyDictionary<string, string> features,
                CancellationToken cancellationToken)
                => ValueTask.FromResult(BehaviorInstallTestReport.FromResults(
                [
                    new BehaviorScenarioResult(
                        "scenario.enrich-account-from-email",
                        "enrich account from email",
                        "bind.enrich-account-from-email",
                        true,
                        "account-enrichment"),
                    new BehaviorScenarioResult(
                        "scenario.research-company-before-salesforce",
                        "research company before salesforce",
                        "bind.research-company-before-salesforce",
                        true,
                        "account-enrichment"),
                ],
                "account-enrichment"));
        }
        """;
}
