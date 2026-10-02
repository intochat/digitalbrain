import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/app_document.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/app_draft_controller.dart';

void main() {
  test(
    'a conflict preserves unsaved blocks and the expected revision',
    () async {
      Map<String, dynamic>? posted;
      final controller = AppDraftController('d', (method, path, [body]) async {
        if (method == 'GET') {
          return {
            'draft': {
              'revision': 7,
              'document': {'version': 1, 'behaviors': [], 'scenarios': []},
            },
          };
        }
        posted = body as Map<String, dynamic>;
        throw StateError('This draft changed. Reload it.');
      });
      await controller.load();
      const pending = AppDocument(
        behaviors: [
          AppBehavior(id: 'new', title: 'Keep me', description: 'Unsaved'),
        ],
        scenarios: [],
      );
      await expectLater(controller.save(pending), throwsStateError);
      expect(posted!['expectedRevision'], 7);
      expect(controller.pending!.behaviors.single.title, 'Keep me');
      expect(controller.revision, 7);
      controller.dispose();
    },
  );
}
