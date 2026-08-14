import 'package:digitalbrain_corev2/digitalbrain_corev2.dart';
import 'package:digitalbrain_corev2_shell/main.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('shell discovers a module and completes its operation', (
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
    expect(find.text('Run durable proof'), findsOneWidget);

    await tester.tap(find.byKey(const Key('invoke-operation')));
    await tester.pumpAndSettle();
    await tester.drag(find.byType(ListView), const Offset(0, -500));
    await tester.pumpAndSettle();

    expect(api.invocations, 1);
    expect(find.text('Activity Completed'), findsOneWidget);
    expect(find.text('{"route":"proof/hello"}'), findsOneWidget);
  });
}

final class _FakeProductApi implements DigitalBrainProductApi {
  int invocations = 0;

  @override
  Future<List<ProductModule>> getModules() async => const [
    ProductModule(id: 'proof', displayName: 'Proof', status: 0),
  ];

  @override
  Future<List<ProductOperation>> getOperations() async => const [
    ProductOperation(
      id: 'proof/run@1',
      moduleId: 'proof',
      displayName: 'Run durable proof',
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
    invocations++;
    return const ProductActivityReceipt(
      activity: 'activity-1',
      operationId: 'proof/run@1',
    );
  }

  @override
  Stream<ProductActivity> watchActivity(
    String activityId, {
    int afterSequence = 0,
  }) async* {
    yield await getActivity(activityId);
  }

  @override
  Future<ProductActivity> getActivity(String activityId) async =>
      const ProductActivity(
        activity: 'activity-1',
        operationId: 'proof/run@1',
        workspace: 'local',
        status: 2,
        sequence: 3,
        resultJson: '{"route":"proof/hello"}',
      );

  @override
  void close() {}
}
