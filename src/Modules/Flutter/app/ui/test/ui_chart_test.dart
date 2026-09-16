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

Future<void> pump(
  WidgetTester tester,
  UiChartPart part, {
  Brightness brightness = Brightness.light,
}) async {
  tester.view.physicalSize = const Size(800, 600);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  await tester.pumpWidget(
    MaterialApp(
      theme: UiTheme.workspace(brightness),
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
    expect(find.byType(PieChart), findsNothing);
    expect(find.text('Companies by country'), findsOneWidget);
  });

  testWidgets('line charts draw a LineChart', (tester) async {
    await pump(tester, chart('line'));
    expect(find.byType(LineChart), findsOneWidget);
    expect(find.byType(BarChart), findsNothing);
    expect(find.byType(PieChart), findsNothing);
    expect(find.text('Companies by country'), findsOneWidget);
  });

  testWidgets('pie charts draw percents and a value legend', (tester) async {
    await pump(tester, chart('pie'));
    expect(find.byType(PieChart), findsOneWidget);
    expect(find.byType(BarChart), findsNothing);
    expect(find.byType(LineChart), findsNothing);
    expect(find.text('Companies by country'), findsOneWidget);
    final pie = tester.widget<PieChart>(find.byType(PieChart));
    expect(pie.data.centerSpaceColor, LumenPalette.surface);
    expect(
      pie.data.sections.map((section) => section.title),
      ['37%', '33%', '30%'],
    );
    expect(find.text('GB  11'), findsOneWidget);
    expect(find.text('DE  10'), findsOneWidget);
    expect(find.text('CZ  9'), findsOneWidget);
  });

  testWidgets('light theme keeps the pie card on Lumen white', (tester) async {
    await pump(tester, chart('pie'), brightness: Brightness.light);
    final card = tester.widget<DecoratedBox>(
      find.byKey(const Key('ui_chart_Companies by country')),
    );
    final decoration = card.decoration as BoxDecoration;
    expect(decoration.color, LumenPalette.surface);
    final pie = tester.widget<PieChart>(find.byType(PieChart));
    expect(pie.data.centerSpaceColor, LumenPalette.surface);
  });

  test('pie percent labels round from the series total', () {
    expect(UiChart.percentLabel(11, 30), '37%');
    expect(UiChart.percentLabel(10, 30), '33%');
    expect(UiChart.percentLabel(0, 0), '0%');
  });

  testWidgets('empty data shows a placeholder instead of a chart', (
    tester,
  ) async {
    await pump(tester, chart('line', points: const []));
    expect(find.text('No series'), findsOneWidget);
    expect(find.byType(BarChart), findsNothing);
    expect(find.byType(LineChart), findsNothing);
    expect(find.byType(PieChart), findsNothing);
  });

  test('long category labels are shortened for the axis', () {
    expect(UiChart.axisLabel('GB'), 'GB');
    expect(UiChart.axisLabel('Construction'), 'Construction');
    expect(UiChart.axisLabel('Civil engineering'), 'Civil engin…');
  });
}
