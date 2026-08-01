import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:test/test.dart';

void main() {
  test('listBehaviors GETs /behaviors', () async {
    http.Request? seen;
    final client = BehaviorClient(
      baseUri: Uri.parse('http://ui.example:5080'),
      httpClient: MockClient((request) async {
        seen = request;
        return http.Response(
          jsonEncode({
            'items': [
              {
                'behaviorId': 'b1',
                'displayName': 'One',
                'description': 'd',
                'status': 'Empty',
                'runState': 'Idle',
                'activationGateOpen': false,
                'overview': 'o',
                'scenarioTitles': ['s'],
                'health': 'draft',
              },
            ],
          }),
          200,
          headers: {'content-type': 'application/json'},
        );
      }),
    );

    final library = await client.listBehaviors();
    expect(seen!.method, 'GET');
    expect(seen!.url.path, '/behaviors');
    expect(library.items.single.behaviorId, 'b1');
  });

  test('stop/start/runOnce hit lifecycle paths', () async {
    final paths = <String>[];
    final client = BehaviorClient(
      baseUri: Uri.parse('http://ui.example:5080'),
      httpClient: MockClient((request) async {
        paths.add('${request.method} ${request.url.path}');
        if (request.url.path.endsWith('/run-once')) {
          return http.Response(
            jsonEncode({
              'succeeded': true,
              'outcome': 'ok',
              'document': _documentJson(runState: 'Running'),
            }),
            200,
            headers: {'content-type': 'application/json'},
          );
        }
        return http.Response(
          jsonEncode(
            _documentJson(
              runState: request.url.path.endsWith('/stop') ? 'Stopped' : 'Running',
              gate: !request.url.path.endsWith('/stop'),
            ),
          ),
          200,
          headers: {'content-type': 'application/json'},
        );
      }),
    );

    final stopped = await client.stop('com.x');
    expect(stopped.isStopped, isTrue);
    final started = await client.start('com.x');
    expect(started.isRunning, isTrue);
    final once = await client.runOnce(
      behaviorId: 'com.x',
      triggerTypeName: 'T',
      triggerJson: '{}',
    );
    expect(once.succeeded, isTrue);
    expect(paths, [
      'POST /behaviors/com.x/stop',
      'POST /behaviors/com.x/start',
      'POST /behaviors/com.x/run-once',
    ]);
  });

  test('scenario-first change propose then approve', () async {
    final client = BehaviorClient(
      baseUri: Uri.parse('http://ui.example:5080'),
      httpClient: MockClient((request) async {
        if (request.url.path.endsWith('/change/propose')) {
          return http.Response(
            jsonEncode({
              'proposalId': 'prop-1',
              'behaviorId': 'com.x',
              'requestText': 'add phone',
              'proposedFeatureText': 'Feature:\n  Scenario: add phone\n',
              'proposedFeatureName': 'install',
              'status': 'awaiting-scenario-approval',
              'diffSummary': 'Add scenario',
            }),
            200,
            headers: {'content-type': 'application/json'},
          );
        }
        return http.Response(
          jsonEncode(_documentJson(status: 'Proposed')),
          200,
          headers: {'content-type': 'application/json'},
        );
      }),
    );

    final proposal = await client.proposeChange(
      behaviorId: 'com.x',
      requestText: 'add phone',
    );
    expect(proposal.awaitsScenarioApproval, isTrue);

    final approved = await client.approveScenarioChange(
      behaviorId: 'com.x',
      proposalId: proposal.proposalId,
      approved: true,
    );
    expect(approved, isA<BehaviorDocument>());
  });

  test('watchEvents parses behavior SSE and cancelInflight aborts', () async {
    const body = '''
: connected

id: 1
event: behavior
data: {"sequence":1,"kind":"BehaviorStopped","behaviorId":"com.x","commandId":"c1","artifactHash":null,"detail":null,"timestamp":"2026-07-30T12:00:00Z"}

''';
    final client = BehaviorClient(
      baseUri: Uri.parse('http://ui.example:5080'),
      httpClient: MockClient((request) async {
        expect(request.url.path, '/behaviors/com.x/events');
        return http.Response(
          body,
          200,
          headers: {'content-type': 'text/event-stream'},
        );
      }),
    );

    final events = await client.watchEvents(behaviorId: 'com.x').toList();
    expect(events.single.kind, 'BehaviorStopped');
    await client.cancelInflight();
    client.close();
  });
}

Map<String, Object?> _documentJson({
  String status = 'Active',
  String runState = 'Running',
  bool gate = true,
}) => {
  'behaviorId': 'com.x',
  'status': status,
  'runState': runState,
  'activationGateOpen': gate,
  'proposedArtifactHash': null,
  'activeArtifactHash': 'a1',
  'priorArtifactHash': null,
  'lastCompileFailure': null,
  'testsPassed': true,
  'isApproved': true,
  'lastExecutionOutcome': null,
  'programSource': '',
  'featureName': 'install',
  'featureText': '',
  'displayName': 'X',
  'description': 'X',
  'overview': 'X',
  'activeSignatureHex': null,
  'activeTaskCount': 0,
  'scenarios': <Object?>[],
  'bindings': <Object?>[],
  'revisions': <Object?>[],
};
