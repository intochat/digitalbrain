import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_alert.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_progress.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_spinner.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_tooltip.dart';

Widget _host(Widget child) => MaterialApp(
      home: FTheme(data: FThemes.neutral.light.touch, child: FScaffold(child: child)),
    );

void main() {
  testWidgets('Alert shows title and subtitle', (tester) async {
    await tester.pumpWidget(_host(const UiKitAlert(title: 'Heads up', subtitle: 'details')));
    expect(find.text('Heads up'), findsOneWidget);
    expect(find.text('details'), findsOneWidget);
  });

  testWidgets('Progress renders with value', (tester) async {
    await tester.pumpWidget(_host(const UiKitProgress(value: 0.5)));
    expect(find.byType(UiKitProgress), findsOneWidget);
  });

  testWidgets('Spinner renders', (tester) async {
    await tester.pumpWidget(_host(const UiKitSpinner()));
    expect(find.byType(UiKitSpinner), findsOneWidget);
  });

  testWidgets('Tooltip wraps its child', (tester) async {
    await tester.pumpWidget(_host(UiKitTooltip(tip: 'hint', child: const Text('hover me'))));
    expect(find.text('hover me'), findsOneWidget);
  });
}
