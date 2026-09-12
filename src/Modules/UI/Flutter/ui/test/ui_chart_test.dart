import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:graphic/graphic.dart';

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
  testWidgets('bar charts draw intervals', (tester) async {
    await pump(tester, chart('bar'));
    final widget = tester.widget<Chart>(
      find.byWidgetPredicate((w) => w is Chart),
    );
    expect(widget.marks.whereType<IntervalMark>(), hasLength(1));
    expect(widget.marks.whereType<LineMark>(), isEmpty);
    expect(widget.tooltip, isNotNull);
    expect(widget.crosshair, isNotNull);
    expect(widget.selections?.keys, contains('tap'));
  });

  testWidgets('line charts draw a line with points', (tester) async {
    await pump(tester, chart('line'));
    final widget = tester.widget<Chart>(
      find.byWidgetPredicate((w) => w is Chart),
    );
    expect(widget.marks.whereType<LineMark>(), hasLength(1));
    expect(widget.marks.whereType<PointMark>(), hasLength(1));
    expect(widget.marks.whereType<IntervalMark>(), isEmpty);
    expect(find.text('Companies by country'), findsOneWidget);
  });

  testWidgets('empty data shows a placeholder instead of a chart', (
    tester,
  ) async {
    await pump(tester, chart('line', points: const []));
    expect(find.text('No series'), findsOneWidget);
    expect(find.byWidgetPredicate((w) => w is Chart), findsNothing);
  });

  test('long category labels are shortened for the axis', () {
    expect(UiChart.axisLabel('GB'), 'GB');
    expect(UiChart.axisLabel('Construction'), 'Construction');
    expect(UiChart.axisLabel('Civil engineering'), 'Civil engin…');
  });
}
