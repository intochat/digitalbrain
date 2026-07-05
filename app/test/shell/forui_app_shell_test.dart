import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';

import 'package:digitalbrain_flutter/shell/forui_app_shell.dart';

void main() {
  group('ShellChatComposer', () {
    testWidgets('shows an enabled attach button', (tester) async {
      var attached = false;
      final controller = TextEditingController();

      await tester.pumpWidget(
        MaterialApp(
          home: FTheme(
            data: FThemes.neutral.light.touch,
            child: FScaffold(
              child: ShellChatComposer(
                controller: controller,
                sending: false,
                onSend: () {},
                onAttachFiles: () => attached = true,
                voiceInput: const SizedBox.shrink(),
              ),
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(shellComposerAttachButtonKey));
      await tester.pump(const Duration(milliseconds: 150));

      expect(attached, isTrue);
    });
  });
}
