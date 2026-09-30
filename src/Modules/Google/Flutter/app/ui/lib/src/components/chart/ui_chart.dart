import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';

import '../../lumen/lumen_palette.dart';
import '../../models/ui_part.dart';
import '../../theme/ui_theme.dart';

/// Product chart control. Same widget for surface galleries and chat bubbles.
///
/// `chartKind` picks bars (`bar`, the default), a line with points (`line`),
/// or pie slices (`pie`). Colors follow the ambient Material theme (Lumen light
/// or dark) instead of the legacy dark-only UiPalette.
final class UiChart extends StatelessWidget {
  const UiChart({super.key, required this.part, this.height = 200});

  final UiChartPart part;
  final double height;

  static const axisLabelLength = 12;

  static const _lightSlices = [
    LumenPalette.accent,
    LumenPalette.learned,
    LumenPalette.warning,
    LumenPalette.link,
    Color(0xFF7B9BE3),
    Color(0xFFC47AC0),
  ];

  static const _darkSlices = [
    UiPalette.signal,
    UiPalette.owner,
    UiPalette.success,
    Color(0xFFC47AC0),
    Color(0xFF5EC4D4),
    Color(0xFFE0C35C),
  ];

  static String axisLabel(String label) => label.length <= axisLabelLength
      ? label
      : '${label.substring(0, axisLabelLength - 1)}…';

