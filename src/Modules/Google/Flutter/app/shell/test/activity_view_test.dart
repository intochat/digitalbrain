import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/activity/activity_controller.dart';
import 'package:digitalbrain_flutter_shell/workspace/activity/activity_view.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

ActivityRecord item(int sequence, String kind) => ActivityRecord(
  id: 'event-$sequence',
  operationId: 'operation',
  sequence: sequence,
  at: DateTime.utc(2026, 9, 25, 12),
  kind: kind,
  type: 'Read',
  status: 'started',
  sourceId: 'caller',
  targetId: 'target',
);

void main() {
  testWidgets('selection links activity list to path and details', (
    tester,
  ) async {
    final controller = ActivityController(
      read: () async => ActivitySnapshot(
        events: [item(1, 'CallStarted'), item(2, 'CallArrived')],
        nextSequence: 2,
        gap: false,
        observedAt: DateTime.utc(2026, 9, 25, 12),
      ),
      watch: (_) => const Stream<ActivityUpdate>.empty(),
    );
    await controller.start();
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(body: ActivityView(controller: controller)),
      ),
    );
    await tester.tap(find.byKey(const Key('activity_event_1')));
    await tester.pump();
    expect(controller.selectedOperationId, 'operation');
    expect(find.text('Read'), findsWidgets);
    expect(find.textContaining('caller'), findsWidgets);
    expect(find.textContaining('target'), findsWidgets);
    expect(find.byKey(const Key('ui_graph')), findsOneWidget);
    controller.dispose();
  });

  test('pause keeps frame fixed while new observations arrive', () async {
    final updates = StreamController<ActivityUpdate>();
    final controller = ActivityController(
      read: () async => ActivitySnapshot(
        events: [item(1, 'CallStarted')],
        nextSequence: 1,
        gap: false,
        observedAt: DateTime.utc(2026, 9, 25, 12),
      ),
      watch: (_) => updates.stream,
    );
    await controller.start();
    controller.pause();
    updates.add(ActivityItem(item(2, 'CallCompleted')));
    await Future<void>.delayed(Duration.zero);
    expect(controller.visibleCursor, 1);
    controller.resumeLive();
    expect(controller.visibleCursor, 2);
    await updates.close();
    controller.dispose();
  });

  testWidgets('gap and unknown target are visible without invented edge', (
    tester,
  ) async {
    final controller = ActivityController(
      read: () async => ActivitySnapshot(
        events: [
          ActivityRecord(
            id: 'signal',
            operationId: 'signal-op',
            sequence: 3,
            at: DateTime.utc(2026, 9, 25),
            kind: 'SignalPublished',
            type: 'Changed',
            status: 'published',
            sourceId: 'caller',
          ),
        ],
        nextSequence: 3,
        gap: true,
        observedAt: DateTime.utc(2026, 9, 25),
      ),
      watch: (_) => const Stream<ActivityUpdate>.empty(),
    );
    await controller.start();
    await tester.pumpWidget(
      MaterialApp(
        home: MediaQuery(
          data: const MediaQueryData(disableAnimations: true),
          child: Scaffold(body: ActivityView(controller: controller)),
        ),
      ),
    );
    expect(find.textContaining('earlier activity was lost'), findsOneWidget);
    expect(controller.edges, isEmpty);
    expect(controller.pulse?.local, isTrue);
    await tester.tap(find.byKey(const Key('activity_event_3')));
    await tester.pump();
    expect(find.textContaining('Unknown target'), findsWidgets);
    controller.dispose();
  });
}
