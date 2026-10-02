import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/app_spec_view.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/app_document.dart';

void main() {
  for (final brightness in Brightness.values) {
    testWidgets('blocks disclose checks and exact source in $brightness', (
      tester,
    ) async {
      const document = AppDocument(
        behaviors: [
          AppBehavior(
            id: 'b',
            title: 'Research',
            description: 'Read pages',
            sourcePaths: ['behaviors/read.cs'],
            scenarioIds: ['s'],
          ),
        ],
        scenarios: [
          AppScenario(id: 's', name: 'Reads', body: 'Only observed facts.'),
        ],
      );
      await tester.pumpWidget(
        MaterialApp(
          theme: ThemeData(brightness: brightness),
          home: Scaffold(
            body: SingleChildScrollView(
              child: AppSpecView(
                spec: '',
                document: document,
                files: const {'behaviors/read.cs': 'exact revision source'},
                sourceRevision: 'abc',
              ),
            ),
          ),
        ),
      );
      expect(find.text('Read pages'), findsOneWidget);
      expect(find.text('Only observed facts.'), findsNothing);
      await tester.tap(find.text('1 scenario'));
      await tester.pumpAndSettle();
      expect(find.text('Only observed facts.'), findsOneWidget);
      await tester.tap(find.byTooltip('Options for Research'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('View source'));
      await tester.pumpAndSettle();
      expect(find.text('exact revision source'), findsOneWidget);
      expect(find.text('Revision abc'), findsOneWidget);
    });
  }
}
