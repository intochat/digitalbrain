import 'package:flutter/material.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/app_verification_summary.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/apps_screen.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/app_document.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/app_spec_view.dart';

void main() {
  test('repeated scenario duplication chooses the first unused copy name', () {
    const original = AppScenario(id: 'a', name: 'Open', body: 'Draw UI.');
    const document = AppDocument(behaviors: [], scenarios: [original]);
    final first = document.duplicateScenario(original, 'first');
    final second = first.duplicateScenario(original, 'second');
    expect(first.scenarios.last.name, 'Open copy');
    expect(second.scenarios.last.name, 'Open copy 2');
    final collided = second.withScenarios([
      ...second.scenarios,
      const AppScenario(id: 'existing', name: 'Open copy 3', body: ''),
      const AppScenario(id: 'later', name: 'Open copy 5', body: ''),
    ]);
    final next = collided.duplicateScenario(original, 'next');
    expect(next.scenarios.last.name, 'Open copy 4');
    expect(next.scenarios.last.id, 'next');
    expect(next.scenarios.last.body, original.body);
  });
  test(
    'scenario edits retain IDs and duplication preserves source bindings',
    () {
      const a = AppScenario(id: 'a', name: 'One', body: 'When clicked, act.');
      const b = AppScenario(id: 'b', name: 'Two', body: 'When started, draw.');
      const doc = AppDocument(
        behaviors: [
          AppBehavior(
            id: 'ui',
            title: 'UI',
            description: '',
            scenarioIds: ['a'],
            sourcePaths: ['ui.cs'],
          ),
        ],
        scenarios: [a, b],
      );
      expect(doc.moveScenario(b, -1).scenarios.map((s) => s.id), ['b', 'a']);
      final copied = doc.duplicateScenario(a, 'copy');
      expect(copied.behaviors.single.scenarioIds, ['a', 'copy']);
      expect(copied.withScenarios([b]).behaviors.single.scenarioIds, isEmpty);
    },
  );
  test('stable verdict IDs survive rename and cannot attach to a different scenario', () {
    const scenario = AppScenario(id: 'a', name: 'Renamed', body: '');
    final run = VerificationRun.fromJson({
      'exitCode': 0,
      'scenarios': [
        {'scenarioId': 'a', 'name': 'Old name', 'passed': true},
      ],
    });
    expect(scenarioStatus(scenario, [scenario], run, true), 'Passed');
    const other = AppScenario(id: 'b', name: 'Old name', body: '');
    expect(scenarioStatus(other, [other], run, true), 'Not run');
  });
  testWidgets(
    'marketplace distinguishes installed apps and searches owner/name',
    (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          home: AppsScreen(
            workspaceId: 'brain',
            onClose: () {},
            request: (method, path, [body]) async {
              if (path == '/packages') {
                return [
                  {
                    'package': {'owner': 'alice', 'name': 'notes'},
                    'title': 'Notes',
                    'revision': 'r1',
                  },
                  {
                    'package': {'owner': 'bob', 'name': 'notes'},
                    'title': 'Notes',
                    'revision': 'r1',
                  },
                ];
              }
              if (path == '/packages/drafts') return [];
              return {
                'app': {
                  'status': path.contains('alice') ? 1 : 0,
                  'revision': {'revision': 'r1'},
                },
              };
            },
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('Installed apps'), findsOneWidget);
      expect(find.text('Published catalog'), findsOneWidget);
      await tester.enterText(
        find.byKey(const ValueKey('search-apps')),
        'bob/notes',
      );
      await tester.pump();
      expect(find.byKey(const ValueKey('package-alice/notes')), findsNothing);
      expect(find.byKey(const ValueKey('install-bob/notes')), findsOneWidget);
    },
  );
  testWidgets(
    'linked scenarios are standalone visible blocks with source links',
    (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: AppSpecView(
              spec: '',
              document: const AppDocument(
                behaviors: [
                  AppBehavior(
                    id: 'ui',
                    title: 'UI',
                    description: 'Draw UI',
                    sourcePaths: ['behaviors/ui.cs'],
                    scenarioIds: ['started'],
                  ),
                ],
                scenarios: [
                  AppScenario(
                    id: 'started',
                    name: 'Open',
                    body: 'When AppStarted, draw UI.',
                  ),
                ],
              ),
              files: const {'behaviors/ui.cs': 'draw();'},
            ),
          ),
        ),
      );
      expect(find.text('Scenario: Open'), findsOneWidget);
      expect(
        find.byKey(const ValueKey('scenario-source-started')),
        findsOneWidget,
      );
      await tester.tap(find.byKey(const ValueKey('scenario-source-started')));
      await tester.pumpAndSettle();
      expect(find.text('behaviors/ui.cs'), findsOneWidget);
    },
  );
}
