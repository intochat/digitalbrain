import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

// Shared fixtures for shell widget tests.

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
  synapse: synapse ?? (fromUser ? 'UserMessaged' : 'Responded'),
  neuronId: 'chat:owner/main',
  caller: 'chat:owner/main',
  correlationId: 'correlation-$sequence',
  timestamp: DateTime.utc(2026, 7, 28, 8, 0, sequence),
);

BrainTopologySnapshot shellTopology() => BrainTopologySnapshot(
  modules: const [
    BrainModule(id: 'DigitalBrain.Chat.ChatModule'),
    BrainModule(id: 'DigitalBrain.AI.AIModule'),
    BrainModule(id: 'DigitalBrain.Shell.ShellModule'),
    BrainModule(id: 'DigitalBrain.Google.GoogleModule'),
    BrainModule(id: 'DigitalBrain.Assistant.AssistantModule'),
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

BehaviorDocument shellBehaviorDocument({
  String runState = 'Running',
  bool gate = true,
  bool withPrior = false,
}) {
  return BehaviorDocument(
    behaviorId: 'com.digitalbrain.account-enrichment',
    status: 'Active',
    runState: runState,
    activationGateOpen: gate,
    proposedArtifactHash: 'proposed-hash',
    activeArtifactHash: 'active-hash-12345678',
    priorArtifactHash: withPrior ? 'prior-hash-12345678' : null,
    lastCompileFailure: null,
    testsPassed: true,
    isApproved: true,
    lastExecutionOutcome: null,
    programSource: 'public sealed class AccountEnrichmentProgram {}',
    featureName: 'account-enrichment',
    featureText:
        'Feature: account enrichment\n  Scenario: enrich account from email\n',
    displayName: 'Account enrichment',
    description: 'Enrich a Salesforce account from a Gmail message.',
    overview: 'Account enrichment: enrich account from email',
    activeSignatureHex: 'AABBCC',
    activeTaskCount: 0,
    scenarios: const [
      BehaviorScenario(
        scenarioId: 'scenario.enrich-account-from-email',
        title: 'enrich account from email',
        bindingKey: 'bind.enrich-account-from-email',
      ),
    ],
    bindings: const [],
    revisions: [
      const BehaviorRevision(
        role: 'active',
        artifactHash: 'active-hash-12345678',
        signatureHex: 'AABBCC',
        status: 'Active',
        isActive: true,
      ),
      if (withPrior)
        const BehaviorRevision(
          role: 'prior',
          artifactHash: 'prior-hash-12345678',
          status: 'superseded',
          isActive: false,
        ),
    ],
  );
}
