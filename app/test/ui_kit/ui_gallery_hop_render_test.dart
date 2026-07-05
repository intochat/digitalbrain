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

// Reproduces the real ui-gallery "inputs" hop layout directly from ui_kit widgets: a UiKitScreen
// containing a UiKitSidebar (full-height nav rail) plus a heading, many panels, and a button. The
// sidebar must not be stacked into the screen's vertical column (where it gets unbounded height and
// blanks the view), and the many panels must scroll.

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
    // "Inputs" renders both as the heading and as a sidebar nav item — proves the sidebar laid out too.
    expect(find.text('Inputs'), findsWidgets);
    expect(find.text('Display'), findsWidgets); // sidebar nav item
    expect(
      find.text('Next: Display'),
      findsOneWidget,
    ); // the trailing button is built (scrollable content)
  });
}