  @override
  Widget build(BuildContext context) {
    final tone = _ChartTone.of(context);
    final maxValue = part.points.isEmpty
        ? 1.0
        : part.points
              .map((p) => p.value.toDouble())
              .reduce((a, b) => a > b ? a : b);
    final maxY = maxValue * 1.15;
    final kind = part.chartKind.trim().toLowerCase();

    return DecoratedBox(
      key: Key('ui_chart_${part.title}'),
      decoration: BoxDecoration(
        color: tone.card,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: tone.border),
      ),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(12, 12, 12, 8),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Semantics(
              label: part.title,
              child: Text(
                part.title,
                style: TextStyle(
                  fontFamily: UiType.bodyFamily,
                  fontFamilyFallback: UiType.bodyFallback,
                  fontSize: 15,
                  fontWeight: FontWeight.w600,
                  letterSpacing: -0.2,
                  color: tone.ink,
                ),
              ),
            ),
            const SizedBox(height: 10),
            SizedBox(
              height: height,
              child: part.points.isEmpty
                  ? Center(
                      child: Text(
                        'No series',
                        style: TextStyle(
                          fontFamily: UiType.bodyFamily,
                          fontFamilyFallback: UiType.bodyFallback,
                          fontSize: 14,
                          height: 1.5,
                          color: tone.muted,
                        ),
                      ),
                    )
                  : switch (kind) {
                      'line' => _lineChart(tone, maxY),
                      'pie' => _pieChart(tone),
                      _ => _barChart(tone, maxY),
                    },
            ),
          ],
        ),
      ),
    );
  }

  Widget _barChart(_ChartTone tone, double maxY) {
    return BarChart(
      BarChartData(
        maxY: maxY,
        alignment: BarChartAlignment.spaceAround,
        barTouchData: BarTouchData(
          enabled: true,
          touchTooltipData: BarTouchTooltipData(
            getTooltipColor: (_) => tone.tooltip,
            getTooltipItem: (group, groupIndex, rod, rodIndex) {
              final point = part.points[groupIndex];
              return BarTooltipItem(
                '${point.label}\n${point.value}',
                TextStyle(
                  color: tone.ink,
                  fontWeight: FontWeight.w600,
                  fontSize: 12,
                ),
              );
            },
          ),
        ),
        titlesData: _titlesData(tone),
        gridData: _gridData(tone),
        borderData: FlBorderData(
          show: true,
          border: Border.all(color: tone.border),
        ),
        barGroups: [
          for (var i = 0; i < part.points.length; i++)
            BarChartGroupData(
              x: i,
              barRods: [
                BarChartRodData(
                  toY: part.points[i].value.toDouble(),
                  width: 14,
                  color: tone.accent,
                  borderRadius: const BorderRadius.vertical(
                    top: Radius.circular(4),
                  ),
                ),
              ],
            ),
        ],
      ),
    );
  }

  Widget _lineChart(_ChartTone tone, double maxY) {
    return LineChart(
      LineChartData(
        minY: 0,
        maxY: maxY,
        lineTouchData: LineTouchData(
          enabled: true,
          touchTooltipData: LineTouchTooltipData(
            getTooltipColor: (_) => tone.tooltip,
            getTooltipItems: (spots) => [
              for (final spot in spots)
                LineTooltipItem(
                  '${part.points[spot.x.toInt()].label}\n${part.points[spot.x.toInt()].value}',
                  TextStyle(
                    color: tone.ink,
                    fontWeight: FontWeight.w600,
                    fontSize: 12,
                  ),
                ),
            ],
          ),
        ),
        titlesData: _titlesData(tone),
        gridData: _gridData(tone),
        borderData: FlBorderData(
          show: true,
          border: Border.all(color: tone.border),
        ),
        lineBarsData: [
          LineChartBarData(
            spots: [
              for (var i = 0; i < part.points.length; i++)
                FlSpot(i.toDouble(), part.points[i].value.toDouble()),
            ],
            isCurved: false,
            color: tone.accent,
            barWidth: 2,
            isStrokeCapRound: true,
            dotData: const FlDotData(show: true),
            belowBarData: BarAreaData(show: false),
          ),
        ],
      ),
    );
  }

  Widget _pieChart(_ChartTone tone) {
    final total = part.points.fold<double>(
      0,
      (sum, point) => sum + point.value.toDouble().abs(),
    );
    final percentStyle = TextStyle(
      color: tone.percentInk,
      fontSize: 13,
      fontWeight: FontWeight.w700,
      fontFamily: UiType.bodyFamily,
      fontFamilyFallback: UiType.bodyFallback,
      shadows: [Shadow(color: tone.percentShadow, blurRadius: 2)],
    );

    return ColoredBox(
      color: tone.card,
      child: Row(
        children: [
          Expanded(
            flex: 3,
            child: PieChart(
              PieChartData(
                sectionsSpace: 2,
                centerSpaceRadius: 32,
                centerSpaceColor: tone.card,
                borderData: FlBorderData(show: false),
                pieTouchData: PieTouchData(enabled: true),
                sections: [
                  for (var i = 0; i < part.points.length; i++)
                    PieChartSectionData(
                      value: part.points[i].value.toDouble().abs(),
                      title: percentLabel(part.points[i].value, total),
                      color: tone.slices[i % tone.slices.length],
                      radius: 52,
                      titleStyle: percentStyle,
                      titlePositionPercentageOffset: 0.55,
                    ),
                ],
              ),
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            flex: 2,
            child: SingleChildScrollView(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  for (var i = 0; i < part.points.length; i++) ...[
                    if (i > 0) const SizedBox(height: 8),
                    _pieLegendRow(
                      tone: tone,
                      color: tone.slices[i % tone.slices.length],
                      label: part.points[i].label,
                      value: part.points[i].value,
                    ),
                  ],
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _pieLegendRow({
    required _ChartTone tone,
    required Color color,
    required String label,
    required num value,
  }) {
    return Row(
      children: [
        Container(
          width: 10,
          height: 10,
          decoration: BoxDecoration(color: color, shape: BoxShape.circle),
        ),
        const SizedBox(width: 6),
        Expanded(
          child: Text(
            '${axisLabel(label)}  ${formatValue(value)}',
            style: TextStyle(
              color: tone.muted,
              fontSize: 12,
              fontFamily: UiType.bodyFamily,
              fontFamilyFallback: UiType.bodyFallback,
            ),
            overflow: TextOverflow.ellipsis,
          ),
        ),
      ],
    );
  }

  static String formatValue(num value) {
    final asDouble = value.toDouble();
    if (asDouble == asDouble.roundToDouble()) {
      return asDouble.round().toString();
    }
    return value.toString();
  }

  static String percentLabel(num value, double total) {
    if (total <= 0) {
      return '0%';
    }
    return '${((value.toDouble().abs() / total) * 100).round()}%';
  }

  FlTitlesData _titlesData(_ChartTone tone) {
    final labelStyle = TextStyle(
      color: tone.muted,
      fontSize: 11,
      fontFamily: UiType.bodyFamily,
      fontFamilyFallback: UiType.bodyFallback,
    );
    return FlTitlesData(
      topTitles: const AxisTitles(sideTitles: SideTitles(showTitles: false)),
      rightTitles: const AxisTitles(sideTitles: SideTitles(showTitles: false)),
      bottomTitles: AxisTitles(
        sideTitles: SideTitles(
          showTitles: true,
          reservedSize: 28,
          interval: 1,
          getTitlesWidget: (value, meta) {
            final index = value.toInt();
            if (index < 0 ||
                index >= part.points.length ||
                value != index.toDouble()) {
              return const SizedBox.shrink();
            }
            return SideTitleWidget(
              meta: meta,
              child: Text(
                axisLabel(part.points[index].label),
                style: labelStyle,
              ),
            );
          },
        ),
      ),
      leftTitles: AxisTitles(
        sideTitles: SideTitles(
          showTitles: true,
          reservedSize: 36,
          getTitlesWidget: (value, meta) {
            return SideTitleWidget(
              meta: meta,
              child: Text(meta.formattedValue, style: labelStyle),
            );
          },
        ),
      ),
    );
  }

  FlGridData _gridData(_ChartTone tone) => FlGridData(
    show: true,
    drawVerticalLine: false,
    getDrawingHorizontalLine: (_) => FlLine(color: tone.grid, strokeWidth: 1),
  );
}

final class _ChartTone {
  const _ChartTone({
    required this.card,
    required this.border,
    required this.grid,
    required this.ink,
    required this.muted,
    required this.accent,
    required this.tooltip,
    required this.percentInk,
    required this.percentShadow,
    required this.slices,
  });

  final Color card;
  final Color border;
  final Color grid;
  final Color ink;
  final Color muted;
  final Color accent;
  final Color tooltip;
  final Color percentInk;
  final Color percentShadow;
  final List<Color> slices;

  factory _ChartTone.of(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final light = theme.brightness == Brightness.light;
    return _ChartTone(
      card: light ? LumenPalette.surface : scheme.surfaceContainerLow,
      border: light ? LumenPalette.line : scheme.outlineVariant,
      grid: light ? LumenPalette.line : scheme.outlineVariant,
      ink: light ? LumenPalette.ink : scheme.onSurface,
      muted: light ? LumenPalette.muted : scheme.onSurfaceVariant,
      accent: light ? LumenPalette.accent : UiPalette.signal,
      tooltip: light ? LumenPalette.surfaceMuted : scheme.surfaceContainerHigh,
      percentInk: light ? LumenPalette.surface : scheme.onSurface,
      percentShadow: light ? const Color(0x66000000) : const Color(0x88000000),
      slices: light ? UiChart._lightSlices : UiChart._darkSlices,
    );
  }
}
