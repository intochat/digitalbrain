import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

ChatTurnEvent shellTurn(
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

BrainTopologySnapshot shellTopology() => BrainTopologySnapshot(
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

BrainTopologySnapshot shellTopologyWithoutNeuron() => BrainTopologySnapshot(
  modules: shellTopology().modules,
  neurons: const [],
  observedAt: DateTime.utc(2026, 7, 28, 8),
);

Future<void> prepareShellSurface(WidgetTester tester) async {
  tester.view.physicalSize = const Size(1400, 900);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);
}

Future<void> drainShellTimers(WidgetTester tester) async {
  await tester.pump(const Duration(milliseconds: 400));
}
