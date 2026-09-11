import 'package:flutter/material.dart';

import '../../models/ui_part.dart';
import '../../theme/ui_theme.dart';
import '../sheet/ui_sheet.dart';

/// Renders a kind-bound interactive surface. Calculator is the built-in kind.
final class UiView extends StatelessWidget {
  const UiView({
    super.key,
    required this.kind,
    required this.display,
    this.phase = 'idle',
    this.onKey,
  });

  final String kind;
  final String display;
  final String phase;
  final ValueChanged<String>? onKey;

  @override
  Widget build(BuildContext context) {
    if (kind.toLowerCase() == 'calculator') {
      return _CalculatorPad(display: display, phase: phase, onKey: onKey);
    }

    if (kind.toLowerCase() == 'spreadsheet') {
      return const UiSheet(
        part: UiSheetPart(
          title: 'Sheet',
          columns: ['A', 'B'],
          rows: [
            ['', ''],
          ],
        ),
      );
    }

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Text('No UiView for kind "$kind"', style: UiType.body),
      ),
    );
  }
}

final class _CalculatorPad extends StatelessWidget {
  const _CalculatorPad({
    required this.display,
    required this.phase,
    this.onKey,
  });

  final String display;
  final String phase;
  final ValueChanged<String>? onKey;

  static const _keys = <List<String>>[
    ['C', 'CE', 'BS', '÷'],
    ['7', '8', '9', '×'],
    ['4', '5', '6', '-'],
    ['1', '2', '3', '+'],
    ['0', '.', '='],
  ];

  @override
  Widget build(BuildContext context) {
    return Material(
      color: UiPalette.surface,
      borderRadius: BorderRadius.circular(16),
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('calculator · $phase', style: UiType.meta),
            const SizedBox(height: 8),
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 16),
              decoration: BoxDecoration(
                color: UiPalette.surfaceRaised,
                borderRadius: BorderRadius.circular(12),
                border: Border.all(color: UiPalette.line),
              ),
              alignment: Alignment.centerRight,
              child: Text(
                display,
                key: const Key('ui_view_display'),
                style: UiType.title.copyWith(fontSize: 28),
              ),
            ),
            const SizedBox(height: 12),
            for (final row in _keys) ...[
              Row(
                children: [
                  for (final key in row)
                    Expanded(
                      flex: key == '0' ? 2 : 1,
                      child: Padding(
                        padding: const EdgeInsets.all(4),
                        child: FilledButton(
                          key: Key('ui_view_key_$key'),
                          onPressed: onKey == null ? null : () => onKey!(key),
                          style: FilledButton.styleFrom(
                            backgroundColor: _isOp(key)
                                ? UiPalette.signal
                                : UiPalette.surfaceRaised,
                            foregroundColor: UiPalette.textPrimary,
                            padding: const EdgeInsets.symmetric(vertical: 16),
                          ),
                          child: Text(key, style: UiType.metaStrong),
                        ),
                      ),
                    ),
                ],
              ),
            ],
          ],
        ),
      ),
    );
  }

  static bool _isOp(String key) =>
      key == '+' ||
      key == '-' ||
      key == '×' ||
      key == '÷' ||
      key == '=' ||
      key == 'C' ||
      key == 'CE' ||
      key == 'BS';
}
