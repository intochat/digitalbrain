import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets(
    'a declared voice primitive renders and an absent voice stays absent',
    (tester) async {
      var withVoice = false;
      Future<Map<String, dynamic>> load(String kind, String name) async =>
          kind == 'surface'
          ? {
              'children': [
                {'kind': 'text', 'name': 'greeting'},
                if (withVoice) {'kind': 'voiceinput', 'name': 'capture'},
              ],
            }
          : kind == 'text'
          ? {'markdown': 'Hello'}
          : {'label': 'Speak here'};
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: NeuronView(
              kind: 'surface',
              name: 'root',
              load: load,
              onAction: (_) async {},
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.byType(UiVoiceInput), findsNothing);
      withVoice = true;
      await tester.pump(const Duration(seconds: 2));
      await tester.pumpAndSettle();
      expect(find.byType(UiVoiceInput), findsOneWidget);
      expect(find.byTooltip('Speak here'), findsOneWidget);
      await tester.pumpWidget(const SizedBox());
    },
  );

  testWidgets('tab child composition preserves explicit voice input', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: NeuronView(
            kind: 'tabs',
            name: 'tabs',
            load: (kind, _) async => kind == 'tabs'
                ? {
                    'selectedId': 'voice',
                    'tabs': [
                      {
                        'id': 'voice',
                        'title': 'Capture',
                        'child': {'kind': 'voiceinput', 'name': 'record'},
                      },
                    ],
                  }
                : {'label': 'Record'},
            voiceBuilder: (_, state) => Text('Declared ${state['label']}'),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Declared Record'), findsOneWidget);
    await tester.pumpWidget(const SizedBox());
  });
}
