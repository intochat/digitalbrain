# Flutter fl_chart Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `graphic` with `fl_chart` inside `UiChart` while keeping the public `UiChart` / `UiChartPart` API unchanged.

**Architecture:** In-place swap in `ui_chart.dart`. `chartKind == 'bar'` maps to `BarChart`; otherwise `LineChart`. Card chrome, empty state, and `axisLabel` stay as today. Style uses fl_chart-native polish (rounded rods, grid, touch tooltips) with `UiPalette`.

**Tech Stack:** Flutter, `fl_chart: ^1.2.0`, existing `digitalbrain_ui` models/theme, `flutter_test`.

**Spec:** `docs/superpowers/specs/2026-09-13-flutter-fl-chart-migration-design.md`

---

## File map

| File | Role |
| --- | --- |
| `src/Modules/UI/Flutter/ui/pubspec.yaml` | Swap `graphic` → `fl_chart` |
| `src/Modules/UI/Flutter/pubspec.lock` | Resolved by `flutter pub get` from workspace root |
| `src/Modules/UI/Flutter/ui/lib/src/components/chart/ui_chart.dart` | Only rendering change |
| `src/Modules/UI/Flutter/ui/test/ui_chart_test.dart` | Assert `BarChart` / `LineChart` |

Do not change: `ui_part.dart`, gallery, shell editors, or shell tests that only inspect `UiChart.part`.

---

### Task 1: Swap the chart dependency

**Files:**
- Modify: `src/Modules/UI/Flutter/ui/pubspec.yaml`
- Modify: `src/Modules/UI/Flutter/pubspec.lock` (via pub get)

- [ ] **Step 1: Replace the dependency**

In `src/Modules/UI/Flutter/ui/pubspec.yaml`, change:

```yaml
  graphic: ^2.7.0
```

to:

```yaml
  fl_chart: ^1.2.0
```

- [ ] **Step 2: Resolve packages**

Run from the Flutter workspace root:

```bash
cd src/Modules/UI/Flutter
flutter pub get
```

Expected: exit 0; lockfile lists `fl_chart` and no longer lists `graphic` as a direct/transitive need of `digitalbrain_ui` (other packages may still mention unrelated names — only `graphic` must be gone).

- [ ] **Step 3: Confirm graphic is gone from the UI package**

```bash
rg "graphic" src/Modules/UI/Flutter/ui/pubspec.yaml src/Modules/UI/Flutter/pubspec.lock
```

Expected: no matches for the `graphic` package (lockfile entry `graphic:` must be absent).

- [ ] **Step 4: Commit**

```bash
git add src/Modules/UI/Flutter/ui/pubspec.yaml src/Modules/UI/Flutter/pubspec.lock
git commit -m "deps(ui): replace graphic with fl_chart"
```

---

### Task 2: Rewrite chart tests for fl_chart (TDD red)

**Files:**
- Modify: `src/Modules/UI/Flutter/ui/test/ui_chart_test.dart`
- Test: same file

- [ ] **Step 1: Replace the test file contents**

Overwrite `src/Modules/UI/Flutter/ui/test/ui_chart_test.dart` with:

```dart
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

UiChartPart chart(String kind, {List<UiChartPoint>? points}) => UiChartPart(
  title: 'Companies by country',
  chartKind: kind,
  points:
      points ??
      const [
        UiChartPoint(label: 'GB', value: 11),
        UiChartPoint(label: 'DE', value: 10),
        UiChartPoint(label: 'CZ', value: 9),
      ],
);

Future<void> pump(WidgetTester tester, UiChartPart part) async {
  tester.view.physicalSize = const Size(800, 600);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  await tester.pumpWidget(
    MaterialApp(
      home: Scaffold(
        body: SizedBox(width: 600, child: UiChart(part: part)),
      ),
    ),
  );
  await tester.pump();
}

void main() {
  testWidgets('bar charts draw a BarChart', (tester) async {
    await pump(tester, chart('bar'));
    expect(find.byType(BarChart), findsOneWidget);
    expect(find.byType(LineChart), findsNothing);
    expect(find.text('Companies by country'), findsOneWidget);
  });

  testWidgets('line charts draw a LineChart', (tester) async {
    await pump(tester, chart('line'));
    expect(find.byType(LineChart), findsOneWidget);
    expect(find.byType(BarChart), findsNothing);
    expect(find.text('Companies by country'), findsOneWidget);
  });

  testWidgets('empty data shows a placeholder instead of a chart', (
    tester,
  ) async {
    await pump(tester, chart('line', points: const []));
    expect(find.text('No series'), findsOneWidget);
    expect(find.byType(BarChart), findsNothing);
    expect(find.byType(LineChart), findsNothing);
  });

  test('long category labels are shortened for the axis', () {
    expect(UiChart.axisLabel('GB'), 'GB');
    expect(UiChart.axisLabel('Construction'), 'Construction');
    expect(UiChart.axisLabel('Civil engineering'), 'Civil engin…');
  });
}
```

