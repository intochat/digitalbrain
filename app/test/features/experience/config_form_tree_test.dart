import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';
import 'package:rfw/rfw.dart' show RemoteEventHandler;

import 'package:digitalbrain_flutter/ui_kit/ui_screen.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_text_field.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_select.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_button.dart';

// Mirrors the field shape ConfigFormSurface.Build() emits on the backend (see Configuration.cs): two
// secret text fields (token/key) + a provider select + a Save button that captures the form's values
// and fires ConfigurationProvided. Built directly from ui_kit widgets (no RFW tree, no
// ExperienceHopView) so this exercises exactly the form-capture contract ui_kit itself promises.

const _packName = 'generic-config-pack';

Widget _configForm({required RemoteEventHandler onEvent}) => MaterialApp(
  builder: (_, w) => FTheme(data: FThemes.neutral.light.touch, child: w!),
  home: Scaffold(
    body: UiKitScreen(
      children: [
        const UiKitTextField(
          name: 'telegram_token',
          placeholder: 'Token',
          secret: true,
        ),
        const UiKitSelect(
          name: 'llm_provider',
          label: 'Provider',
          options: ['openai', 'ollama'],
        ),
        const UiKitTextField(
          name: 'llm_key',
          placeholder: 'API Key',
          secret: true,
        ),
        UiKitButton(
          label: 'Save',
          pack: _packName,
          experienceId: '',
          eventName: '',
          synapseType: 'ConfigurationProvided',
          onEvent: onEvent,
        ),
      ],
    ),
  ),
);

void main() {
  group('config-form ui_kit composition', () {
    testWidgets('renders both text fields, the select, and the Save button', (
      tester,
    ) async {
      await tester.pumpWidget(_configForm(onEvent: (_, _) {}));
      await tester.pumpAndSettle();

      expect(
        find.byType(FTextField),
        findsNWidgets(3),
      ); // 2 editable + FSelect's own readonly trigger
      expect(find.byWidgetPredicate((w) => w is FSelect), findsOneWidget);
      expect(find.text('Save'), findsOneWidget);
    });

    testWidgets(
      'Save captures both field values and fires ConfigurationProvided',
      (tester) async {
        String? capturedEventName;
        Map<String, Object?>? capturedArgs;

        await tester.pumpWidget(
          _configForm(
            onEvent: (name, args) {
              capturedEventName = name;
              capturedArgs = args;
            },
          ),
        );
        await tester.pumpAndSettle();

        final textFields = find.byType(FTextField);
        final obscuredEditable = find.byWidgetPredicate(
          (w) => w is EditableText && w.obscureText,
        );
        expect(obscuredEditable, findsNWidgets(2));

        await tester.enterText(textFields.at(0), 'my-token');
        await tester.pump();
        await tester.enterText(textFields.at(2), 'sk-secret');
        await tester.pump();

        await tester.tap(find.text('Save'));
        await tester.pumpAndSettle();

        expect(capturedEventName, equals('press'));
        expect(capturedArgs?['synapseType'], equals('ConfigurationProvided'));
        final props = capturedArgs!['props'] as Map<String, Object?>;
        expect(props['pack'], equals(_packName));
        expect(props['telegram_token'], equals('my-token'));
        expect(props['llm_key'], equals('sk-secret'));
      },
    );
  });
}
