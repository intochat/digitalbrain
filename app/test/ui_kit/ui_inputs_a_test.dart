import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_checkbox.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_switch.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_text_area.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_form_scope.dart';

Widget _host(Widget child) => MaterialApp(
      home: FTheme(data: FThemes.neutral.light.touch, child: FScaffold(child: child)),
    );

void main() {
  testWidgets('Checkbox writes "true" when checked', (tester) async {
    final c = UiKitFormController();
    await tester.pumpWidget(_host(UiKitFormScope(
      controller: c,
      child: const UiKitCheckbox(name: 'agree', label: 'I agree'),
    )));
    await tester.tap(find.byType(FCheckbox));
    await tester.pumpAndSettle();
    expect(c.values['agree'], 'true');
  });

  testWidgets('Switch writes "true" into form scope', (tester) async {
    final c = UiKitFormController();
    await tester.pumpWidget(_host(UiKitFormScope(
      controller: c,
      child: const UiKitSwitch(name: 'notify', label: 'Notify'),
    )));
    await tester.tap(find.byType(FSwitch));
    await tester.pumpAndSettle();
    expect(c.values['notify'], 'true');
  });

  testWidgets('TextArea writes its text into form scope', (tester) async {
    final c = UiKitFormController();
    await tester.pumpWidget(_host(UiKitFormScope(
      controller: c,
      child: const UiKitTextArea(name: 'bio'),
    )));
    await tester.enterText(find.byType(FTextField), 'hello');
    await tester.pump();
    expect(c.values['bio'], 'hello');
  });
}
