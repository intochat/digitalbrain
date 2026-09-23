import 'package:digitalbrain_flutter_shell/workspace/mydata/grant_prompt.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('asks once or always and reports the choice', (tester) async {
    final decisions = <GrantDecision>[];
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: GrantPrompt(
            appName: 'Image Editor',
            fieldLabel: 'date of birth',
            onDecision: decisions.add,
          ),
        ),
      ),
    );

    expect(
      find.text('Allow Image Editor to read date of birth?'),
      findsOneWidget,
    );

    await tester.tap(find.text('Once'));
    await tester.tap(find.text('Always'));
    await tester.tap(find.text('Not now'));

    expect(decisions, [GrantDecision.once, GrantDecision.always, GrantDecision.deny]);
  });
}