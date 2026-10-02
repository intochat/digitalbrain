import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/behavior_authoring_view.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/app_spec_screen.dart';

Map<String, dynamic> fixture() => {
  'draft': {
    'revision': 4,
    'title': 'Research',
    'status': 4,
    'error': 'Build failed.',
    'attempts': [
      {
        'revision': 'failed-1',
        'green': false,
        'failures': 'Research saves: The report was empty.',
      },
    ],
    'document': {
      'version': 1,
      'preamble': '',
      'behaviors': [],
      'scenarios': [
        {
          'id': 's',
          'name': 'Research saves',
          'body': 'The report is saved.',
          'isLive': false,
        },
      ],
    },
  },
};
void main() {
  testWidgets(
    'published actions leave room for the app title on narrow screens',
    (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      await tester.pumpWidget(
        MaterialApp(
          home: AppSpecScreen(
            packageId: 'a/research',
            title: 'Customer research',
            onOpen: () {},
            request: (m, p, [b]) async => {
              'spec': 'Research.',
              'revision': {'revision': 'r1'},
              'canEdit': true,
            },
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(
        tester.getSize(find.text('Customer research')).width,
        greaterThan(190),
      );
      expect(find.text('Open app'), findsOneWidget);
      expect(find.text('Edit app'), findsOneWidget);
      expect(find.text('Run checks'), findsOneWidget);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets(
    'failed build diagnostics remain visible without current success',
    (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          home: BehaviorAuthoringView(
            draftId: 'd',
            initial: fixture(),
            request: (m, p, [b]) async => fixture(),
          ),
        ),
      );
      await tester.tap(find.text('Build diagnostics'));
      await tester.pumpAndSettle();
      expect(
        find.text('Research saves: The report was empty.'),
        findsOneWidget,
      );
      expect(find.textContaining('failed-1'), findsOneWidget);
      expect(find.text('Checks passed'), findsNothing);
    },
  );
  testWidgets(
    'scenario conflict reload preserves prose and merges current document',
    (tester) async {
      var view = fixture();
      var saves = 0;
      Map<String, dynamic>? saved;
      Future<dynamic> request(String m, String p, [Object? body]) async {
        if (m == 'GET') {
          return {
            'draft': {
              ...view['draft'],
              'revision': 5,
              'document': {
                ...view['draft']['document'],
                'preamble': 'Another editor changed this.',
              },
            },
          };
        }
        saves++;
        if (saves == 1) {
          throw StateError(
            'This draft changed. Reload it before saving your edits.',
          );
        }
        saved = body as Map<String, dynamic>;
        view = {
          'draft': {
            ...view['draft'],
            'revision': 6,
            'document': saved!['document'],
          },
        };
        return view;
      }

      await tester.pumpWidget(
        MaterialApp(
          home: BehaviorAuthoringView(
            draftId: 'd',
            initial: view,
            request: request,
          ),
        ),
      );
      await tester.tap(find.text('Edit scenarios'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Research saves').last);
      await tester.pumpAndSettle();
      await tester.enterText(
        find.widgetWithText(TextField, 'The report is saved.'),
        'Keep my unsaved text.',
      );
      await tester.tap(find.text('Save'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Reload latest draft'));
      await tester.pumpAndSettle();
      expect(find.text('Keep my unsaved text.'), findsOneWidget);
      await tester.tap(find.text('Save'));
      await tester.pumpAndSettle();
      expect(saved!['expectedRevision'], 5);
      expect(saved!['document']['preamble'], 'Another editor changed this.');
      expect(
        saved!['document']['scenarios'][0]['body'],
        'Keep my unsaved text.',
      );
    },
  );
}
