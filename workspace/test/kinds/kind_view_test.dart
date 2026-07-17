import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:workspace/kinds/kind_view.dart';

void main() {
  testWidgets('unknown viewKind renders a fallback tile without throwing', (
    tester,
  ) async {
    await tester.pumpWidget(
      const MaterialApp(home: Scaffold(body: KindView('bogus', {}))),
    );

    expect(find.text('unsupported kind: bogus'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('decisionCard dispatches to DecisionCard', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: KindView('decisionCard', {
            'title': 'Ship it?',
            'summary': 'Deploy to prod',
          }),
        ),
      ),
    );

    expect(find.text('Ship it?'), findsOneWidget);
  });

  testWidgets('grantPrompt renders reasons and a Grant/Cancel pair', (
    tester,
  ) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: KindView('grantPrompt', {
            'reasons': [
              {'scope': 'calendar.read', 'reason': 'See your schedule'},
            ],
          }),
        ),
      ),
    );

    expect(find.text('calendar.read'), findsOneWidget);
    expect(find.text('Grant'), findsOneWidget);
    expect(find.text('Cancel'), findsOneWidget);
  });

  testWidgets('effectPreview renders the summary and a truncated digest', (
    tester,
  ) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: KindView('effectPreview', {
            'summary': 'Will send 3 emails',
            'payloadDigest': 'sha256:abcdefabcdefabcdefabcdefabcdef',
          }),
        ),
      ),
    );

    expect(find.text('Will send 3 emails'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
