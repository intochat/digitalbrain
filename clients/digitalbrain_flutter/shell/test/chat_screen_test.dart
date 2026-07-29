import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/chat_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

ChatTurnEvent _turn(
  int sequence,
  bool fromUser,
  String text, {
  String? synapse,
}) => ChatTurnEvent(
  sequence: sequence,
  fromUser: fromUser,
  text: text,
  commandId: 'c$sequence',
  synapse: synapse ?? (fromUser ? 'UserMessaged' : 'AssistantResponded'),
  neuronId: 'chat:owner/main',
  caller: 'chat:owner/main',
  correlationId: 'correlation-$sequence',
  timestamp: DateTime.utc(2026, 7, 28, 8, 0, sequence),
);

BrainTopologySnapshot _topology() => BrainTopologySnapshot(
  modules: const [
    BrainModule(id: 'DigitalBrain.Chat.ChatModule'),
    BrainModule(id: 'DigitalBrain.AI.AIModule'),
    BrainModule(id: 'DigitalBrain.Flutter.FlutterModule'),
    BrainModule(id: 'DigitalBrain.Google.GoogleModule'),
    BrainModule(id: 'DigitalBrain.OS.OSBehaviorsModule'),
    BrainModule(id: 'DigitalBrain.Salesforce.SalesforceModule'),
  ],
  neurons: const [
    BrainNeuron(
      id: 'chat:owner/main',
      grainType: 'chat',
      identity: 'owner/main',
      placement: 'cluster-1',
    ),
  ],
  observedAt: DateTime.utc(2026, 7, 28, 8),
);

BrainTopologySnapshot _topologyWithoutNeuron() => BrainTopologySnapshot(
  modules: _topology().modules,
  neurons: const [],
  observedAt: DateTime.utc(2026, 7, 28, 8),
);

Future<void> _prepareSurface(WidgetTester tester) async {
  tester.view.physicalSize = const Size(1400, 900);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);
}

Future<void> _drainChatTimers(WidgetTester tester) async {
  await tester.pump(const Duration(milliseconds: 400));
}

