import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:workspace/blocks/block_action.dart';
import 'package:workspace/kinds/grant_prompt.dart';

void main() {
  testWidgets(
    'tapping Grant invokes onAction with effect.approve.v1 by default',
    (tester) async {
      BlockAction? captured;
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: GrantPrompt(
              data: const {
                'reasons': [
                  {'scope': 'calendar.read', 'reason': 'See your schedule'},
                ],
              },
              onAction: (action) => captured = action,
            ),
          ),
        ),
      );

      await tester.tap(find.text('Grant'));
      await tester.pump();

      expect(captured, isNotNull);
      expect(captured!.contract, 'effect.approve.v1');
    },
  );

  testWidgets('tapping Grant uses the provided grantContract', (tester) async {
    BlockAction? captured;
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: GrantPrompt(
            data: const {
              'reasons': [
                {'scope': 'calendar.read', 'reason': 'See your schedule'},
              ],
              'grantContract': 'effect.custom.v1',
            },
            onAction: (action) => captured = action,
          ),
        ),
      ),
    );

    await tester.tap(find.text('Grant'));
    await tester.pump();

    expect(captured!.contract, 'effect.custom.v1');
  });

  testWidgets(
    'tapping Cancel invokes onAction with effect.decline.v1 by default',
    (tester) async {
      BlockAction? captured;
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: GrantPrompt(
              data: const {
                'reasons': [
                  {'scope': 'calendar.read', 'reason': 'See your schedule'},
                ],
              },
              onAction: (action) => captured = action,
            ),
          ),
        ),
      );

      await tester.tap(find.text('Cancel'));
      await tester.pump();

      expect(captured!.contract, 'effect.decline.v1');
    },
  );

  testWidgets('renders each reason scope and explanation', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: GrantPrompt(
            data: {
              'reasons': [
                {'scope': 'calendar.read', 'reason': 'See your schedule'},
              ],
            },
          ),
        ),
      ),
    );

    expect(find.text('calendar.read'), findsOneWidget);
    expect(find.text('See your schedule'), findsOneWidget);
  });
}
