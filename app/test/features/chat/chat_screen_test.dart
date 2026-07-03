import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';

import 'package:digitalbrain_flutter/features/chat/chat_screen.dart';

Widget _wrap(Widget child) => MaterialApp(
  builder: (_, w) => FTheme(data: FThemes.neutral.light.touch, child: w!),
  home: Scaffold(body: child),
);

void main() {
  testWidgets('shows the kernel-unreachable banner when connecting fails', (
    tester,
  ) async {
    await tester.pumpWidget(
      _wrap(ChatScreen(debugClientFactory: () => throw Exception('no kernel'))),
    );
    await tester.pumpAndSettle();

    expect(find.textContaining('Could not reach the kernel'), findsOneWidget);
  });
}
