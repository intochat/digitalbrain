import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';

import '../../models/ui_part.dart';
import '../../theme/ui_theme.dart';

/// Product chart control. Same widget for surface galleries and chat bubbles.
///
/// `chartKind` picks bars (`bar`, the default), a line with points (`line`),
/// or pie slices (`pie`). Touch a point for a tooltip with the full label and
/// value; long category labels are shortened on axes and slice titles only.
final class UiChart extends StatelessWidget {
  const UiChart({super.key, required this.part, this.height = 200});

  final UiChartPart part;
  final double height;

  static const axisLabelLength = 12;

  static const _sliceColors = [
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
              child: part.points.isEmpty
                  ? const Center(
                      child: Text('No series', style: UiType.bodyMuted),
                    )
                  : switch (kind) {
                      'line' => _lineChart(maxY),
                      'pie' => _pieChart(),
                      _ => _barChart(maxY),
                    },
            ),
          ],
        ),
      ),
    );
  }

  Widget _barChart(double maxY) {
    return BarChart(
      BarChartData(
        maxY: maxY,
        alignment: BarChartAlignment.spaceAround,
        barTouchData: BarTouchData(
          enabled: true,
          touchTooltipData: BarTouchTooltipData(
            getTooltipColor: (_) => UiPalette.surfaceSunken,
            getTooltipItem: (group, groupIndex, rod, rodIndex) {
              final point = part.points[groupIndex];
              return BarTooltipItem(
                '${point.label}\n${point.value}',
                const TextStyle(
                  color: UiPalette.textPrimary,
                  fontWeight: FontWeight.w600,
                  fontSize: 12,
                ),
              );
            },
          ),
        ),
        titlesData: _titlesData(),
        gridData: _gridData(),
        borderData: FlBorderData(
          show: true,
          border: Border.all(color: UiPalette.line),
        ),
        barGroups: [
          for (var i = 0; i < part.points.length; i++)
            BarChartGroupData(
              x: i,
              barRods: [
                BarChartRodData(
                  toY: part.points[i].value.toDouble(),
                  width: 14,
                  color: UiPalette.signal,
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

  Widget _lineChart(double maxY) {
    return LineChart(
      LineChartData(
        minY: 0,
        maxY: maxY,
        lineTouchData: LineTouchData(
          enabled: true,
          touchTooltipData: LineTouchTooltipData(
            getTooltipColor: (_) => UiPalette.surfaceSunken,
            getTooltipItems: (spots) => [
              for (final spot in spots)
                LineTooltipItem(
                  '${part.points[spot.x.toInt()].label}\n${part.points[spot.x.toInt()].value}',
                  const TextStyle(
                    color: UiPalette.textPrimary,
                    fontWeight: FontWeight.w600,
                    fontSize: 12,
                  ),
                ),
            ],
          ),
        ),
        titlesData: _titlesData(),
        gridData: _gridData(),
        borderData: FlBorderData(
          show: true,
          border: Border.all(color: UiPalette.line),
        ),
        lineBarsData: [
          LineChartBarData(
            spots: [
              for (var i = 0; i < part.points.length; i++)
                FlSpot(i.toDouble(), part.points[i].value.toDouble()),
            ],
            isCurved: false,
            color: UiPalette.signal,
            barWidth: 2,
            isStrokeCapRound: true,
            dotData: const FlDotData(show: true),
            belowBarData: BarAreaData(show: false),
          ),
        ],
      ),
    );
  }

  Widget _pieChart() {
    const titleStyle = TextStyle(
      color: UiPalette.textPrimary,
      fontSize: 11,
      fontWeight: FontWeight.w600,
      fontFamily: UiType.bodyFamily,
      fontFamilyFallback: UiType.bodyFallback,
    );
    // PieChart has no built-in tooltip bubble in fl_chart 1.2; slice titles carry
    // the shortened label and the full label stays available via the point model.
    return PieChart(
      PieChartData(
        sectionsSpace: 2,
        centerSpaceRadius: 36,
        borderData: FlBorderData(show: false),
        pieTouchData: PieTouchData(enabled: true),
        sections: [
          for (var i = 0; i < part.points.length; i++)
            PieChartSectionData(
              value: part.points[i].value.toDouble().abs(),
              title: axisLabel(part.points[i].label),
              color: _sliceColors[i % _sliceColors.length],
              radius: 56,
              titleStyle: titleStyle,
            ),
        ],
      ),
    );
  }

  FlTitlesData _titlesData() {
    final labelStyle = TextStyle(
      color: UiPalette.textMuted,
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

  FlGridData _gridData() => FlGridData(
    show: true,
    drawVerticalLine: false,
    getDrawingHorizontalLine: (_) =>
        const FlLine(color: UiPalette.line, strokeWidth: 1),
  );
}
