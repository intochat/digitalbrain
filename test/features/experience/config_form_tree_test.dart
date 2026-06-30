import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';

import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';
import 'package:digitalbrain_flutter/features/experience/experience_hop_view.dart';

// Mirrors the tree structure emitted by ConfigFormSurface.Build() on the backend:
//   ui:Screen -> ui:Column -> [ui:TextField (secret token), ui:Select (provider choice),
//                              ui:TextField (secret key), ui:Button (Save/ConfigurationProvided)]
//
// Props match Configuration.cs: 'name'/'key' for field identity, 'label' for display,
// 'secret' flag for secrets, 'items' for choices, 'synapseType' on the button.

Map<String, Object?> _configFormTree(String packName) => {
      'Type': 'ui:Screen',
      'Props': <String, Object?>{'title': '$packName configuration'},
      'Children': [
        {
          'Type': 'ui:Column',
          'Props': <String, Object?>{},
          'Children': [
            // Field 1: secret text (e.g. Telegram token)
            {
              'Type': 'ui:TextField',
              'Props': <String, Object?>{
                'label': 'Token',
                'name': 'telegram_token',
                'key': 'telegram_token',
                'secret': true,
              },
              'Children': <Object?>[],
            },
            // Field 2: choice (LLM provider)
            {
              'Type': 'ui:Select',
              'Props': <String, Object?>{
                'label': 'Provider',
                'name': 'llm_provider',
                'key': 'llm_provider',
                'items': ['openai', 'ollama'],
              },
              'Children': <Object?>[],
            },
            // Field 3: secret text (API key)
            {
              'Type': 'ui:TextField',
              'Props': <String, Object?>{
                'label': 'API Key',
                'name': 'llm_key',
                'key': 'llm_key',
                'secret': true,
              },
              'Children': <Object?>[],
            },
            // Submit button — round-trips ConfigurationProvided
            {
              'Type': 'ui:Button',
              'Props': <String, Object?>{
                'label': 'Save',
                'eventName': 'ConfigurationProvided',
                'synapseType': 'ConfigurationProvided',
                'pack': packName,
              },
              'Children': <Object?>[],
            },
          ],
        },
      ],
    };

void main() {
  group('config-form tree', () {
    const packName = 'generic-config-pack';

    testWidgets('renders three fields and Save button', (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          builder: (_, w) => FTheme(
            data: FThemes.neutral.light.touch,
            child: w!,
          ),
          home: Scaffold(
            body: ExperienceHopView(
              host: RfwRuntimeHost(),
              data: <String, Object?>{
                'activeExperience': '$packName/config',
                'surfaceId': 'surface.pack-config.$packName',
                'tree': _configFormTree(packName),
              },
              correlationId: 'surface.pack-config.$packName',
              onEvent: (name, args) {},
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();

      // FSelect renders an internal readonly FTextField as its trigger, so 3 FTextFields total:
      // index 0 = token field, index 1 = select trigger (readonly), index 2 = key field.
      expect(find.byType(FTextField), findsNWidgets(3));
      // The provider choices are in the select (visible as text items once the select is built).
      expect(find.byWidgetPredicate((w) => w is FSelect), findsOneWidget);
      // Save button rendered.
      expect(find.text('Save'), findsOneWidget);
    });

    testWidgets('submit dispatches ConfigurationProvided envelope with field values', (tester) async {
      String? capturedEventName;
      Map<String, Object?>? capturedArgs;

      await tester.pumpWidget(
        MaterialApp(
          builder: (_, w) => FTheme(
            data: FThemes.neutral.light.touch,
            child: w!,
          ),
          home: Scaffold(
            body: ExperienceHopView(
              host: RfwRuntimeHost(),
              data: <String, Object?>{
                'activeExperience': '$packName/config',
                'surfaceId': 'surface.pack-config.$packName',
                'tree': _configFormTree(packName),
              },
              correlationId: 'surface.pack-config.$packName',
              onEvent: (name, args) {
                capturedEventName = name;
                capturedArgs = args;
              },
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();

      // FSelect renders a readonly FTextField as its trigger (index 1).
      // The two editable text fields are at index 0 (token) and index 2 (key).
      final textFields = find.byType(FTextField);

      // Secret fields must obscure their text — EditableText carries the obscureText flag.
      final obscuredEditable = find.byWidgetPredicate(
        (w) => w is EditableText && w.obscureText,
      );
      // Both secret fields (token + key) should be obscured.
      expect(obscuredEditable, findsNWidgets(2));

      await tester.enterText(textFields.at(0), 'my-token');
      await tester.pump();

      // index 2 = llm_key field (index 1 is the select's readonly trigger).
      await tester.enterText(textFields.at(2), 'sk-secret');
      await tester.pump();

      // Tap Save.
      await tester.tap(find.text('Save'));
      await tester.pumpAndSettle();

      expect(capturedEventName, equals('press'));
      expect(capturedArgs, isNotNull);

      final args = capturedArgs!;
      // Top-level synapseType must be ConfigurationProvided.
      expect(args['synapseType'], equals('ConfigurationProvided'));

      // Field values are in 'props'.
      final props = args['props'] as Map<String, Object?>;
      expect(props['pack'], equals(packName));
      expect(props['telegram_token'], equals('my-token'));
      expect(props['llm_key'], equals('sk-secret'));
    });
  });
}
