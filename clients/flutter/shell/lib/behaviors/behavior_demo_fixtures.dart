import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';

/// Offline / empty-edge demo content for Behavior Studio.
/// Not a live grain — [BehaviorStudioController] serves these when the edge
/// has nothing to show. Seed real grains with
/// `dart run bin/seed_demo_behaviors.dart` against UiEdge.
abstract final class BehaviorDemoFixtures {
  static const accountEnrichmentId = 'com.digitalbrain.account-enrichment';
  static const inboxBriefId = 'com.digitalbrain.inbox-brief';

  static const library = <BehaviorLibraryItem>[
    BehaviorLibraryItem(
      behaviorId: accountEnrichmentId,
      displayName: 'Account enrichment',
      description:
          'Read Gmail, research the company online, propose Salesforce account fields.',
      status: 'Active',
      runState: 'Running',
      activationGateOpen: true,
      activeArtifactHash: 'demo-active-account-enrichment',
      overview:
          'Gmail → IResearch (company online) → Salesforce Account Description proposal.',
      scenarioTitles: [
        'enrich account from email',
        'research company before salesforce',
      ],
      health: 'healthy',
    ),
    BehaviorLibraryItem(
      behaviorId: inboxBriefId,
      displayName: 'Inbox brief',
      description: 'Summarize the latest Gmail message into chat.',
      status: 'Active',
      runState: 'Stopped',
      activationGateOpen: false,
      activeArtifactHash: 'demo-active-inbox-brief',
      overview: 'Demo flow: read one inbox message and emit a short brief fact.',
      scenarioTitles: ['brief latest inbox message'],
      health: 'stopped',
    ),
  ];

  static bool isDemoId(String behaviorId) =>
      behaviorId == accountEnrichmentId || behaviorId == inboxBriefId;

  static BehaviorDocument? documentFor(String behaviorId) => switch (behaviorId) {
        accountEnrichmentId => accountEnrichment,
        inboxBriefId => inboxBrief,
        _ => null,
      };

  static final accountEnrichment = BehaviorDocument(
    behaviorId: accountEnrichmentId,
    status: 'Active',
    runState: 'Running',
    activationGateOpen: true,
    proposedArtifactHash: null,
    activeArtifactHash: 'demo-active-account-enrichment',
    priorArtifactHash: null,
    lastCompileFailure: null,
    testsPassed: true,
    isApproved: true,
    lastExecutionOutcome: 'demo: proposed Salesforce description from last Gmail',
    programSource: accountEnrichmentProgramSource,
    featureName: 'account-enrichment',
    featureText: accountEnrichmentFeatureText,
    displayName: 'Account enrichment',
    description:
        'Read Gmail, research the company online, propose Salesforce account fields.',
    overview:
        'Gmail → IResearch (company online) → Salesforce Account Description proposal.',
    activeSignatureHex: 'DEMOAE01',
    activeTaskCount: 0,
    scenarios: const [
      BehaviorScenario(
        scenarioId: 'scenario.enrich-account-from-last-email',
        title: 'enrich account from last email',
        bindingKey: 'bind.enrich-account-from-last-email',
        passed: true,
        detail: 'demo fixture',
      ),
      BehaviorScenario(
        scenarioId: 'scenario.propose-salesforce-description',
        title: 'propose salesforce description',
        bindingKey: 'bind.propose-salesforce-description',
        passed: true,
        detail: 'demo fixture',
      ),
    ],
    bindings: const [
      BehaviorBinding(
        bindingId: 'bind.enrich-account-from-last-email',
        sourceModule: 'DigitalBrain.Google',
        sourceSynapse: 'GmailSearchRequest',
        targetCase: 'EnrichFromLatestEmail',
        contractVersion: 'v1-demo',
        enabled: true,
        configurationHint: 'Gmail account name (default)',
      ),
      BehaviorBinding(
        bindingId: 'bind.propose-salesforce-description',
        sourceModule: 'DigitalBrain.Salesforce',
        sourceSynapse: 'SalesforceRequest',
        targetCase: 'EnrichFromLatestEmail',
        contractVersion: 'v1-demo',
        enabled: true,
        configurationHint: 'Salesforce account id',
      ),
    ],
    revisions: const [
      BehaviorRevision(
        role: 'active',
        artifactHash: 'demo-active-account-enrichment',
        signatureHex: 'DEMOAE01',
        status: 'Active',
        isActive: true,
      ),
    ],
  );

