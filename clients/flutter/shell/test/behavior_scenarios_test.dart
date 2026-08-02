import 'package:digitalbrain_flutter_shell/behaviors/behavior_scenarios.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/shell_test_support.dart';

void main() {
  testWidgets('scenarios list titles and feature text without source code', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    await tester.pumpWidget(
      MaterialApp(
        home: BehaviorScenariosView(document: shellBehaviorDocument()),
      ),
    );

    expect(find.byKey(const Key('behavior_scenarios')), findsOneWidget);
    expect(find.text('enrich account from email'), findsOneWidget);
    expect(find.textContaining('Feature: account enrichment'), findsOneWidget);
    expect(find.textContaining('AccountEnrichmentProgram'), findsNothing);
  });
}
