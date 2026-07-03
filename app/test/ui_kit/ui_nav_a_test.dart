import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_tabs.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_breadcrumb.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_pagination.dart';

Widget _host(Widget child) => MaterialApp(
      home: FTheme(data: FThemes.neutral.light.touch, child: FScaffold(child: child)),
    );

void main() {
  testWidgets('Tabs fires ExperienceStep with the tapped item eventName', (tester) async {
    Map<String, Object?>? captured;
    await tester.pumpWidget(_host(UiKitTabs(
      pack: 'p', experienceId: 'p',
      items: const [{'label': 'One', 'eventName': 'one'}, {'label': 'Two', 'eventName': 'two'}],
      onEvent: (n, a) => captured = a,
    )));
    await tester.tap(find.text('Two'));
    await tester.pumpAndSettle();
    final propsMap = captured!['props'] as Map<String, Object?>;
    expect(propsMap['eventName'], 'two');
  });

  testWidgets('Breadcrumb fires ExperienceStep with the tapped item eventName', (tester) async {
    Map<String, Object?>? captured;
    await tester.pumpWidget(_host(UiKitBreadcrumb(
      pack: 'p', experienceId: 'p',
      items: const [{'label': 'Home', 'eventName': 'home'}, {'label': 'Sub', 'eventName': 'sub'}],
      onEvent: (n, a) => captured = a,
    )));
    await tester.tap(find.text('Sub'));
    await tester.pumpAndSettle();
    final propsMap = captured!['props'] as Map<String, Object?>;
    expect(propsMap['eventName'], 'sub');
  });

  testWidgets('Pagination fires ExperienceStep with the tapped page eventName', (tester) async {
    Map<String, Object?>? captured;
    await tester.pumpWidget(_host(UiKitPagination(
      pack: 'p', experienceId: 'p',
      items: const [{'label': '1', 'eventName': 'page-0'}, {'label': '2', 'eventName': 'page-1'}, {'label': '3', 'eventName': 'page-2'}],
      onEvent: (n, a) => captured = a,
    )));
    await tester.tap(find.text('2'));
    await tester.pumpAndSettle();
    final propsMap = captured!['props'] as Map<String, Object?>;
    expect(propsMap['eventName'], 'page-1');
  });
}
