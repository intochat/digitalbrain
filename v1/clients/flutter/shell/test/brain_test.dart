import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/chat_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/shell_test_support.dart';

void main() {
  testWidgets('Brain renders live modules and active neurons', (tester) async {
    await tester.pumpWidget(
      BrainChatApp(
        chatName: 'main',
        onLoadTopology: () async => shellTopology(),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('destination_brain')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('brain_topology_canvas')), findsOneWidget);
    expect(find.byKey(const Key('topology_module_0')), findsOneWidget);
    expect(find.byKey(const Key('topology_module_1')), findsOneWidget);
    expect(find.text('Chat'), findsWidgets);
    expect(find.text('AI'), findsOneWidget);
    expect(find.text('chat:owner/main'), findsOneWidget);
    await drainShellTimers(tester);
  });

  testWidgets('Brain clears a transient topology failure after recovery', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    var fail = false;
    await tester.pumpWidget(
      BrainChatApp(
        chatName: 'main',
        onLoadTopology: () async {
          if (fail) {
            throw StateError('topology temporarily unavailable');
          }
          return shellTopology();
        },
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('destination_brain')));
    await tester.pumpAndSettle();

    expect(find.text('Connected'), findsOneWidget);
    expect(find.byKey(const Key('brain_topology_canvas')), findsOneWidget);

    fail = true;
    await tester.tap(find.byKey(const Key('destination_chat')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('destination_brain')));
    await tester.pumpAndSettle();

    expect(find.text('Offline'), findsOneWidget);
    expect(find.byKey(const Key('brain_topology_canvas')), findsNothing);
    expect(find.text('Waiting for live topology…'), findsOneWidget);

    fail = false;
    await tester.tap(find.byKey(const Key('destination_chat')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('destination_brain')));
    await tester.pumpAndSettle();

    expect(find.text('Connected'), findsOneWidget);
    expect(find.byKey(const Key('brain_topology_canvas')), findsOneWidget);
    await drainShellTimers(tester);
  });

  testWidgets('a chat turn pulses Brain and opens correlation inspector', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(turns.close);

    await tester.pumpWidget(
      BrainChatApp(
        chatName: 'main',
        onLoadTopology: () async => shellTopology(),
        turns: turns.stream,
      ),
    );
    turns.add(shellTurn(9, true, 'private pulse content'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('destination_brain')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('brain_pulse')), findsOneWidget);
    expect(find.byKey(const Key('brain_local_pulse')), findsOneWidget);
    expect(find.byKey(const Key('brain_edge_pulse')), findsNothing);
    expect(find.byKey(const Key('brain_inspector')), findsOneWidget);
    expect(find.text('correlation-9'), findsOneWidget);
    expect(find.text('chat:owner/main'), findsWidgets);
    expect(find.text('private pulse content'), findsNothing);
    await drainShellTimers(tester);
  });

  testWidgets(
    'a pulse paints from the journal even before the neuron is in grain stats',
    (tester) async {
      await prepareShellSurface(tester);
      final turns = StreamController<ChatTurnEvent>();
      addTearDown(turns.close);

      await tester.pumpWidget(
        BrainChatApp(
          chatName: 'main',
          onLoadTopology: () async => shellTopologyWithoutNeuron(),
          turns: turns.stream,
        ),
      );
      turns.add(shellTurn(12, false, 'not exposed by the topology view'));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('destination_brain')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('brain_pulse')), findsOneWidget);
      await drainShellTimers(tester);
    },
  );

  testWidgets('Brain clears a neuron selection when the neuron disappears', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    tester.view.physicalSize = const Size(1200, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    var topology = shellTopology();

    await tester.pumpWidget(
      BrainChatApp(
        chatName: 'main',
        onLoadTopology: () async => topology,
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('destination_brain')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('topology_neuron_0')));
    await tester.pumpAndSettle();

    expect(find.text('cluster-1'), findsOneWidget);

    topology = shellTopologyWithoutNeuron();
    await tester.tap(find.byKey(const Key('destination_chat')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('destination_brain')));
    await tester.pumpAndSettle();

    expect(find.text('cluster-1'), findsNothing);
    expect(
      find.text(
        'Select a module or neuron. New chat turns open their causal pulse automatically.',
      ),
      findsOneWidget,
    );
    await drainShellTimers(tester);
  });

  testWidgets('Brain clears causal selection when the chat changes', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    final turns = StreamController<ChatTurnEvent>.broadcast();
    addTearDown(turns.close);

    await tester.pumpWidget(
      BrainChatApp(
        chatName: 'main',
        onLoadTopology: () async => shellTopology(),
        turns: turns.stream,
      ),
    );
    turns.add(shellTurn(14, false, 'private turn'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('destination_brain')));
    await tester.pumpAndSettle();

    expect(find.text('correlation-14'), findsOneWidget);

    await tester.pumpWidget(
      BrainChatApp(
        chatName: 'other',
        onLoadTopology: () async => shellTopology(),
        turns: turns.stream,
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('correlation-14'), findsNothing);
    await drainShellTimers(tester);
  });

  testWidgets('topology loads on start, Brain tab, and chat turn only', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    var loads = 0;
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(turns.close);

    await tester.pumpWidget(
      BrainChatApp(
        chatName: 'main',
        onLoadTopology: () async {
          loads++;
          return shellTopology();
        },
        turns: turns.stream,
      ),
    );
    await tester.pumpAndSettle();
    expect(loads, 1);

    await tester.tap(find.byKey(const Key('destination_activity')));
    await tester.pumpAndSettle();
    expect(loads, 1);

    await tester.tap(find.byKey(const Key('destination_brain')));
    await tester.pumpAndSettle();
    expect(loads, 2);

    turns.add(shellTurn(1, true, 'reload topology'));
    await tester.pumpAndSettle();
    expect(loads, 3);

    await drainShellTimers(tester);
  });
}
