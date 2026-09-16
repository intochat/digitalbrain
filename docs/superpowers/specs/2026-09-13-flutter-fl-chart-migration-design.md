# Flutter chart migration: graphic → fl_chart

**Date:** 2026-09-13  
**Status:** Approved for planning  
**Package:** [fl_chart](https://pub.dev/packages/fl_chart) `^1.2.0` (latest on pub.dev at design time)

## Goal

Replace the `graphic` dependency in the DigitalBrain Flutter UI package with `fl_chart`, without changing the public `UiChart` / `UiChartPart` contract used by gallery, chat bubbles, and workspace artifact editors.

## Decisions

| Decision | Choice |
| --- | --- |
| Public API | Keep identical (`UiChart`, `UiChartPart`, `UiChartPoint`, `chartKind` `bar` \| `line`) |
| Structure | In-place swap inside `UiChart` (no new public widgets, no renderer abstraction) |
| Visual style | fl_chart-native polish (rounded bars, subtle grid, touch indicators) with `UiPalette` |
| New chart kinds | Out of scope (no pie/area/radar in this change) |

## Current state

- Dependency: `graphic: ^2.7.0` in `src/Modules/Flutter/app/ui/pubspec.yaml`
- Implementation: `src/Modules/Flutter/app/ui/lib/src/components/chart/ui_chart.dart`
- Tests: `src/Modules/Flutter/app/ui/test/ui_chart_test.dart` assert graphic `IntervalMark` / `LineMark` / `PointMark`
- Call sites (unchanged by this work): gallery preview, shell artifact editors, chat tool results — they consume `UiChart` / `UiChartPart` only

## Target behavior

### Unchanged for callers

- `UiChart({required UiChartPart part, double height = 200})`
- `UiChartPart` fields: `title`, `points`, `chartKind` (default `'bar'`)
- Empty `points` → centered muted text `No series` (no chart widget)
- `UiChart.axisLabel` truncates labels longer than 12 chars with `…`
- Card chrome: raised surface, 12px radius, border, title, padding
- Y domain headroom: `maxValue * 1.15` (or `1.0` when empty — empty path does not chart)

### Changed (rendering only)

- `chartKind == 'bar'` → `BarChart` with one rod per point, rounded rods, `UiPalette.signal`
- `chartKind == 'line'` (and any non-`bar` kind treated as line, matching today) → `LineChart` with spots + dots, `UiPalette.signal`
- Shared: `FlGridData`, `FlTitlesData` (bottom category labels via `axisLabel`, left numeric), subtle `FlBorderData`, fl_chart touch tooltips showing **full** label + value
- Remove all `package:graphic/graphic.dart` imports and the `graphic` pubspec dependency

## Files to touch

1. `src/Modules/Flutter/app/ui/pubspec.yaml` — replace `graphic` with `fl_chart: ^1.2.0`
2. `src/Modules/Flutter/app/ui/lib/src/components/chart/ui_chart.dart` — rewrite chart body to fl_chart
3. `src/Modules/Flutter/app/ui/test/ui_chart_test.dart` — assert `BarChart` / `LineChart` instead of graphic marks
4. Workspace lockfile under `src/Modules/Flutter/app/` — refresh via `flutter pub get`

Shell tests that only find `UiChart` and inspect `part` metadata do not need changes.

## Verification

- `flutter test` in `src/Modules/Flutter/app/ui` (at least `ui_chart_test.dart`)
- Confirm no remaining `graphic` references under `src/Modules/Flutter/app`
- Smoke: gallery chart entry and empty-series placeholder still render

## Out of scope

- New `chartKind` values (pie, area, scatter, radar, candlestick)
- Changing metadata / server contract for chart parts
- Theme token redesign beyond using existing `UiPalette` / `UiType`
- Extracting a reusable chart engine abstraction for future swaps

## Risks

- fl_chart touch/tooltip APIs differ from graphic selections; behavior should feel native to fl_chart while still exposing label + value
- Widget tests must target fl_chart types (`BarChart`, `LineChart`) rather than internal mark classes
- Ensure Flutter SDK / workspace resolution accepts `fl_chart` 1.2.x alongside existing deps (`forui`, `flutter_chat_ui`, etc.)
