import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_date_field.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_form_scope.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_radio_group.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_registry.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_select.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_slider.dart';

void main() {
  test('registry maps inputs-b nodes to their covers', () {
    expect(
      buildUiNode('ui:select', {'name': 'c', 'options': const ['Red'], 'label': ''},
          const [], (a, b) {}, buildChild: (_) => const SizedBox()),
      isA<UiKitSelect>(),
    );
    expect(
      buildUiNode('ui:slider', {'name': 'l', 'min': 0.0, 'max': 10.0, 'label': ''},
          const [], (a, b) {}, buildChild: (_) => const SizedBox()),
      isA<UiKitSlider>(),
    );
    expect(
      buildUiNode('ui:radiogroup', {'name': 'r', 'options': const ['A'], 'label': ''},
          const [], (a, b) {}, buildChild: (_) => const SizedBox()),
      isA<UiKitRadioGroup>(),
    );
    expect(
      buildUiNode('ui:datefield', {'name': 'd', 'label': ''},
          const [], (a, b) {}, buildChild: (_) => const SizedBox()),
      isA<UiKitDateField>(),
    );
  });

  testWidgets('Slider renders an FSlider', (tester) async {
    await tester.pumpWidget(MaterialApp(
      home: FTheme(
        data: FThemes.neutral.light.touch,
        child: FScaffold(
          child: UiKitSlider(name: 'level', min: 0, max: 10),
        ),
      ),
    ));
    await tester.pumpAndSettle();
    expect(find.byType(FSlider), findsOneWidget);
  });

  testWidgets('RadioGroup capture: tapping option writes value to form scope', (tester) async {
    final controller = UiKitFormController();
    await tester.pumpWidget(MaterialApp(
      home: FTheme(
        data: FThemes.neutral.light.touch,
        child: FScaffold(
          child: UiKitFormScope(
            controller: controller,
            child: UiKitRadioGroup(name: 'size', options: const ['S', 'M', 'L']),
          ),
        ),
      ),
    ));
    await tester.pumpAndSettle();

    await tester.tap(find.text('M'));
    await tester.pumpAndSettle();

    expect(controller.values['size'], equals('M'));
  });

  testWidgets('Slider capture: drag writes numeric string to form scope', (tester) async {
    final controller = UiKitFormController();
    await tester.pumpWidget(MaterialApp(
      home: FTheme(
        data: FThemes.neutral.light.touch,
        child: Scaffold(
          body: SizedBox(
            width: 400,
            height: 60,
            child: UiKitFormScope(
              controller: controller,
              child: UiKitSlider(name: 'level', min: 0, max: 10),
            ),
          ),
        ),
      ),
    ));
    await tester.pumpAndSettle();

    expect(find.byType(FSlider), findsOneWidget);

    // Drag from left of the track to trigger FSlider's onEnd callback.
    final sliderTopLeft = tester.getTopLeft(find.byType(FSlider));
    final sliderSize = tester.getSize(find.byType(FSlider));
    final startPt = sliderTopLeft + Offset(sliderSize.width * 0.1, sliderSize.height / 2);
    final gesture = await tester.startGesture(startPt);
    await tester.pump(const Duration(milliseconds: 50));
    await gesture.moveBy(const Offset(80, 0));
    await tester.pump(const Duration(milliseconds: 50));
    await gesture.up();
    await tester.pump(const Duration(milliseconds: 50));

    expect(controller.values['level'], isNotNull);
    expect(double.tryParse(controller.values['level']!), isNotNull);
  });

  testWidgets('DateField capture: text entry writes ISO yyyy-MM-dd string to form scope', (tester) async {
    final controller = UiKitFormController();
    await tester.pumpWidget(MaterialApp(
      home: FTheme(
        data: FThemes.neutral.light.touch,
        child: FScaffold(
          child: UiKitFormScope(
            controller: controller,
            child: UiKitDateField(name: 'when'),
          ),
        ),
      ),
    ));
    await tester.pumpAndSettle();

    // Find the text field that is nested inside FDateField.
    final textFieldFinder = find.byType(FTextField);
    expect(textFieldFinder, findsWidgets);

    // FDateField accepts MM/DD/YYYY format via text input.
    await tester.enterText(textFieldFinder.first, '06/15/2024');
    await tester.pumpAndSettle();

    expect(controller.values['when'], equals('2024-06-15'));
  });
}