void main() {
  testWidgets('the workspace exposes Chat, Activity, and Brain destinations', (
    tester,
  ) async {
    await _prepareSurface(tester);
    final topology = StreamController<BrainTopologySnapshot>();
    addTearDown(topology.close);

    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', topology: topology.stream),
    );
    topology.add(_topology());
    await tester.pumpAndSettle();
    await _drainChatTimers(tester);

    expect(find.byKey(const Key('destination_chat')), findsOneWidget);
    expect(find.byKey(const Key('destination_activity')), findsOneWidget);
    expect(find.byKey(const Key('destination_brain')), findsOneWidget);

    await tester.tap(find.byKey(const Key('destination_activity')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('activity_screen')), findsOneWidget);
    expect(find.text('No activity yet.'), findsOneWidget);

    await tester.tap(find.byKey(const Key('destination_brain')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('brain_screen')), findsOneWidget);
    await _drainChatTimers(tester);
  });

  testWidgets('activity shows journal facts without message content', (
    tester,
  ) async {
    await _prepareSurface(tester);
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(turns.close);

    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', turns: turns.stream, onSend: (_) async {}),
    );

    turns.add(_turn(1, true, 'private customer message'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('destination_activity')));
    await tester.pumpAndSettle();

    expect(find.text('UserMessaged'), findsOneWidget);
    expect(find.text('sequence 001'), findsOneWidget);
    expect(find.text('command c1'), findsOneWidget);
    expect(find.text('private customer message'), findsNothing);
    await _drainChatTimers(tester);
  });

  testWidgets('activity renders the authoritative synapse name', (
    tester,
  ) async {
    await _prepareSurface(tester);
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(turns.close);

    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', turns: turns.stream, onSend: (_) async {}),
    );
    turns.add(
      _turn(2, true, 'private payload', synapse: 'ObservedCustomSynapse'),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('destination_activity')));
    await tester.pumpAndSettle();

    expect(find.text('ObservedCustomSynapse'), findsOneWidget);
    expect(find.text('UserMessaged'), findsNothing);
    expect(find.text('private payload'), findsNothing);
    await _drainChatTimers(tester);
  });

  testWidgets('the shared event projection survives destination changes', (
    tester,
  ) async {
    await _prepareSurface(tester);
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(turns.close);

    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', turns: turns.stream, onSend: (_) async {}),
    );

    await tester.tap(find.byKey(const Key('destination_activity')));
    await tester.pumpAndSettle();
    turns.add(_turn(9, false, 'arrived while activity was open'));
    await tester.pumpAndSettle();

    expect(find.text('AssistantResponded'), findsOneWidget);

    await tester.tap(find.byKey(const Key('destination_chat')));
    await tester.pumpAndSettle();
    expect(find.text('arrived while activity was open'), findsOneWidget);
    await _drainChatTimers(tester);
  });

  testWidgets('narrow windows use bottom navigation', (tester) async {
    tester.view.physicalSize = const Size(600, 800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(const BrainChatApp(chatName: 'main'));
    await tester.pumpAndSettle();
    await _drainChatTimers(tester);

    expect(find.byType(NavigationBar), findsOneWidget);
    expect(find.byType(NavigationRail), findsNothing);
    await _drainChatTimers(tester);
  });

  testWidgets('Brain renders live modules and active neurons', (tester) async {
    final topology = StreamController<BrainTopologySnapshot>();
    addTearDown(topology.close);

    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', topology: topology.stream),
    );
    topology.add(_topology());
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('destination_brain')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('brain_topology_canvas')), findsOneWidget);
    expect(find.byKey(const Key('topology_module_0')), findsOneWidget);
    expect(find.byKey(const Key('topology_module_1')), findsOneWidget);
    expect(find.text('Chat'), findsWidgets);
    expect(find.text('AI'), findsOneWidget);
    expect(find.text('chat:owner/main'), findsOneWidget);
    await _drainChatTimers(tester);
  });

  testWidgets('Brain clears a transient topology failure after recovery', (
    tester,
  ) async {
    await _prepareSurface(tester);
    final topology = StreamController<BrainTopologySnapshot>();
    addTearDown(topology.close);

    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', topology: topology.stream),
    );
    topology.add(_topology());
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

    topology.add(_topology());
    await tester.pumpAndSettle();

    expect(find.text('Connected'), findsOneWidget);
    expect(find.byKey(const Key('brain_topology_canvas')), findsOneWidget);
    await _drainChatTimers(tester);
  });

  testWidgets('a chat turn pulses Brain and opens correlation inspector', (
    tester,
  ) async {
    await _prepareSurface(tester);
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
    topology.add(_topology());
    turns.add(_turn(9, true, 'private pulse content'));
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
    await _drainChatTimers(tester);
  });

  testWidgets('a pulse waits for its neuron to appear in live topology', (
    tester,
  ) async {
    await _prepareSurface(tester);
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
    topology.add(_topologyWithoutNeuron());
    turns.add(_turn(12, false, 'not exposed by the topology view'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('destination_brain')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('brain_pulse')), findsNothing);

    await tester.pump(const Duration(milliseconds: 1200));
    topology.add(_topology());
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('brain_pulse')), findsOneWidget);
    await _drainChatTimers(tester);
  });

  testWidgets('Brain clears a neuron selection when the neuron disappears', (
    tester,
  ) async {
    await _prepareSurface(tester);
    tester.view.physicalSize = const Size(1200, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final topology = StreamController<BrainTopologySnapshot>();
    addTearDown(topology.close);

    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', topology: topology.stream),
    );
    topology.add(_topology());
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('destination_brain')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('topology_neuron_0')));
    await tester.pumpAndSettle();

    expect(find.text('cluster-1'), findsOneWidget);

    topology.add(_topologyWithoutNeuron());
    await tester.pumpAndSettle();

    expect(find.text('cluster-1'), findsNothing);
    expect(
      find.text(
        'Select a module or neuron. New chat turns open their causal pulse automatically.',
      ),
      findsOneWidget,
    );
    await _drainChatTimers(tester);
  });

  testWidgets('Brain clears causal selection when the chat changes', (
    tester,
  ) async {
    await _prepareSurface(tester);
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
    topology.add(_topology());
    turns.add(_turn(14, false, 'private turn'));
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
    await _drainChatTimers(tester);
  });

  testWidgets('an empty conversation mounts the flyer chat surface', (
    tester,
  ) async {
    await _prepareSurface(tester);
    await tester.pumpWidget(const BrainChatApp(chatName: 'main'));
    await tester.pump();

    expect(find.byKey(const Key('chat_surface')), findsOneWidget);
    await _drainChatTimers(tester);
  });

  testWidgets('journal turns project as message text on the chat surface', (
    tester,
  ) async {
    await _prepareSurface(tester);
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(turns.close);

    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', turns: turns.stream, onSend: (_) async {}),
    );

    turns.add(_turn(1, true, 'how is my account?'));
    turns.add(_turn(2, false, 'Your account is up to date.'));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('chat_surface')), findsOneWidget);
    expect(find.text('how is my account?'), findsOneWidget);
    expect(find.text('Your account is up to date.'), findsOneWidget);
    await _drainChatTimers(tester);
  });

  testWidgets('a repeated sequence is projected once', (tester) async {
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(turns.close);

    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', turns: turns.stream, onSend: (_) async {}),
    );

    turns.add(_turn(7, false, 'only once'));
    turns.add(_turn(7, false, 'only once'));
    await tester.pumpAndSettle();

    expect(find.text('only once'), findsOneWidget);
    await _drainChatTimers(tester);
  });

  testWidgets('sending hands the text to the edge and shows the journal answer', (
    tester,
  ) async {
    await _prepareSurface(tester);
    final sent = <String>[];
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(turns.close);

    await tester.pumpWidget(
      BrainChatApp(
        chatName: 'main',
        turns: turns.stream,
        onSend: (text) async => sent.add(text),
      ),
    );

    await tester.enterText(find.byType(TextField), 'enrich my account');
    await tester.testTextInput.receiveAction(TextInputAction.send);
    await tester.pumpAndSettle();

    expect(sent, ['enrich my account']);
    expect(find.text('enrich my account'), findsWidgets);

    turns.add(_turn(1, true, 'enrich my account'));
    turns.add(_turn(2, false, 'Done.'));
    await tester.pumpAndSettle();

    expect(find.text('Done.'), findsOneWidget);
    await _drainChatTimers(tester);
  });

  testWidgets('a disconnected edge says so and mounts chat without a send path', (
    tester,
  ) async {
    await _prepareSurface(tester);
    await tester.pumpWidget(
      const BrainChatApp(chatName: 'main', statusMessage: 'no edge'),
    );
    await tester.pump();

    expect(find.text('not connected'), findsOneWidget);
    expect(find.byKey(const Key('chat_surface')), findsOneWidget);
    await _drainChatTimers(tester);
  });
}
