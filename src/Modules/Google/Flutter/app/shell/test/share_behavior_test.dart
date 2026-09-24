import 'package:digitalbrain_flutter_shell/workspace/behaviors/behavior_manager.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

Map<String, dynamic> detail({
  required String checkStatus,
  int checkRevision = 1,
}) => {
  'description': {
    'id': 'daily_digest',
    'revision': 1,
    'name': 'Daily digest',
    'purpose': 'Summarizes the day',
    'triggers': <String>[],
    'effects': <String>[],
  },
  'program': {
    'revision': 0,
    'state': 'Stopped',
    'ready': false,
    'desiredState': 'Stopped',
    'deployments': <Object>[],
  },
  'draft': {'revision': 1, 'source': 'source', 'tests': 'tests'},
  'check': {
    'revision': checkRevision,
    'status': checkStatus,
    'artifact': {'id': 'artifact'},
  },
  'canDeploy': true,
  'allowActivation': true,
};

void main() {
  testWidgets(
    'a checked automation is shared as a package under a chosen name',
    (tester) async {
      final calls = <String, Map<String, Object?>?>{};
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: BehaviorManager(
              request: (path, {body}) async {
                calls[path] = body;
                if (path.isEmpty) {
                  return {
                    'items': [detail(checkStatus: 'Passed')],
                  };
                }
                if (path == 'daily_digest/share') {
                  return {
                    'id': {'owner': 'alice', 'name': body!['name']},
                    'published': 'revision',
                  };
                }
                return detail(checkStatus: 'Passed');
              },
              onAsk: (_, _) {},
              onSelected: (_) {},
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('Daily digest').first);
      await tester.pumpAndSettle();

      await tester.tap(find.text('Share as package'));
      await tester.pumpAndSettle();
      expect(find.widgetWithText(TextField, 'daily-digest'), findsOneWidget);
      await tester.enterText(
        find.byKey(const ValueKey('share-package-name')),
        'digest',
      );
      await tester.tap(find.text('Share'));
      await tester.pumpAndSettle();

      expect(calls['daily_digest/share'], {'name': 'digest'});
      expect(find.textContaining('Shared as alice/digest'), findsOneWidget);
    },
  );

  testWidgets('only a draft that passed its latest check can be shared', (
    tester,
  ) async {
    Future<void> show(Map<String, dynamic> data) async {
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: BehaviorManager(
              request: (path, {body}) async => path.isEmpty
                  ? {
                      'items': [data],
                    }
                  : data,
              onAsk: (_, _) {},
              onSelected: (_) {},
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('Daily digest').first);
      await tester.pumpAndSettle();
    }

    await show(detail(checkStatus: 'Failed'));
    expect(find.text('Share as package'), findsNothing);
    await show(detail(checkStatus: 'Passed', checkRevision: 0));
    expect(find.text('Share as package'), findsNothing);
  });
}
