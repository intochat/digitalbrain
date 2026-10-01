import 'package:digitalbrain_flutter_shell/workspace/compute_usage_history.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('saved preview and app receipt merge without losing settlement', () {
    final store = WorkspaceStore(persistence: MemoryWorkspacePersistence());
    store.currentConversation.messages.addAll([
      {
        'id': 'operation',
        'role': 'ui',
        'card': {
          'kind': 'charge-receipt',
          'intentId': 'usage',
          'chargedCompute': 2,
        },
      },
      {
        'id': 'run/receipt',
        'role': 'receipt',
        'receipt': <String, dynamic>{
          'id': 'usage',
          'compute': .4,
          'shadow': true,
        },
      },
    ]);
    final item = savedComputeHistory(store.currentProject).single;
    expect(item.id, 'usage');
    expect(item.previewCompute, .4);
    expect(item.settledCompute, 2);
    expect(item.chargedCompute, isNull);
    (store.currentConversation.messages.last['receipt']
            as Map)['previewCompute'] =
        null;
    expect(
      savedComputeHistory(store.currentProject).single.previewCompute,
      isNull,
    );
    store.dispose();
  });
}
