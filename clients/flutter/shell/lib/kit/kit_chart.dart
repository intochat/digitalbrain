import 'package:flutter/material.dart';
import 'package:graphic/graphic.dart';

import '../brain_theme.dart';

const kitBarChartData = <Map<String, Object>>[
  {'label': 'Mon', 'value': 42},
  {'label': 'Tue', 'value': 68},
  {'label': 'Wed', 'value': 51},
  {'label': 'Thu', 'value': 89},
  {'label': 'Fri', 'value': 74},
  {'label': 'Sat', 'value': 33},
  {'label': 'Sun', 'value': 47},
];

const kitLineChartData = <Map<String, Object>>[
  {'t': 0, 'value': 12},
  {'t': 1, 'value': 18},
  {'t': 2, 'value': 15},
  {'t': 3, 'value': 28},
  {'t': 4, 'value': 22},
  {'t': 5, 'value': 35},
  {'t': 6, 'value': 31},
  {'t': 7, 'value': 44},
];

/// Bar chart demo via [graphic](https://pub.dev/packages/graphic).
final class KitBarChart extends StatelessWidget {
  const KitBarChart({super.key, this.height = 220});

  final double height;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      key: const Key('kit_bar_chart'),
      height: height,
      child: Chart(
        data: kitBarChartData,
        variables: {
          'label': Variable(accessor: (Map map) => map['label'] as String),
          'value': Variable(
            accessor: (Map map) => map['value'] as num,
            scale: LinearScale(min: 0, max: 100),
          ),
        },
        marks: [
          IntervalMark(
            color: ColorEncode(value: BrainPalette.signal),
            shape: ShapeEncode(
              value: RectShape(borderRadius: BorderRadius.circular(4)),
            ),
          ),
        ],
        axes: [
          Defaults.horizontalAxis
            ..line = PaintStyle(strokeColor: BrainPalette.line)
            ..label = LabelStyle(
              textStyle: BrainType.meta.copyWith(color: BrainPalette.textMuted),
            ),
          Defaults.verticalAxis
            ..line = PaintStyle(strokeColor: BrainPalette.line)
            ..grid = PaintStyle(strokeColor: BrainPalette.line.withValues(alpha: 0.5))
            ..label = LabelStyle(
              textStyle: BrainType.meta.copyWith(color: BrainPalette.textMuted),
            ),
        ],
        padding: (_) => const EdgeInsets.fromLTRB(36, 12, 12, 28),
      ),
    );
  }
}

/// Line chart demo via graphic.
final class KitLineChart extends StatelessWidget {
  const KitLineChart({super.key, this.height = 220});

  final double height;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      key: const Key('kit_line_chart'),
      height: height,
      child: Chart(
        data: kitLineChartData,
        variables: {
          't': Variable(
            accessor: (Map map) => map['t'] as num,
            scale: LinearScale(min: 0, max: 7, tickCount: 8),
          ),
          'value': Variable(
            accessor: (Map map) => map['value'] as num,
            scale: LinearScale(min: 0, max: 50),
          ),
        },
        marks: [
          LineMark(
            color: ColorEncode(value: BrainPalette.owner),
            shape: ShapeEncode(value: BasicLineShape(smooth: true)),
          ),
          PointMark(
            color: ColorEncode(value: BrainPalette.signal),
            size: SizeEncode(value: 6),
          ),
        ],
        axes: [
          Defaults.horizontalAxis
            ..line = PaintStyle(strokeColor: BrainPalette.line)
            ..label = LabelStyle(
              textStyle: BrainType.meta.copyWith(color: BrainPalette.textMuted),
            ),
          Defaults.verticalAxis
            ..line = PaintStyle(strokeColor: BrainPalette.line)
            ..grid = PaintStyle(strokeColor: BrainPalette.line.withValues(alpha: 0.5))
            ..label = LabelStyle(
              textStyle: BrainType.meta.copyWith(color: BrainPalette.textMuted),
            ),
        ],
        padding: (_) => const EdgeInsets.fromLTRB(36, 12, 12, 28),
      ),
    );
  }
}

final class KitChartCard extends StatelessWidget {
  const KitChartCard({
    super.key,
    required this.title,
    required this.child,
  });

  final String title;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 360,
      padding: const EdgeInsets.fromLTRB(14, 14, 14, 8),
      decoration: BoxDecoration(
        color: BrainPalette.surfaceRaised,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: BrainPalette.line),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(title, style: BrainType.cardTitle),
          const SizedBox(height: 10),
          child,
        ],
      ),
    );
  }
}
