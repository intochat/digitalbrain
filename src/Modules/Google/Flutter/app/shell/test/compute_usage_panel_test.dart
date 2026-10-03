import 'dart:convert';
import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/compute_usage_panel.dart';
import 'package:digitalbrain_flutter_shell/workspace/compute_usage_details.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

Map<String, dynamic> usage(String id) => {
  'id': id,
  'title': 'Assistant response $id',
  'occurredAt': '2026-09-28T10:00:00Z',
  'outcome': 'Succeeded',
  'previewCompute': 0.4795,
  'chargedCompute': null,
  'reservedCompute': null,
  'modelUsage': [
    {
      'provider': 'Provider',
      'model': 'Model',
      'inputTokens': 120,
      'outputTokens': null,
      'usageReported': true,
    },
  ],
  'calls': [
    {'operation': 'read_table', 'succeeded': true},
  ],
  'touched': [
    {'source': 'Customers', 'rowsRead': 25, 'readOnly': true},
  ],
};

void main() {
  testWidgets('failed tool details retain the reason and code', (tester) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: ComputeUsageDetails(
            item: ComputeUsageItem.fromJson({
              ...usage('failed'),
              'calls': [
                {
                  'operation': 'research',
                  'succeeded': false,
                  'errorCode': 'tool_unavailable',
                  'errorMessage': 'Select this capability first.',
                },
              ],
            }),
          ),
        ),
      ),
    );
    expect(find.textContaining('research · Failed'), findsOneWidget);
    expect(
      find.textContaining('Select this capability first.'),
      findsOneWidget,
    );
    expect(find.textContaining('tool_unavailable'), findsOneWidget);
  });

  test('mixed usage shows charges and estimates as separate labels', () {
    expect(
      usageAmountLabel(
        const ComputeUsageItem(
          id: 'mixed',
          title: 'Mixed',
          chargedCompute: 2,
          reservedCompute: 1,
          previewCompute: .5,
        ),
      ),
      '2 Charged\n1 Reserved\n0.5 Preview',
    );
    expect(computeAmount(0), '0');
  });

  testWidgets(
    'open panel recovers committed usage when receipt delivery is lost',
    (tester) async {
      var ready = false;
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://test'),
        httpClient: MockClient(
          (request) async => http.Response(
            jsonEncode(
              request.url.path == '/compute/summary'
                  ? {}
                  : {
                      'items': ready ? [usage('recovered')] : [],
                      'nextCursor': null,
                    },
            ),
            200,
          ),
        ),
      );
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: ComputeUsagePanel(
              client: client,
              workspaceId: 'workspace',
              workspaceName: 'Project',
              onClose: () {},
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('No usage recorded yet.'), findsOneWidget);
      ready = true;
      await tester.pump(const Duration(seconds: 30));
      await tester.pumpAndSettle();
      expect(find.text('Assistant response recovered'), findsOneWidget);
      await tester.pumpWidget(const SizedBox());
    },
  );
  testWidgets(
    'refresh arriving during a fetch is replayed and narrow panel remains usable',
    (tester) async {
      tester.view.physicalSize = const Size(360, 640);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final signal = ValueNotifier<int>(0);
      final pending = Completer<http.Response>();
      var reads = 0;
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://test'),
        httpClient: MockClient((request) async {
          if (request.url.path == '/compute/summary') {
            return http.Response(
              '{"chargedCompute":2,"reservedCompute":1}',
              200,
            );
          }
          reads++;
          if (reads == 1) return pending.future;
          return http.Response(
            jsonEncode({
              'items': [usage('new')],
              'nextCursor': null,
            }),
            200,
          );
        }),
      );
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: ComputeUsagePanel(
              client: client,
              workspaceId: 'workspace',
              workspaceName: 'My project',
              onClose: () {},
              refreshSignal: signal,
            ),
          ),
        ),
      );
      await tester.pump();
      signal.value++;
      pending.complete(http.Response('{"items":[],"nextCursor":null}', 200));
      await tester.pumpAndSettle();
      expect(reads, 2);
      expect(find.text('Assistant response new'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await tester.tap(find.text('Assistant response new'));
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      signal.dispose();
    },
  );

  testWidgets('changing workspace discards an older in-flight response', (
    tester,
  ) async {
    final old = Completer<http.Response>();
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://test'),
      httpClient: MockClient((request) async {
        if (request.url.path == '/compute/summary') {
          return http.Response('{}', 200);
        }
        if (request.url.path.contains('/old/')) return old.future;
        return http.Response(
          jsonEncode({
            'items': [usage('new')],
            'nextCursor': null,
          }),
          200,
        );
      }),
    );
    Widget panel(String id) => MaterialApp(
      home: Scaffold(
        body: ComputeUsagePanel(
          client: client,
          workspaceId: id,
          workspaceName: id,
          onClose: () {},
        ),
      ),
    );
    await tester.pumpWidget(panel('old'));
    await tester.pumpWidget(panel('new'));
    old.complete(
      http.Response(
        jsonEncode({
          'items': [usage('old')],
          'nextCursor': null,
        }),
        200,
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Assistant response new'), findsOneWidget);
    expect(find.text('Assistant response old'), findsNothing);
  });

  testWidgets(
    'authoritative history replaces local copy without mixing legacy amounts into totals',
    (tester) async {
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://test'),
        httpClient: MockClient(
          (request) async => http.Response(
            jsonEncode(
              request.url.path == '/compute/summary'
                  ? {'chargedCompute': 2, 'reservedCompute': 0}
                  : {
                      'items': [usage('same')],
                      'nextCursor': null,
                    },
            ),
            200,
          ),
        ),
      );
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: ComputeUsagePanel(
              client: client,
              workspaceId: 'workspace',
              workspaceName: 'Project',
              onClose: () {},
              savedHistory: () => [
                const ComputeUsageItem(
                  id: 'same',
                  title: 'Old duplicate',
                  previewCompute: 100,
                  localHistory: true,
                ),
                const ComputeUsageItem(
                  id: 'legacy',
                  title: 'Old saved receipt',
                  previewCompute: 500,
                  localHistory: true,
                ),
              ],
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('Old duplicate'), findsNothing);
      expect(find.text('Assistant response same'), findsOneWidget);
      expect(find.text('Old saved receipt'), findsOneWidget);
      expect(find.text('2 Compute'), findsOneWidget);
    },
  );
  testWidgets(
    'Compute separates account charges from preview and opens resource details',
    (tester) async {
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://test'),
        httpClient: MockClient((request) async {
          return http.Response(
            jsonEncode(
              request.url.path == '/compute/summary'
                  ? {
                      'chargedCompute': 2,
                      'reservedCompute': 3,
                      'spentCompute': 5,
                      'limitCompute': 10,
                    }
                  : {
                      'items': [usage('run')],
                      'nextCursor': null,
                    },
            ),
            200,
          );
        }),
      );
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: ComputeUsagePanel(
              client: client,
              workspaceId: 'workspace',
              workspaceName: 'My project',
              onClose: () {},
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('Charged'), findsOneWidget);
      expect(find.text('Reserved'), findsOneWidget);
      expect(find.textContaining('0.4795'), findsOneWidget);
      await tester.tap(find.text('Assistant response run'));
      await tester.pumpAndSettle();
      expect(find.textContaining('Model'), findsWidgets);
      expect(find.textContaining('Unreported'), findsWidgets);
      expect(find.textContaining('Customers'), findsOneWidget);
      expect(find.textContaining('Preview · not charged'), findsOneWidget);
    },
  );

  testWidgets(
    'history paginates, deduplicates and retries without losing rows',
    (tester) async {
      var fail = true;
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://test'),
        httpClient: MockClient((request) async {
          if (request.url.path == '/compute/summary') {
            return http.Response('{}', 200);
          }
          if (request.url.queryParameters['cursor'] != null) {
            if (fail) return http.Response('{"error":"offline"}', 503);
            return http.Response(
              jsonEncode({
                'items': [usage('a'), usage('b')],
                'nextCursor': null,
              }),
              200,
            );
          }
          return http.Response(
            jsonEncode({
              'items': [usage('a')],
              'nextCursor': 'next',
            }),
            200,
          );
        }),
      );
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: ComputeUsagePanel(
              client: client,
              workspaceId: 'workspace',
              workspaceName: 'Project',
              onClose: () {},
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('Load more'));
      await tester.pumpAndSettle();
      expect(find.text('Assistant response a'), findsOneWidget);
      expect(find.textContaining('Could not load'), findsOneWidget);
      fail = false;
      await tester.tap(find.text('Retry'));
      await tester.pumpAndSettle();
      expect(find.text('Assistant response a'), findsOneWidget);
      expect(find.text('Assistant response b'), findsOneWidget);
    },
  );
}
