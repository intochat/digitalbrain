import 'dart:async';

import 'package:digitalbrain_flutter/features/activity/activity_gateway.dart';
import 'package:digitalbrain_flutter/features/activity/activity_models.dart';
import 'package:digitalbrain_flutter/features/activity/activity_run_detail.dart';
import 'package:digitalbrain_flutter/features/activity/activity_run_page.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'activity_test_fixtures.dart';

void main() {
  testWidgets('loads an exact Run into the native detail view', (tester) async {
    final gateway = _RunGateway();
    await tester.pumpWidget(
      MaterialApp(
        home: ActivityRunPage(
          runId: 'run-001',
          gateway: gateway,
          onBackToActivity: () {},
        ),
      ),
    );

    expect(find.byType(CircularProgressIndicator), findsOneWidget);
    expect(gateway.runIds, ['run-001']);

    gateway.requests.single.complete(activityRun());
    await tester.pumpAndSettle();

    expect(find.text('Research brief'), findsOneWidget);
    expect(find.text('Technical details'), findsOneWidget);
  });

  testWidgets('shows a safe retryable error without leaking the failure', (
    tester,
  ) async {
    final gateway = _RunGateway();
    await tester.pumpWidget(
      MaterialApp(
        home: ActivityRunPage(
          runId: 'run-001',
          gateway: gateway,
          onBackToActivity: () {},
        ),
      ),
    );
    gateway.requests.single.completeError(
      StateError('provider payload must-not-escape'),
    );
    await tester.pumpAndSettle();

    expect(find.text("We couldn't load this Run."), findsOneWidget);
    expect(find.textContaining('must-not-escape'), findsNothing);

    await tester.tap(find.byKey(activityRunPageRetryKey));
    await tester.pump();
    expect(gateway.runIds, ['run-001', 'run-001']);
    gateway.requests.last.complete(activityRun());
    await tester.pumpAndSettle();
    expect(find.text('Research brief'), findsOneWidget);
  });

  testWidgets(
    'keeps the loaded Run when only the gateway wrapper identity changes',
    (tester) async {
      final sessionIdentity = Object();
      final firstGateway = _RunGateway();
      await tester.pumpWidget(
        MaterialApp(
          home: ActivityRunPage(
            runId: 'run-001',
            gateway: firstGateway,
            onBackToActivity: () {},
            sessionIdentity: sessionIdentity,
          ),
        ),
      );
      firstGateway.requests.single.complete(activityRun());
      await tester.pumpAndSettle();

      final replacementGateway = _RunGateway();
      await tester.pumpWidget(
        MaterialApp(
          home: ActivityRunPage(
            runId: 'run-001',
            gateway: replacementGateway,
            onBackToActivity: () {},
            sessionIdentity: sessionIdentity,
          ),
        ),
      );
      await tester.pump();

      expect(replacementGateway.runIds, isEmpty);
      expect(find.text('Research brief'), findsOneWidget);
    },
  );

  testWidgets('reveals the raw input kind only in technical details', (
    tester,
  ) async {
    final gateway = _RunGateway();
    await tester.pumpWidget(
      MaterialApp(
        home: ActivityRunPage(
          runId: 'run-001',
          gateway: gateway,
          onBackToActivity: () {},
        ),
      ),
    );
    gateway.requests.single.complete(activityRun());
    await tester.pumpAndSettle();

    expect(find.text('Input'), findsNothing);
    expect(find.text('chat.request'), findsNothing);

    await tester.tap(find.byKey(activityTechnicalDetailsKey));
    await tester.pumpAndSettle();

    expect(find.text('Input kind'), findsOneWidget);
    expect(find.text('chat.request'), findsOneWidget);
  });
}

class _RunGateway implements ActivityRunGateway {
  final List<String> runIds = [];
  final List<Completer<ActivityRun>> requests = [];

  @override
  Future<ActivityRun> loadRun(String runId) {
    runIds.add(runId);
    final request = Completer<ActivityRun>();
    requests.add(request);
    return request.future;
  }
}
