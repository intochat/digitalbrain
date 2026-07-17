import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:workspace/kinds/conversation_view.dart';

void main() {
  testWidgets('renders each message text', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: ConversationView(
            data: {
              'messages': [
                {'text': 'Hello', 'at': '2026-07-17T00:00:00Z'},
                {'text': 'World', 'at': '2026-07-17T00:01:00Z'},
              ],
            },
          ),
        ),
      ),
    );

    expect(find.text('Hello'), findsOneWidget);
    expect(find.text('World'), findsOneWidget);
  });

  testWidgets('empty messages renders without throwing', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(body: ConversationView(data: {})),
      ),
    );

    expect(tester.takeException(), isNull);
  });
}
