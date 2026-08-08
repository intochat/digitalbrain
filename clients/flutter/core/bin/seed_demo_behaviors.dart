// DORMANT until a BehaviorHost HTTP surface ships again.
// Product shell uses Behavior Studio fixtures (behaviorClient: null).
// This script POSTs /behaviors/* when that API exists again.

import 'dart:convert';
import 'dart:io';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:http/http.dart' as http;

Future<void> main(List<String> args) async {
  final base = _arg(args, '--base') ??
      Platform.environment[DigitalBrainHostEnv.uiBaseVariable];
  if (base == null || base.isEmpty) {
    stderr.writeln(
      'Set ${DigitalBrainHostEnv.uiBaseVariable} or pass --base http://host:port',
    );
    exitCode = 64;
    return;
  }

  final activate = args.contains('--activate');
  final client = http.Client();
  final root = Uri.parse(base.endsWith('/') ? base : '$base/');

  stdout.writeln('seed_demo_behaviors base=$root activate=$activate');

  try {
    final before = await _list(client, root);
    stdout.writeln('behaviors before: ${before.length}');
    for (final id in before) {
      stdout.writeln('  - $id');
    }

    for (final seed in _seeds) {
      stdout.writeln('propose ${seed.id} …');
      final doc = await _propose(client, root, seed);
      stdout.writeln(
        '  status=${doc['status']} proposed=${doc['proposedArtifactHash']}',
      );

      if (!activate) {
        continue;
      }

      final hash = doc['proposedArtifactHash'] as String?;
      if (hash == null || hash.isEmpty) {
        stderr.writeln('  skip activate: no proposedArtifactHash');
        continue;
      }

      stdout.writeln('  run-tests $hash …');
      final tested = await _post(
        client,
        root,
        '/behaviors/${Uri.encodeComponent(seed.id)}/tests',
        {'artifactHash': hash},
      );
      stdout.writeln('  testsPassed=${tested['testsPassed']}');

      final approvalId = _approvalId();
      stdout.writeln('  approve …');
      await _post(
        client,
        root,
        '/behaviors/${Uri.encodeComponent(seed.id)}/approve',
        {'artifactHash': hash, 'approvalId': approvalId},
      );

      stdout.writeln('  activate …');
      final active = await _post(
        client,
        root,
        '/behaviors/${Uri.encodeComponent(seed.id)}/activate',
        {'artifactHash': hash},
      );
      stdout.writeln(
        '  active=${active['activeArtifactHash']} status=${active['status']}',
      );
    }

    final after = await _list(client, root);
    stdout.writeln('behaviors after: ${after.length}');
    for (final id in after) {
      stdout.writeln('  - $id');
    }
    stdout.writeln(
      activate
          ? 'done — open Behaviors in the shell; demo grains should list from the edge.'
          : 'done — proposed only. Re-run with --activate for tests/approve/activate.',
    );
  } on Object catch (error, stack) {
    stderr.writeln('seed failed: $error');
    stderr.writeln('$stack');
    exitCode = 1;
  } finally {
    client.close();
  }
}

Future<List<String>> _list(http.Client client, Uri root) async {
  final response = await client.get(root.replace(path: '/behaviors'));
  if (response.statusCode < 200 || response.statusCode >= 300) {
    throw StateError('GET /behaviors → ${response.statusCode} ${response.body}');
  }
  final json = jsonDecode(response.body) as Map<String, Object?>;
  final items = json['items'] as List<Object?>? ?? const [];
  return [
    for (final item in items)
      (Map<String, Object?>.from(item! as Map))['behaviorId'] as String,
  ];
}

Future<Map<String, Object?>> _propose(
  http.Client client,
  Uri root,
  _Seed seed,
) =>
    _post(client, root, '/behaviors/${Uri.encodeComponent(seed.id)}/propose', {
      'programSource': seed.programSource,
      'featureText': seed.featureText,
      'featureName': seed.featureName,
      'displayName': seed.displayName,
      'description': seed.description,
    });

Future<Map<String, Object?>> _post(
  http.Client client,
  Uri root,
  String path,
  Map<String, Object?> body,
) async {
  final response = await client.post(
    root.replace(path: path),
    headers: const {
      'content-type': 'application/json',
      'accept': 'application/json',
    },
    body: jsonEncode(body),
  );
  if (response.statusCode < 200 || response.statusCode >= 300) {
    throw StateError(
      'POST $path → ${response.statusCode} ${response.body}',
    );
  }
  return Map<String, Object?>.from(jsonDecode(response.body) as Map);
}

String? _arg(List<String> args, String name) {
  final index = args.indexOf(name);
  if (index < 0 || index + 1 >= args.length) {
    return null;
  }
  return args[index + 1];
}

String _approvalId() {
  final now =
      DateTime.now().toUtc().microsecondsSinceEpoch.toRadixString(16).padLeft(12, '0');
  return '00000000-0000-4000-8000-${now.substring(now.length - 12)}';
}

final class _Seed {
  const _Seed({
    required this.id,
    required this.displayName,
    required this.description,
    required this.featureName,
    required this.featureText,
    required this.programSource,
  });

  final String id;
  final String displayName;
  final String description;
  final String featureName;
  final String featureText;
  final String programSource;
}

// Keep sources compilable under BehaviorCompiler (IBehaviorProgram + install tests).
// Comments document the Gmail → Salesforce story for demos.
const _seeds = <_Seed>[
  _Seed(
    id: 'com.digitalbrain.account-enrichment',
    displayName: 'Account enrichment',
    description: 'Gmail → Salesforce: last inbound email enriches an account.',
    featureName: 'account-enrichment',
    featureText: r'''
Feature: account enrichment
  Scenario: enrich account from last email
    Given a Gmail connection and a Salesforce account id
    When the behavior searches in:inbox for the latest message
    Then it drafts an account description from that email
  Scenario: propose salesforce description
    Given a drafted description
    When the behavior proposes a Salesforce Account Description mutation
    Then a human approval card is shown before write
''',
    programSource: r'''
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;

// Intended live path: GmailSearchRequest("in:inbox", 1) → Salesforce propose.
// Stub body keeps the rail green without OAuth during demos.

public sealed record EnrichFromLatestEmail(string GmailAccount, string AccountId) : Synapse;

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
''',
  ),
  _Seed(
    id: 'com.digitalbrain.inbox-brief',
    displayName: 'Inbox brief',
    description: 'Summarize the latest Gmail message into a brief outcome.',
    featureName: 'inbox-brief',
    featureText: r'''
Feature: inbox brief
  Scenario: brief latest inbox message
    Given a Gmail connection
    When the behavior reads the latest inbox message
    Then it records a short brief outcome
''',
    programSource: r'''
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;

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
''',
  ),
];
