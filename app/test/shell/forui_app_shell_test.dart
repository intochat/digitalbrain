import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter/shell/app_session.dart';
import 'package:digitalbrain_flutter/shell/forui_app_shell.dart';

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
}
