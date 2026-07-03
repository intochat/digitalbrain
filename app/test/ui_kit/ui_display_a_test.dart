import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_heading.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_badge.dart';

Widget _host(Widget child) => MaterialApp(
      home: FTheme(data: FThemes.neutral.light.touch, child: FScaffold(child: child)),
    );

void main() {
  testWidgets('Heading renders its text', (tester) async {
    await tester.pumpWidget(_host(const UiKitHeading(text: 'Title')));
    expect(find.text('Title'), findsOneWidget);
  });

  testWidgets('Badge renders its text', (tester) async {
    await tester.pumpWidget(_host(const UiKitBadge(text: 'New')));
    expect(find.text('New'), findsOneWidget);
  });
}
