import 'dart:convert';

import 'package:digitalbrain_corev2/digitalbrain_corev2.dart';
import 'package:digitalbrain_corev2_shell/main.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('shell discovers modules and completes a generic operation', (
    tester,
  ) async {
    final api = _FakeProductApi();
    await tester.pumpWidget(
      DigitalBrainShell(
        productBase: Uri.parse('http://localhost:5100'),
        api: api,
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('DigitalBrain CoreV2'), findsOneWidget);
    expect(find.text('Proof'), findsOneWidget);
    expect(find.text('Ready'), findsOneWidget);
    await tester.scrollUntilVisible(
      find.byKey(const Key('invoke-operation')).first,
      300,
      scrollable: find.byType(Scrollable).first,
    );
    expect(find.textContaining('Run durable proof'), findsOneWidget);

    await tester.tap(find.byKey(const Key('invoke-operation')));
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(
      find.text('Activity Completed'),
      300,
      scrollable: find.byType(Scrollable).first,
    );

    expect(api.invocations, ['proof/run@1']);
    expect(find.text('Activity Completed'), findsOneWidget);
    expect(find.text('{"route":"proof/hello"}'), findsOneWidget);
  });

  testWidgets('conversation surface reads and sends durable messages', (
    tester,
  ) async {
    final api = _FakeProductApi(withConversation: true);
    await tester.pumpWidget(
      DigitalBrainShell(
        productBase: Uri.parse('http://localhost:5100'),
        api: api,
      ),
    );
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(
      find.byKey(const Key('conversation-input')).first,
      300,
      scrollable: find.byType(Scrollable).first,
    );

    await tester.enterText(
      find.byKey(const Key('conversation-input')),
      'Hello durable brain',
    );
    await tester.scrollUntilVisible(
      find.byKey(const Key('conversation-send')).first,
      100,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.tap(find.byKey(const Key('conversation-send')));
    await tester.pumpAndSettle();

    expect(api.invocations, ['conversation/read@1', 'conversation/send@1']);
    expect(find.text('Hello durable brain'), findsOneWidget);
    expect(find.text('owner'), findsOneWidget);
  });
}

final class _FakeProductApi implements DigitalBrainProductApi {
  _FakeProductApi({this.withConversation = false});

  final bool withConversation;
  final List<String> invocations = [];
  final Map<String, Map<String, Object?>> _inputs = {};

  @override
  Future<List<ProductModule>> getModules() async => [
    const ProductModule(id: 'proof', displayName: 'Proof', status: 0),
    if (withConversation)
      const ProductModule(
        id: 'conversation',
        displayName: 'Conversation',
        status: 0,
      ),
  ];

  @override
  Future<List<ProductOperation>> getOperations() async => [
    const ProductOperation(
      id: 'proof/run@1',
      moduleId: 'proof',
      displayName: 'Run durable proof',
      inputSchema: '{}',
      resultSchema: '{}',
    ),
    if (withConversation) ...const [
      ProductOperation(
        id: 'conversation/read@1',
        moduleId: 'conversation',
        displayName: 'Read conversation',
        inputSchema: '{}',
        resultSchema: '{}',
      ),
      ProductOperation(
        id: 'conversation/send@1',
        moduleId: 'conversation',
        displayName: 'Send conversation message',
        inputSchema: '{}',
        resultSchema: '{}',
      ),
    ],
  ];

  @override
  Future<ProductActivityReceipt> invoke(
    String operationId,
    Map<String, Object?> input, {
    required String idempotencyKey,
  }) async {
    invocations.add(operationId);
    final activity = 'activity-${invocations.length}';
    _inputs[activity] = {'operationId': operationId, ...input};
    return ProductActivityReceipt(activity: activity, operationId: operationId);
  }

  @override
  Stream<ProductActivity> watchActivity(
    String activityId, {
    int afterSequence = 0,
  }) async* {
    yield await getActivity(activityId);
  }

  @override
  Future<ProductActivity> getActivity(String activityId) async {
    final input = _inputs[activityId]!;
    final operation = input['operationId']! as String;
    final result = switch (operation) {
      'proof/run@1' => {'route': 'proof/hello'},
      'conversation/read@1' => {
        'conversationId': 'main',
        'messages': <Object?>[],
      },
      'conversation/send@1' => {
        'conversationId': 'main',
        'messages': [
          {
            'sequence': 1,
            'role': 'user',
            'text': input['message'],
            'principal': 'owner',
          },
        ],
      },
      _ => throw StateError('Unexpected operation $operation'),
    };
    return ProductActivity(
      activity: activityId,
      operationId: operation,
      workspace: 'local',
      status: 2,
      sequence: 3,
      resultJson: jsonEncode(result),
    );
  }

  @override
  void close() {}
}
