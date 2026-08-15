import 'dart:convert';

import 'package:digitalbrain_corev2/digitalbrain_corev2.dart';
import 'package:digitalbrain_corev2_shell/main.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('chat renders the operation journal and live BrainGraph', (
    tester,
  ) async {
    await tester.binding.setSurfaceSize(const Size(1500, 900));
    addTearDown(() => tester.binding.setSurfaceSize(null));
    final api = _FakeProductApi();
    await tester.pumpWidget(
      DigitalBrainShell(
        productBase: Uri.parse('http://localhost:5100'),
        api: api,
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Chat'), findsOneWidget);
    expect(find.text('BrainGraph'), findsOneWidget);
    expect(find.text('Runtime journal'), findsOneWidget);
    expect(find.text('source'), findsOneWidget);
    expect(find.text('assessment'), findsOneWidget);

    await tester.enterText(
      find.byKey(const Key('chat-input')),
      'Wire and run proof',
    );
    await tester.tap(find.byKey(const Key('chat-send')));
    await tester.pumpAndSettle();

    expect(api.invocations, ['Chat.Send@1']);
    expect(find.text('Wire and run proof'), findsWidgets);
    expect(find.text('Proof completed.'), findsOneWidget);
    expect(find.text('Proof.Run@1'), findsWidgets);
    expect(find.text('Chat.UserMessage@1'), findsOneWidget);
    expect(find.text('ProofProduced@1'), findsOneWidget);
  });
}

final class _FakeProductApi implements DigitalBrainProductApi {
  final List<String> invocations = [];

  BrainSnapshot get _brain => BrainSnapshot(
    workspaceId: 'local',
    sequence: 2,
    observedAt: DateTime.utc(2026, 8, 14),
    neurons: const [
      BrainNeuron(
        id: 'proof/source/local',
        moduleId: 'proof',
        roleId: 'source',
        scope: 'local',
        firingCount: 1,
      ),
      BrainNeuron(
        id: 'proof/assessment/local',
        moduleId: 'proof',
        roleId: 'assessment',
        scope: 'local',
        firingCount: 1,
      ),
    ],
    synapses: const [
      BrainSynapse(
        id: 'synapse-1',
        revision: 1,
        sourceNeuronId: 'proof/source/local',
        targetNeuronId: 'proof/assessment/local',
        inputContractId: 'ProofProduced@1',
        outputContractId: 'ProofProduced@1',
        status: 'live',
        usageCount: 1,
        provenanceActivityId: 'activity-1',
      ),
    ],
  );

  List<BrainJournalRecord> get _records => [
    BrainJournalRecord(
      sequence: 1,
      activityId: 'activity-1',
      neuronId: 'ui/chat/principal',
      direction: 0,
      contractId: 'Chat.UserMessage@1',
      occurredAt: DateTime.utc(2026, 8, 14),
      routeCount: 0,
      outcome: 'received',
      summary: 'Wire and run proof',
    ),
    BrainJournalRecord(
      sequence: 2,
      activityId: 'activity-1',
      neuronId: 'proof/source/local',
      direction: 1,
      contractId: 'ProofProduced@1',
      occurredAt: DateTime.utc(2026, 8, 14),
      routeCount: 1,
      outcome: 'emitted',
      summary: 'Proof produced',
    ),
  ];

  @override
  Future<List<ProductModule>> getModules() async => const [
    ProductModule(id: 'ui', displayName: 'UI', status: 'ready'),
    ProductModule(id: 'ai', displayName: 'AI', status: 'ready'),
    ProductModule(id: 'proof', displayName: 'Proof', status: 'ready'),
  ];
  @override
  Future<List<ProductOperation>> getOperations() async => const [
    ProductOperation(
      id: 'Chat.Send@1',
      moduleId: 'ui',
      displayName: 'Send chat',
      inputSchema: '{}',
      resultSchema: '{}',
    ),
  ];
  @override
  Future<ProductActivityReceipt> invoke(
    String operationId,
    Map<String, Object?> input, {
    required String idempotencyKey,
  }) async {
    invocations.add(operationId);
    return ProductActivityReceipt(
      activityId: 'activity-1',
      operationId: operationId,
    );
  }

  @override
  Stream<ProductActivity> watchActivity(
    String activityId, {
    int afterSequence = 0,
  }) async* {
    yield ProductActivity(
      activityId: activityId,
      operationId: 'Chat.Send@1',
      workspaceId: 'local',
      status: 3,
      sequence: 9,
      resultJson: jsonEncode({
        'response': 'Proof completed.',
        'tools': [
          {'operationId': 'Proof.Run@1', 'resultJson': '{}'},
        ],
      }),
    );
  }

  @override
  Future<ProductActivity> getActivity(String activityId) async =>
      throw UnimplementedError();
  @override
  Future<ChatTurnEnvelope> sendChat(
    String message, {
    required String idempotencyKey,
  }) async => throw UnimplementedError();
  @override
  Future<BrainSnapshot> getBrain() async => _brain;
  @override
  Stream<BrainSnapshot> watchBrain({int afterSequence = 0}) =>
      const Stream.empty();
  @override
  Future<BrainJournalPage> getJournal(
    String activityId, {
    int afterSequence = 0,
  }) async => BrainJournalPage(
    workspaceId: 'local',
    activityId: activityId,
    lastSequence: 2,
    records: _records,
    hasMore: false,
  );
  @override
  Stream<BrainJournalRecord> watchJournal(
    String activityId, {
    int afterSequence = 0,
  }) => Stream.fromIterable(_records);
  @override
  void close() {}
}
