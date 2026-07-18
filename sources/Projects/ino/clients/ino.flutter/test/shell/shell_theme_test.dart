import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/screens/shell/shell_theme.dart';

void main() {
  group('InoShellTheme palette', () {
    test('ink-0 matches prototype #0A0E14', () {
      expect(InoShellTheme.ink0.value, 0xFF0A0E14);
    });

    test('cyan == neuron color #3DDCFF', () {
      expect(InoShellTheme.cyan.value, 0xFF3DDCFF);
    });

    test('indigo == synapse color #7C8AFF', () {
      expect(InoShellTheme.indigo.value, 0xFF7C8AFF);
    });

    test('gold == recall color #E8C56A reserved for memory feedback', () {
      expect(InoShellTheme.gold.value, 0xFFE8C56A);
    });
  });

  group('InoShellTheme motion', () {
    test('ease curve matches cubic-bezier(0.22, 1, 0.36, 1)', () {
      expect(InoShellTheme.easeOut, const Cubic(0.22, 1, 0.36, 1));
    });

    test('idle-pulse beat is 4.8 seconds', () {
      expect(InoShellTheme.brainIdleBeat, const Duration(milliseconds: 4800));
    });
  });

  group('InoShellTheme latency budgets', () {
    test('utterance to first comet ≤ 400ms', () {
      expect(InoShellTheme.utteranceToFirstCometBudget,
          const Duration(milliseconds: 400));
    });

    test('to first card ≤ 2.5s', () {
      expect(InoShellTheme.toFirstCardBudget,
          const Duration(milliseconds: 2500));
    });

    test('to complete plan ≤ 6s', () {
      expect(InoShellTheme.toCompletePlanBudget,
          const Duration(seconds: 6));
    });
  });
}
