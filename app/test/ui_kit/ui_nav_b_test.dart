import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_bottom_nav.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_sidebar.dart';

Widget _host(Widget child) => MaterialApp(
      home: FTheme(data: FThemes.neutral.light.touch, child: FScaffold(child: child)),
    );

void main() {
  testWidgets('BottomNav fires ExperienceStep on item tap', (tester) async {
    Map<String, Object?>? captured;
    await tester.pumpWidget(_host(UiKitBottomNav(
      pack: 'p', experienceId: 'p',
      items: const [{'label': 'A', 'eventName': 'a'}, {'label': 'B', 'eventName': 'b'}],
      onEvent: (n, a) => captured = a,
    )));
    await tester.tap(find.text('B'));
    await tester.pumpAndSettle();
    expect((captured!['props'] as Map)['eventName'], 'b');
  });

  testWidgets('Sidebar fires ExperienceStep on item tap', (tester) async {
    Map<String, Object?>? captured;
    await tester.pumpWidget(_host(UiKitSidebar(
      pack: 'p', experienceId: 'p',
      items: const [{'label': 'Home', 'eventName': 'home'}, {'label': 'Settings', 'eventName': 'settings'}],
      onEvent: (n, a) => captured = a,
    )));
    await tester.tap(find.text('Settings'));
    await tester.pumpAndSettle();
    expect((captured!['props'] as Map)['eventName'], 'settings');
  });
}
