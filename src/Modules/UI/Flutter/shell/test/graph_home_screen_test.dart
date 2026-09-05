import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/chat/brain_chat_screen.dart';
import 'package:digitalbrain_flutter_shell/chat/graph_home_screen.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/shell_test_support.dart';

BrainSnapshot snapshot({bool subscribed = true}) => BrainSnapshot(
  rootId: 'chat:owner/main',
  observedAt: DateTime.utc(2026, 9, 5, 10),
  scope: 'Current conversation',
  nodes: const [
    BrainNeuron(
      id: 'timer',
      type: 'Timer',
      name: 'daily',
      label: 'Daily timer',
      module: 'Time',
      handledSignals: ['Tick'],
    ),
    BrainNeuron(
      id: 'assistant',
      type: 'Assistant',
      name: 'main',
      label: 'Ino',
      module: 'AI',
      handledSignals: ['Tick'],
    ),
  ],
  synapses: subscribed
      ? const [
          BrainSynapse(
            id: 'timer-assistant',
            sourceId: 'timer',
            targetId: 'assistant',
            signalType: 'Tick',
            kind: 'Bound',
            canUnsubscribe: true,
          ),
        ]
      : const [],
);

Widget host(Widget child) => MaterialApp(
  theme: KitTheme.light(),
  home: KitThemeScope(child: Scaffold(body: child)),
);

void main() {
  testWidgets(
    'parallel, reverse and self synapses have distinct inspect controls',
    (tester) async {
      await prepareShellSurface(tester);
      final base = snapshot();
      final edges = [
        ...base.synapses,
        const BrainSynapse(
          id: 'second',
          sourceId: 'timer',
          targetId: 'assistant',
          signalType: 'Reminder',
          kind: 'Learned',
        ),
        const BrainSynapse(
          id: 'reverse',
          sourceId: 'assistant',
          targetId: 'timer',
          signalType: 'Response',
          kind: 'Bound',
        ),
        const BrainSynapse(
          id: 'self',
          sourceId: 'timer',
          targetId: 'timer',
          signalType: 'Tick',
          kind: 'Bound',
        ),
      ];
      final inspected = <String>[];
      await tester.pumpWidget(
        host(
          LumenBrainGraph(
            snapshot: BrainSnapshot(
              rootId: base.rootId,
              observedAt: base.observedAt,
              scope: base.scope,
              nodes: base.nodes,
              synapses: edges,
            ),
            onNeuron: (_) {},
            onSynapse: (edge) => inspected.add(edge.id),
          ),
        ),
      );
      await tester.pumpAndSettle();
      final rects = <Rect>[];
      for (final edge in edges) {
        final control = find.byKey(ValueKey('synapse_${edge.id}'));
        final rect = tester.getRect(control);
        expect(rects.every((previous) => !previous.overlaps(rect)), isTrue);
        rects.add(rect);
        await tester.tap(control);
        await tester.pumpAndSettle();
      }
      expect(inspected, edges.map((edge) => edge.id).toList());
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      await drainShellTimers(tester);
    },
  );

  testWidgets('home shows observed graph above the compact conversation', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    await tester.pumpWidget(
      host(
        GraphHomeScreen(
          chatName: 'main',
          turns: const [],
          onReadBrain: () async => snapshot(),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.byType(LumenBrainGraph), findsOneWidget);
    expect(
      tester.widget<BrainChatScreen>(find.byType(BrainChatScreen)).presentation,
      BrainChatPresentation.compact,
    );
    expect(
      tester.getRect(find.byKey(const Key('graph_brain_panel'))).bottom,
      lessThanOrEqualTo(
        tester.getRect(find.byKey(const Key('graph_chat_panel'))).top,
      ),
    );
    expect(find.text('SIMULATION'), findsNothing);
    expect(find.textContaining('2 neurons · 1 synapses'), findsOneWidget);
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox());
    await drainShellTimers(tester);
  });

  testWidgets(
    'inspect a real bound edge and confirm its removal from snapshot',
    (tester) async {
      await prepareShellSurface(tester);
      var subscribed = true;
      final changes = <bool>[];
      await tester.pumpWidget(
        host(
          GraphHomeScreen(
            chatName: 'main',
            turns: const [],
            onReadBrain: () async => snapshot(subscribed: subscribed),
            onSetBrainSubscription:
                ({
                  required sourceId,
                  required targetId,
                  required signalType,
                  required subscribed,
                }) async {
                  expect(sourceId, 'timer');
                  expect(targetId, 'assistant');
                  expect(signalType, 'Tick');
                  changes.add(subscribed);
                },
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const ValueKey('synapse_timer-assistant')));
      await tester.pumpAndSettle();
      expect(find.text('Bound'), findsOneWidget);
      subscribed = false;
      await tester.tap(find.byKey(const Key('unsubscribe_synapse')));
      await tester.pumpAndSettle();
      expect(changes, [false]);
      expect(
        tester
            .widget<LumenBrainGraph>(find.byType(LumenBrainGraph))
            .snapshot
            .synapses,
        isEmpty,
      );
      expect(
        find.text('This connection is no longer in the current graph.'),
        findsOneWidget,
      );
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      await drainShellTimers(tester);
    },
  );

  testWidgets(
    'unavailable observation is visible and never becomes simulated activity',
    (tester) async {
      await prepareShellSurface(tester);
      await tester.pumpWidget(
        host(
          GraphHomeScreen(
            chatName: 'main',
            turns: const [],
            onReadBrain: () async => throw StateError('offline'),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('Observation unavailable'), findsOneWidget);
      expect(find.byType(LumenBrainGraph), findsNothing);
      expect(find.text('Retry'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      await drainShellTimers(tester);
    },
  );

  testWidgets('narrow graph and directory remain usable without overflow', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(600, 800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      host(
        GraphHomeScreen(
          chatName: 'main',
          turns: const [],
          onReadBrain: () async => snapshot(),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('brain_directory')));
    await tester.pumpAndSettle();
    expect(find.text('Your neurons'), findsOneWidget);
    expect(find.text('Daily timer'), findsWidgets);
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox());
    await drainShellTimers(tester);
  });
}