- [ ] **Step 2: Run tests — expect compile/runtime failure**

```bash
cd src/Modules/UI/Flutter/ui
flutter test test/ui_chart_test.dart
```

Expected: **FAIL** — `ui_chart.dart` still imports `package:graphic/graphic.dart` (missing) and/or does not build `BarChart` / `LineChart`.

- [ ] **Step 3: Commit the red tests**

```bash
git add src/Modules/UI/Flutter/ui/test/ui_chart_test.dart
git commit -m "test(ui): retarget chart tests to fl_chart widgets"
```

---

### Task 3: Implement UiChart with fl_chart (TDD green)

**Files:**
- Modify: `src/Modules/UI/Flutter/ui/lib/src/components/chart/ui_chart.dart`

- [ ] **Step 1: Replace `ui_chart.dart` with the fl_chart implementation**

Overwrite `src/Modules/UI/Flutter/ui/lib/src/components/chart/ui_chart.dart` with:

```dart
import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';

import '../../models/ui_part.dart';
import '../../theme/ui_theme.dart';

/// Product chart control. Same widget for surface galleries and chat bubbles.
///
/// `chartKind` picks bars (`bar`, the default) or a line with points (`line`).
/// Touch a point for a tooltip with the full label and value; long category
/// labels are shortened on the axis only.
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
    final maxValue = part.points.isEmpty
        ? 1.0
        : part.points
              .map((p) => p.value.toDouble())
              .reduce((a, b) => a > b ? a : b);
    final maxY = maxValue * 1.15;
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
              child: part.points.isEmpty
                  ? const Center(
                      child: Text('No series', style: UiType.bodyMuted),
                    )
                  : line
                  ? _lineChart(maxY)
                  : _barChart(maxY),
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
              child: Text(axisLabel(part.points[index].label), style: labelStyle),
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
              child: Text(
                meta.formattedValue,
                style: labelStyle,
              ),
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
```

- [ ] **Step 2: Run chart tests — expect pass**

```bash
cd src/Modules/UI/Flutter/ui
flutter test test/ui_chart_test.dart
```

Expected: all 4 tests PASS.

If `SideTitleWidget(meta: ...)` or tooltip APIs fail to analyze against the resolved `fl_chart` version, fix against the installed package API (prefer `getTooltipColor` over deprecated `tooltipBgColor`; prefer `FlGridData` over any `GridData` alias). Re-run until green.

- [ ] **Step 3: Confirm no graphic imports remain**

```bash
rg "package:graphic|graphic:" src/Modules/UI/Flutter --glob "!**/pubspec.lock"
```

Expected: no matches under Flutter sources/pubspecs (ignore unrelated words like `table_chart` icon names).

- [ ] **Step 4: Commit**

```bash
git add src/Modules/UI/Flutter/ui/lib/src/components/chart/ui_chart.dart
git commit -m "feat(ui): render UiChart with fl_chart"
```

---

### Task 4: Workspace verification

**Files:** none expected (read-only verification)

- [ ] **Step 1: Run the UI package test suite**

```bash
cd src/Modules/UI/Flutter/ui
flutter test
```

Expected: all tests PASS (including `ui_chart_test.dart`).

- [ ] **Step 2: Optionally run shell chart artifact smoke tests**

```bash
cd src/Modules/UI/Flutter/shell
flutter test test/workspace_editors_test.dart
```

Expected: PASS — these assert `UiChart` / `part` metadata, not graphic types.

- [ ] **Step 3: Final commit only if verification forced extra fixes**

If Steps 1–2 required code fixes, commit those fixes with a focused message. Otherwise no extra commit.

---

## Self-review (plan vs spec)

| Spec requirement | Task |
| --- | --- |
| Replace `graphic` with `fl_chart ^1.2.0` | Task 1 |
| Keep `UiChart` / `UiChartPart` / `chartKind` / empty `No series` / `axisLabel` | Task 3 (API preserved) |
| Bar → `BarChart`, line → `LineChart` with polish + `UiPalette` | Task 3 |
| Retarget `ui_chart_test.dart` | Task 2 |
| Shell metadata tests unchanged | Explicit non-touch + Task 4 optional run |
| No new chart kinds | Not introduced |

No TBD/placeholder steps. Types used: `BarChart`, `LineChart`, `BarChartRodData`, `FlSpot`, `FlTitlesData`, `SideTitleWidget`, `getTooltipColor`.
