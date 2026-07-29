import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/chat_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/shell_test_support.dart';
void main() {
  testWidgets('Brain renders live modules and active neurons', (tester) async {
    final topology = StreamController<BrainTopologySnapshot>();
    addTearDown(topology.close);

    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', topology: topology.stream),
    );
    topology.add(shellTopology());
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
    final topology = StreamController<BrainTopologySnapshot>();
    addTearDown(topology.close);

    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', topology: topology.stream),
    );
    topology.add(shellTopology());
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('destination_brain')));
    await tester.pumpAndSettle();

    expect(find.text('Connected'), findsOneWidget);
    expect(find.byKey(const Key('brain_topology_canvas')), findsOneWidget);

    topology.addError(StateError('topology temporarily unavailable'));
    await tester.pumpAndSettle();

    expect(find.text('Offline'), findsOneWidget);
    expect(find.byKey(const Key('brain_topology_canvas')), findsNothing);
    expect(find.text('Waiting for live topology…'), findsOneWidget);

    topology.add(shellTopology());
    await tester.pumpAndSettle();

    expect(find.text('Connected'), findsOneWidget);
    expect(find.byKey(const Key('brain_topology_canvas')), findsOneWidget);
    await drainShellTimers(tester);
  });

  testWidgets('a chat turn pulses Brain and opens correlation inspector', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    final topology = StreamController<BrainTopologySnapshot>();
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(topology.close);
    addTearDown(turns.close);

    await tester.pumpWidget(
      BrainChatApp(
        chatName: 'main',
        topology: topology.stream,
        turns: turns.stream,
      ),
    );
    topology.add(shellTopology());
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

  testWidgets('a pulse waits for its neuron to appear in live topology', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    final topology = StreamController<BrainTopologySnapshot>();
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(topology.close);
    addTearDown(turns.close);

    await tester.pumpWidget(
      BrainChatApp(
        chatName: 'main',
        topology: topology.stream,
        turns: turns.stream,
      ),
    );
    topology.add(shellTopologyWithoutNeuron());
    turns.add(shellTurn(12, false, 'not exposed by the topology view'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('destination_brain')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('brain_pulse')), findsNothing);

    await tester.pump(const Duration(milliseconds: 1200));
    topology.add(shellTopology());
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('brain_pulse')), findsOneWidget);
    await drainShellTimers(tester);
  });

  testWidgets('Brain clears a neuron selection when the neuron disappears', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    tester.view.physicalSize = const Size(1200, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final topology = StreamController<BrainTopologySnapshot>();
    addTearDown(topology.close);

    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', topology: topology.stream),
    );
    topology.add(shellTopology());
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('destination_brain')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('topology_neuron_0')));
    await tester.pumpAndSettle();

    expect(find.text('cluster-1'), findsOneWidget);

    topology.add(shellTopologyWithoutNeuron());
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
    final topology = StreamController<BrainTopologySnapshot>.broadcast();
    final turns = StreamController<ChatTurnEvent>.broadcast();
    addTearDown(topology.close);
    addTearDown(turns.close);

    await tester.pumpWidget(
      BrainChatApp(
        chatName: 'main',
        topology: topology.stream,
        turns: turns.stream,
      ),
    );
    topology.add(shellTopology());
    turns.add(shellTurn(14, false, 'private turn'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('destination_brain')));
    await tester.pumpAndSettle();

    expect(find.text('correlation-14'), findsOneWidget);

    await tester.pumpWidget(
      BrainChatApp(
        chatName: 'other',
        topology: topology.stream,
        turns: turns.stream,
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('correlation-14'), findsNothing);
    await drainShellTimers(tester);
  });
}

