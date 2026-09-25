import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/coding/coding_run_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

Map<String, dynamic> _snapshot({int status = 2, Map<String, dynamic>? plan, List<String> clarifications = const []}) => {
  'runId': 'run-1',
  'status': status,
  'failureReason': null,
  'clarifications': clarifications,
  'plan': plan,
};

Map<String, dynamic> _plan() => {
  'summary': 'Add an unread count',
  'steps': [
    {'number': 1, 'title': 'Count unread', 'files': ['Inbox.cs'], 'detail': 'Expose a count.'},
  ],
  'openQuestions': ['Which field?'],
};

void main() {
  testWidgets('coding run shows the plan, open questions and controls', (tester) async {
    final paths = <String>[];
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://test'),
      httpClient: MockClient((request) async {
        paths.add('${request.method} ${request.url.path}');
        return http.Response(jsonEncode(_snapshot(plan: _plan())), 200, headers: {'content-type': 'application/json'});
      }),
    );
    await tester.pumpWidget(
      MaterialApp(home: Scaffold(body: CodingRunScreen(runId: 'run-1', client: client, active: true))),
    );
    await tester.pump();

    expect(find.text('Plan drafted'), findsOneWidget);
    expect(find.text('Count unread'), findsOneWidget);
    expect(find.text('Which field?'), findsOneWidget);

    await tester.enterText(find.byKey(const Key('coding-run-clarify')), 'Include the dismissed count.');
    await tester.pump();
    await tester.tap(find.byKey(const Key('coding-run-send-clarify')));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));

    expect(paths, contains('GET /csharp-expert/runs/run-1'));
    expect(paths, contains('POST /csharp-expert/runs/run-1/clarify'));
    await tester.pumpWidget(const SizedBox());
    client.close();
  });

  testWidgets('approve stays disabled without a plan and posts once one exists', (tester) async {
    var body = _snapshot(status: 0);
    final paths = <String>[];
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://test'),
      httpClient: MockClient((request) async {
        paths.add('${request.method} ${request.url.path}');
        return http.Response(jsonEncode(body), 200, headers: {'content-type': 'application/json'});
      }),
    );
    await tester.pumpWidget(
      MaterialApp(home: Scaffold(body: CodingRunScreen(runId: 'run-1', client: client, active: true))),
    );
    await tester.pump();
    expect(tester.widget<FilledButton>(find.byKey(const Key('coding-run-approve'))).onPressed, isNull);

    body = _snapshot(status: 2, plan: _plan());
    await tester.tap(find.byTooltip('Refresh'));
    await tester.pump();
    await tester.pump();

    body = _snapshot(status: 4, plan: _plan());
    await tester.tap(find.byKey(const Key('coding-run-approve')));
    await tester.pump();

    expect(paths, contains('POST /csharp-expert/runs/run-1/approve'));
    await tester.pumpWidget(const SizedBox());
    client.close();
  });

  testWidgets('a finished run shows progress, results and the diff with controls closed', (tester) async {
    final finished = {
      ..._snapshot(status: 14, plan: _plan()),
      'currentStep': 1,
      'totalSteps': 1,
      'fixAttempts': 1,
      'diff': '+    public int UnreadCount() => 0;',
      'build': {'succeeded': true, 'errors': []},
      'test': {'passed': 3, 'total': 3},
      'reviewFindings': <String>[],
    };
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://test'),
      httpClient: MockClient((request) async =>
          http.Response(jsonEncode(finished), 200, headers: {'content-type': 'application/json'})),
    );
    await tester.pumpWidget(
      MaterialApp(home: Scaffold(body: CodingRunScreen(runId: 'run-1', client: client, active: true))),
    );
    await tester.pump();

    expect(find.text('Finished'), findsOneWidget);
    expect(find.text('Step 1 of 1'), findsOneWidget);
    expect(find.text('Build passed · Tests 3/3 passed · 1 fix attempts'), findsOneWidget);
    await tester.scrollUntilVisible(find.byKey(const Key('coding-run-diff')), 200, scrollable: find.descendant(of: find.byType(ListView), matching: find.byType(Scrollable)));
    expect(find.byKey(const Key('coding-run-diff')), findsOneWidget);
    expect(tester.widget<FilledButton>(find.byKey(const Key('coding-run-approve'))).onPressed, isNull);
    expect(tester.widget<OutlinedButton>(find.byKey(const Key('coding-run-stop'))).onPressed, isNull);
    await tester.pumpWidget(const SizedBox());
    client.close();
  });
}
