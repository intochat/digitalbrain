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
      description: 'Gmail → Salesforce: last inbound email enriches an account.',
      status: 'Active',
      runState: 'Running',
      activationGateOpen: true,
      activeArtifactHash: 'demo-active-account-enrichment',
      overview:
          'Demo flow: search latest inbox mail, draft Account.Description, propose for human approval.',
      scenarioTitles: [
        'enrich account from last email',
        'propose salesforce description',
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
    description: 'Gmail → Salesforce: last inbound email enriches an account.',
    overview:
        'Demo flow: search latest inbox mail, draft Account.Description, propose for human approval.',
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

  /// Compilable demo program (stub body). Comments describe the intended
  /// Gmail → Salesforce path for demos; live wiring needs capability grants.
  static const accountEnrichmentProgramSource = r'''
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;

// Intended demo path (when Gmail + Salesforce grants are armed):
//   1) GmailSearchRequest("in:inbox", maxResults: 1)
//   2) Build Account.Description from From / Subject / Body
//   3) SalesforceRequest propose description for AccountId
//   4) Human approves the Salesforce mutation card
// This single-file program is a rail-friendly stub so propose/compile/activate
// can run without live OAuth during demos.

public sealed record EnrichFromLatestEmail(
    string GmailAccount,
    string AccountId) : Synapse;

public sealed class AccountEnrichmentProgram : IBehaviorProgram<EnrichFromLatestEmail>
{
    public ValueTask ExecuteAsync(
        EnrichFromLatestEmail trigger,
        IBehaviorContext context,
        CancellationToken cancellationToken)
    {
        context.SetState(
            "outcome",
            $"demo-enrich:{trigger.GmailAccount}:{trigger.AccountId}:last-inbox");
        return ValueTask.CompletedTask;
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
                "scenario.enrich-account-from-last-email",
                "enrich account from last email",
                "bind.enrich-account-from-last-email",
                true,
                "account-enrichment"),
            new BehaviorScenarioResult(
                "scenario.propose-salesforce-description",
                "propose salesforce description",
                "bind.propose-salesforce-description",
                true,
                "account-enrichment"),
        ],
        "account-enrichment"));
}
''';

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
