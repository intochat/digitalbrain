import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/behavior_authoring_view.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/app_spec_screen.dart';

void main() {
  testWidgets(
    'published blocks verify the displayed revision and show their source',
    (tester) async {
      final calls = <String>[];
      Future<dynamic> request(
        String method,
        String path, [
        Object? body,
      ]) async {
        calls.add('$method $path');
        return {
          'revision': {
            'package': {'owner': 'a', 'name': 'app'},
            'revision': 'old',
          },
          'spec': '',
          'files': {'behaviors/open.cs': 'old source'},
          'documentReadResult': {
            'document': {
              'version': 1,
              'preamble': '',
              'behaviors': [
                {
                  'id': 'b',
                  'title': 'Open',
                  'description': 'Draw UI',
                  'sourcePaths': ['behaviors/open.cs'],
                  'scenarioIds': [],
                },
              ],
              'scenarios': [],
            },
          },
        };
      }

      await tester.pumpWidget(
        MaterialApp(
          home: AppSpecScreen(
            packageId: 'a/app',
            title: 'App',
            request: request,
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('Draw UI'), findsOneWidget);
      await tester.tap(find.byKey(const ValueKey('verify-app')));
      await tester.pumpAndSettle();
      expect(calls.last, 'POST /packages/a/app/verify?revision=old');
    },
  );

  testWidgets(
    'behavior edits send a revision-bound document and preserve other blocks',
    (tester) async {
      var view = <String, dynamic>{
        'draft': {
          'revision': 4,
          'title': 'App',
          'status': 1,
          'document': {
            'version': 1,
            'preamble': '',
            'behaviors': [
              {
                'id': 'b',
                'title': 'Open',
                'description': 'Draw UI',
                'sourcePaths': [],
                'scenarioIds': [],
              },
            ],
            'scenarios': [],
          },
        },
      };
      Map<String, dynamic>? saved;
      Future<dynamic> request(
        String method,
        String path, [
        Object? body,
      ]) async {
        saved = body as Map<String, dynamic>;
        view = {
          'draft': {
            'revision': 5,
            'title': 'App',
            'status': 1,
            'document': saved!['document'],
          },
        };
        return view;
      }

      await tester.pumpWidget(
        MaterialApp(
          home: BehaviorAuthoringView(
            draftId: 'd',
            request: request,
            initial: view,
          ),
        ),
      );
      await tester.tap(find.byTooltip('Options for Open'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Edit description'));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.byKey(const ValueKey('behavior-description')),
        'Draw a small UI',
      );
      await tester.tap(find.text('Save'));
      await tester.pumpAndSettle();
      expect(saved!['expectedRevision'], 4);
      expect(find.text('Draw a small UI'), findsOneWidget);
      expect(find.text('Build and publish'), findsOneWidget);
    },
  );
}
