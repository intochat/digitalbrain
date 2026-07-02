import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter/shell/forui_app_shell.dart';

void main() {
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
}