  static final inboxBrief = BehaviorDocument(
    behaviorId: inboxBriefId,
    status: 'Active',
    runState: 'Stopped',
    activationGateOpen: false,
    proposedArtifactHash: null,
    activeArtifactHash: 'demo-active-inbox-brief',
    priorArtifactHash: null,
    lastCompileFailure: null,
    testsPassed: true,
    isApproved: true,
    lastExecutionOutcome: null,
    programSource: inboxBriefProgramSource,
    featureName: 'inbox-brief',
    featureText: inboxBriefFeatureText,
    displayName: 'Inbox brief',
    description: 'Summarize the latest Gmail message into chat.',
    overview: 'Demo flow: read one inbox message and emit a short brief fact.',
    activeSignatureHex: 'DEMOIB01',
    activeTaskCount: 0,
    scenarios: const [
      BehaviorScenario(
        scenarioId: 'scenario.brief-latest-inbox',
        title: 'brief latest inbox message',
        bindingKey: 'bind.brief-latest-inbox',
        passed: true,
        detail: 'demo fixture',
      ),
    ],
    bindings: const [
      BehaviorBinding(
        bindingId: 'bind.brief-latest-inbox',
        sourceModule: 'DigitalBrain.Google',
        sourceSynapse: 'GmailSearchRequest',
        targetCase: 'BriefLatestInbox',
        contractVersion: 'v1-demo',
        enabled: false,
        configurationHint: 'Gmail account name (default)',
      ),
    ],
    revisions: const [
      BehaviorRevision(
        role: 'active',
        artifactHash: 'demo-active-inbox-brief',
        signatureHex: 'DEMOIB01',
        status: 'Active',
        isActive: true,
      ),
    ],
  );

  static const accountEnrichmentProgramSource = r'''
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.ComponentModel;
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
'''

  static const accountEnrichmentFeatureText = '''
Feature: account enrichment
  Scenario: enrich account from last email
    Given a Gmail connection and a Salesforce account id
    When the behavior searches in:inbox for the latest message
    Then it drafts an account description from that email
  Scenario: propose salesforce description
    Given a drafted description
    When the behavior proposes a Salesforce Account Description mutation
    Then a human approval card is shown before write
''';

  static const inboxBriefProgramSource = r'''
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;

// Demo: brief the latest inbox message. Live path would call Gmail search.

public sealed record BriefLatestInbox(string GmailAccount) : Synapse;

public sealed class InboxBriefProgram : IBehaviorProgram<BriefLatestInbox>
{
    public ValueTask ExecuteAsync(
        BriefLatestInbox trigger,
        IBehaviorContext context,
        CancellationToken cancellationToken)
    {
        context.SetState("outcome", $"demo-brief:{trigger.GmailAccount}:last-inbox");
        return ValueTask.CompletedTask;
    }
}

public sealed class InboxBriefInstallTests : IBehaviorInstallTests
{
    public ValueTask<BehaviorInstallTestReport> RunAsync(
        IBehaviorContext context,
        IReadOnlyDictionary<string, string> features,
        CancellationToken cancellationToken)
        => ValueTask.FromResult(BehaviorInstallTestReport.FromResults(
        [
            new BehaviorScenarioResult(
                "scenario.brief-latest-inbox",
                "brief latest inbox message",
                "bind.brief-latest-inbox",
                true,
                "inbox-brief"),
        ],
        "inbox-brief"));
}
''';

  static const inboxBriefFeatureText = '''
Feature: inbox brief
  Scenario: brief latest inbox message
    Given a Gmail connection
    When the behavior reads the latest inbox message
    Then it records a short brief outcome
''';
}
