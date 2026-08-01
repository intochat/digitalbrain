import 'package:digitalbrain_flutter_shell/behaviors/behavior_revisions.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/shell_test_support.dart';

void main() {
  testWidgets('revisions list active/prior and restore control', (tester) async {
    await prepareShellSurface(tester);
    var restored = false;
    await tester.pumpWidget(
      MaterialApp(
        home: BehaviorRevisionsView(
          document: shellBehaviorDocument(withPrior: true),
          onRestorePrior: () => restored = true,
        ),
      ),
    );

    expect(find.byKey(const Key('behavior_revisions')), findsOneWidget);
    expect(find.text('active'), findsOneWidget);
    expect(find.text('prior'), findsOneWidget);
    await tester.tap(find.byKey(const Key('behavior_restore_prior')));
    expect(restored, isTrue);
  });
}
