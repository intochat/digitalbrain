import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:workspace/blocks/block_action.dart';
import 'package:workspace/kinds/decision_card.dart';

void main() {
  testWidgets(
    'tapping Approve invokes onAction with effect.approve.v1 by default',
    (tester) async {
      BlockAction? captured;
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: DecisionCard(
              data: const {'title': 'Ship it?', 'summary': 'Deploy to prod'},
              onAction: (action) => captured = action,
            ),
          ),
        ),
      );

      await tester.tap(find.text('Approve'));
      await tester.pump();

      expect(captured, isNotNull);
      expect(captured!.contract, 'effect.approve.v1');
    },
  );

  testWidgets('tapping Approve uses the provided approveContract', (
    tester,
  ) async {
    BlockAction? captured;
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: DecisionCard(
            data: const {
              'title': 'Ship it?',
              'summary': 'Deploy to prod',
              'approveContract': 'effect.custom.v1',
            },
            onAction: (action) => captured = action,
          ),
        ),
      ),
    );

    await tester.tap(find.text('Approve'));
    await tester.pump();

    expect(captured!.contract, 'effect.custom.v1');
  });

  testWidgets(
    'tapping Decline invokes onAction with effect.decline.v1 by default',
    (tester) async {
      BlockAction? captured;
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: DecisionCard(
              data: const {'title': 'Ship it?', 'summary': 'Deploy to prod'},
              onAction: (action) => captured = action,
            ),
          ),
        ),
      );

      await tester.tap(find.text('Decline'));
      await tester.pump();

      expect(captured!.contract, 'effect.decline.v1');
    },
  );

  testWidgets('renders title and summary', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: DecisionCard(
            data: {'title': 'Ship it?', 'summary': 'Deploy to prod'},
          ),
        ),
      ),
    );

    expect(find.text('Ship it?'), findsOneWidget);
    expect(find.text('Deploy to prod'), findsOneWidget);
  });
}
