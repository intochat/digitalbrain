import 'package:flutter/material.dart';
import 'package:graphic/graphic.dart';

import '../../models/ui_part.dart';
import '../../theme/ui_theme.dart';

/// Product chart control. Same widget for surface galleries and chat bubbles.
final class UiChart extends StatelessWidget {
  const UiChart({super.key, required this.part, this.height = 200});

  final UiChartPart part;
  final double height;

  @override
  Widget build(BuildContext context) {
    final data = [
      for (final p in part.points) {'label': p.label, 'value': p.value},
    ];
    final maxValue = part.points.isEmpty
        ? 1.0
        : part.points
              .map((p) => p.value.toDouble())
              .reduce((a, b) => a > b ? a : b);

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
                        ),
                        'value': Variable(
                          accessor: (Map map) => map['value'] as num,
                          scale: LinearScale(min: 0, max: maxValue * 1.15),
                        ),
                      },
                      marks: [
                        IntervalMark(
                          color: ColorEncode(value: UiPalette.signal),
                          size: SizeEncode(value: 14),
                        ),
                      ],
                      axes: [Defaults.horizontalAxis, Defaults.verticalAxis],
                    ),
            ),
          ],
        ),
      ),
    );
  }
}
