import 'package:digitalbrain_flutter_shell/behaviors/behavior_source.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/shell_test_support.dart';

void main() {
  testWidgets('source shows evidence and read-only overview separate from authored files', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: BehaviorSourceView(document: shellBehaviorDocument()),
        ),
      ),
    );

    expect(find.byKey(const Key('behavior_source')), findsOneWidget);
    expect(find.byKey(const Key('behavior_evidence')), findsOneWidget);
    expect(find.textContaining('Generated overview'), findsWidgets);
    expect(find.byKey(const Key('behavior_program_source')), findsOneWidget);
    expect(find.byKey(const Key('behavior_feature_source')), findsOneWidget);
  });
}
