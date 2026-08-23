import 'package:flutter/material.dart';
import 'package:graphic/graphic.dart';

import '../brain_theme.dart';

const shellBarChartData = <Map<String, Object>>[
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

/// Demo BTC/USD-style closes over 24h (fixed wall times).
final kitTimeChartData = <Map<String, Object>>[
  {'time': DateTime(2026, 8, 5, 0), 'value': 64210},
  {'time': DateTime(2026, 8, 5, 1), 'value': 63880},
  {'time': DateTime(2026, 8, 5, 2), 'value': 64120},
  {'time': DateTime(2026, 8, 5, 3), 'value': 63540},
  {'time': DateTime(2026, 8, 5, 4), 'value': 62980},
  {'time': DateTime(2026, 8, 5, 5), 'value': 63240},
  {'time': DateTime(2026, 8, 5, 6), 'value': 64010},
  {'time': DateTime(2026, 8, 5, 7), 'value': 64860},
  {'time': DateTime(2026, 8, 5, 8), 'value': 65120},
  {'time': DateTime(2026, 8, 5, 9), 'value': 64740},
  {'time': DateTime(2026, 8, 5, 10), 'value': 65590},
  {'time': DateTime(2026, 8, 5, 11), 'value': 66210},
  {'time': DateTime(2026, 8, 5, 12), 'value': 65840},
  {'time': DateTime(2026, 8, 5, 13), 'value': 66480},
  {'time': DateTime(2026, 8, 5, 14), 'value': 67120},
  {'time': DateTime(2026, 8, 5, 15), 'value': 66890},
  {'time': DateTime(2026, 8, 5, 16), 'value': 67540},
  {'time': DateTime(2026, 8, 5, 17), 'value': 68210},
  {'time': DateTime(2026, 8, 5, 18), 'value': 67860},
  {'time': DateTime(2026, 8, 5, 19), 'value': 68440},
  {'time': DateTime(2026, 8, 5, 20), 'value': 69120},
  {'time': DateTime(2026, 8, 5, 21), 'value': 68780},
  {'time': DateTime(2026, 8, 5, 22), 'value': 69340},
  {'time': DateTime(2026, 8, 5, 23), 'value': 69842},
];

const _btcUp = Color(0xFF0ECB81);
const _btcDown = Color(0xFFF6465D);

/// Bar chart demo via [graphic](https://pub.dev/packages/graphic).
final class ShellBarChartDemo extends StatelessWidget {
  const ShellBarChartDemo({super.key, this.height = 220});

  final double height;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      key: const Key('kit_bar_chart'),
      height: height,
      child: Chart(
        data: shellBarChartData,
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
            ..grid = PaintStyle(
              strokeColor: BrainPalette.line.withValues(alpha: 0.5),
            )
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
            ..grid = PaintStyle(
              strokeColor: BrainPalette.line.withValues(alpha: 0.5),
            )
            ..label = LabelStyle(
              textStyle: BrainType.meta.copyWith(color: BrainPalette.textMuted),
            ),
        ],
        padding: (_) => const EdgeInsets.fromLTRB(36, 12, 12, 28),
      ),
    );
  }
}

/// Bitcoin-style price chart: ticker header, area fill, tight price scale.
final class KitTimeChart extends StatelessWidget {
  const KitTimeChart({super.key, this.height});

  /// Optional fixed height; when null, fills the parent (window panel body).
  final double? height;

  static const _open = 64210.0;
  static const _last = 69842.0;

  @override
  Widget build(BuildContext context) {
    final change = _last - _open;
    final pct = change / _open * 100;
    final up = change >= 0;
    final accent = up ? _btcUp : _btcDown;

    final body = Column(
      key: const Key('kit_time_chart'),
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    'BTC / USD',
                    style: BrainType.metaStrong.copyWith(
                      color: BrainPalette.textMuted,
                      letterSpacing: 0.4,
                    ),
                  ),
                  const SizedBox(height: 2),
                  Text(
                    _formatUsd(_last),
                    style: BrainType.cardTitle.copyWith(
                      fontSize: 18,
                      fontFeatures: const [FontFeature.tabularFigures()],
                    ),
                  ),
                ],
              ),
            ),
            Column(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                Text(
                  '24H',
                  style: BrainType.meta.copyWith(color: BrainPalette.textFaint),
                ),
                const SizedBox(height: 2),
                Text(
                  '${up ? '+' : ''}${pct.toStringAsFixed(2)}%',
                  style: BrainType.metaStrong.copyWith(
                    color: accent,
                    fontFeatures: const [FontFeature.tabularFigures()],
                  ),
                ),
              ],
            ),
          ],
        ),
        const SizedBox(height: 6),
        Expanded(
          child: Chart(
            data: kitTimeChartData,
            variables: {
              'time': Variable(
                accessor: (Map map) => map['time'] as DateTime,
                scale: TimeScale(
                  min: DateTime(2026, 8, 5),
                  max: DateTime(2026, 8, 5, 23),
                  tickCount: 5,
                  formatter: (time) =>
                      '${time.hour.toString().padLeft(2, '0')}:00',
                ),
              ),
              'value': Variable(
                accessor: (Map map) => map['value'] as num,
                scale: LinearScale(
                  min: 62000,
                  max: 71000,
                  tickCount: 4,
                  formatter: (v) => '\$${(v / 1000).toStringAsFixed(0)}k',
                ),
              ),
            },
            marks: [
              AreaMark(
                gradient: GradientEncode(
                  value: LinearGradient(
                    begin: Alignment.topCenter,
                    end: Alignment.bottomCenter,
                    colors: [
                      accent.withValues(alpha: 0.42),
                      accent.withValues(alpha: 0.0),
                    ],
                  ),
                ),
                shape: ShapeEncode(value: BasicAreaShape(smooth: true)),
              ),
              LineMark(
                color: ColorEncode(value: accent),
                shape: ShapeEncode(value: BasicLineShape(smooth: true)),
                size: SizeEncode(value: 2),
              ),
            ],
            axes: [
              Defaults.horizontalAxis
                ..line = PaintStyle(strokeColor: BrainPalette.line)
                ..label = LabelStyle(
                  textStyle: BrainType.meta.copyWith(
                    color: BrainPalette.textMuted,
                  ),
                ),
              Defaults.verticalAxis
                ..line = null
                ..grid = PaintStyle(
                  strokeColor: BrainPalette.line.withValues(alpha: 0.55),
                )
                ..label = LabelStyle(
                  textStyle: BrainType.meta.copyWith(
                    color: BrainPalette.textMuted,
                  ),
                ),
            ],
            padding: (_) => const EdgeInsets.fromLTRB(40, 8, 8, 24),
          ),
        ),
      ],
    );

    if (height != null) {
      return SizedBox(height: height, child: body);
    }
    return body;
  }

  static String _formatUsd(double value) {
    final whole = value.round();
    final digits = whole.toString();
    final buf = StringBuffer(r'$');
    for (var i = 0; i < digits.length; i++) {
      if (i > 0 && (digits.length - i) % 3 == 0) buf.write(',');
      buf.write(digits[i]);
    }
    return buf.toString();
  }
}

final class KitChartCard extends StatelessWidget {
  const KitChartCard({super.key, required this.title, required this.child});

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
