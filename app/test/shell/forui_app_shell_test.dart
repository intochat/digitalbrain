import 'dart:typed_data';

import 'package:cross_file/cross_file.dart';
import 'package:desktop_drop/desktop_drop.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';
import 'package:digitalbrain_flutter/shell/app_session.dart';
import 'package:digitalbrain_flutter/shell/forui_app_shell.dart';

Widget _host(Widget child) => MaterialApp(
  home: FTheme(
    data: FThemes.neutral.light.touch,
    child: FScaffold(child: child),
  ),
);

void main() {
  test('app client id is stable across shell surfaces', () {
    expect(digitalBrainAppClientId, 'flutter');
  });

  group('autoSwitchTargetForKind', () {
    test('pack-config-form triggers an auto-switch to itself', () {
      expect(
        autoSwitchTargetForKind('pack-config-form'),
        equals('pack-config-form'),
      );
    });

    test('unrelated kinds do not trigger an auto-switch', () {
      expect(autoSwitchTargetForKind('toast'), isNull);
      expect(autoSwitchTargetForKind('installed-bundles'), isNull);
      expect(autoSwitchTargetForKind('marketplace-list'), isNull);
      expect(autoSwitchTargetForKind(''), isNull);
    });
  });

  group('classifySurface', () {
    test('assistant widget-tree is a chat surface, not a shell', () {
      final data = <String, Object?>{
        'kind': 'widget-tree',
        'role': 'assistant',
        'tree': {'Type': 'Text', 'Props': <String, Object?>{}},
      };

      expect(isShellSurface(data), isFalse);
      expect(classifySurface(data), SurfaceDisposition.chat);
    });

    test('app shell tree is a shell surface', () {
      final data = <String, Object?>{
        'kind': 'widget-tree',
        'tree': {
          'Type': 'FScaffold',
          'Props': {'activeContent': 'chat'},
        },
      };

      expect(classifySurface(data), SurfaceDisposition.shell);
    });

    test('pack config and toast surfaces keep their own lanes', () {
      expect(
        classifySurface({'kind': 'pack-config-form'}),
        SurfaceDisposition.content,
      );
      expect(
        classifySurface({'kind': 'toast', 'message': 'Saved'}),
        SurfaceDisposition.toast,
      );
    });
  });

  group('shellChatIsSelected', () {
    test('chat route and INO target select chat body', () {
      expect(shellChatIsSelected('/chat', null), isTrue);
      expect(shellChatIsSelected('/marketplace', 'ino'), isTrue);
      expect(shellChatIsSelected('/marketplace', 'marketplace'), isFalse);
    });
  });

  group('ShellChatComposer', () {
    testWidgets('shows an enabled attach button', (tester) async {
      var attached = false;
      final controller = TextEditingController();
      addTearDown(controller.dispose);

      await tester.pumpWidget(
        _host(
          ShellChatComposer(
            controller: controller,
            sending: false,
            onSend: () {},
            onAttachFiles: () => attached = true,
            voiceInput: const SizedBox(width: 40, height: 40),
          ),
        ),
      );

      expect(find.byKey(shellComposerAttachButtonKey), findsOneWidget);

      await tester.tap(find.byKey(shellComposerAttachButtonKey));
      await tester.pump(const Duration(milliseconds: 150));

      expect(attached, isTrue);
    });

    test(
      'dropped files use the shared ingest handler and skip directories',
      () async {
        final file = XFile.fromData(
          Uint8List.fromList([1, 2, 3]),
          name: 'q2-sales.xlsx',
        );
        final directory = DropItemDirectory('C:\\workspace\\folder', []);
        final ingested = <XFile>[];

        await ingestDroppedFilesForShell([file, directory], (files) async {
          ingested.addAll(files);
        });

        expect(ingested, [file]);
      },
    );

    test('voice transcript appends to composer text', () {
      final controller = TextEditingController(text: 'show accounts');
      addTearDown(controller.dispose);

      appendTranscriptToComposer(controller, 'from Salesforce');

      expect(controller.text, 'show accounts from Salesforce');
      expect(controller.selection.baseOffset, controller.text.length);
    });
  });
}
