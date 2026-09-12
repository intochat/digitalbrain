import 'package:flutter/material.dart';
import 'package:graphic/graphic.dart';

import '../../models/ui_part.dart';
import '../../theme/ui_theme.dart';

/// Product chart control. Same widget for surface galleries and chat bubbles.
///
/// `chartKind` picks bars (`bar`, the default) or a line with points (`line`).
/// Hover or tap a point for a tooltip with the full label and value; long
/// category labels are shortened on the axis only.
final class UiChart extends StatelessWidget {
  const UiChart({super.key, required this.part, this.height = 200});

  final UiChartPart part;
  final double height;

  static const axisLabelLength = 12;

  static String axisLabel(String label) => label.length <= axisLabelLength
      ? label
      : '${label.substring(0, axisLabelLength - 1)}…';

  @override
  Widget build(BuildContext context) {
    final data = [
      for (final p in part.points)
        {'label': p.label, 'name': p.label, 'value': p.value},
    ];
    final maxValue = part.points.isEmpty
        ? 1.0
        : part.points
              .map((p) => p.value.toDouble())
              .reduce((a, b) => a > b ? a : b);
    final line = part.chartKind == 'line';

    return DecoratedBox(
      key: Key('ui_chart_${part.title}'),
      decoration: BoxDecoration(
        color: UiPalette.surfaceRaised,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: UiPalette.line),
      ),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(12, 12, 12, 8),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(part.title, style: UiType.title),
            const SizedBox(height: 10),
            SizedBox(
              height: height,
              child: data.isEmpty
                  ? const Center(
                      child: Text('No series', style: UiType.bodyMuted),
                    )
                  : Chart(
                      data: data,
                      variables: {
                        'label': Variable(
                          accessor: (Map map) => map['label'] as String,
                          scale: OrdinalScale(formatter: axisLabel),
                        ),
                        'name': Variable(
                          accessor: (Map map) => map['name'] as String,
                        ),
                        'value': Variable(
                          accessor: (Map map) => map['value'] as num,
                          scale: LinearScale(min: 0, max: maxValue * 1.15),
                        ),
                      },
                      marks: line
                          ? [
                              LineMark(
                                position: Varset('label') * Varset('value'),
                                color: ColorEncode(value: UiPalette.signal),
                                size: SizeEncode(value: 2),
                              ),
                              PointMark(
                                position: Varset('label') * Varset('value'),
                                color: ColorEncode(value: UiPalette.signal),
                                size: SizeEncode(value: 6),
                              ),
                            ]
                          : [
                              IntervalMark(
                                position: Varset('label') * Varset('value'),
                                color: ColorEncode(value: UiPalette.signal),
                                size: SizeEncode(value: 14),
                              ),
                            ],
                      axes: [Defaults.horizontalAxis, Defaults.verticalAxis],
                      selections: {
                        'tap': PointSelection(
                          on: {GestureType.hover, GestureType.tap},
                          dim: Dim.x,
                        ),
                      },
                      tooltip: TooltipGuide(
                        variables: const ['name', 'value'],
                        followPointer: const [false, true],
                        align: Alignment.topLeft,
                        offset: const Offset(-20, -20),
                      ),
                      crosshair: CrosshairGuide(
                        followPointer: const [false, true],
                      ),
                    ),
            ),
          ],
        ),
      ),
    );
  }
}
