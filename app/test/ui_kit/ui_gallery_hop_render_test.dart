import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';

import 'package:digitalbrain_flutter/ui_kit/ui_screen.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_sidebar.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_heading.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_panel.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_text.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_text_field.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_button.dart';

void _noop(String name, Map<String, Object?> args) {}

void main() {
  testWidgets('sidebar + many panels render together without a layout error', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        builder: (_, child) =>
            FTheme(data: FThemes.neutral.dark.desktop, child: child!),
        home: Scaffold(
          body: UiKitScreen(
            children: [
              UiKitSidebar(
                pack: 'ui-gallery',
                experienceId: 'ui-gallery',
                items: const [
                  {'label': 'Inputs', 'eventName': 'inputs'},
                  {'label': 'Display', 'eventName': 'display'},
                  {'label': 'Feedback', 'eventName': 'feedback'},
                ],
                onEvent: _noop,
              ),
              const UiKitHeading(text: 'Inputs'),
              for (var i = 0; i < 8; i++)
                UiKitPanel(
                  children: const [
                    UiKitText(text: 'TextField'),
                    UiKitTextField(name: 'name', placeholder: 'Your name'),
                  ],
                ),
              UiKitButton(
                label: 'Next: Display',
                pack: 'ui-gallery',
                experienceId: 'ui-gallery',
                eventName: 'display',
                onEvent: _noop,
              ),
            ],
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(tester.takeException(), isNull);

    expect(find.text('Inputs'), findsWidgets);
    expect(find.text('Display'), findsWidgets);
    expect(find.text('Next: Display'), findsOneWidget);
  });
}
