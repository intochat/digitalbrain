import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('chart title is visible', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: UiChart(
            part: UiChartPart(
              title: 'E2E BTC chart',
              chartKind: 'line',
              points: [UiChartPoint(label: 'open', value: 64000)],
            ),
          ),
        ),
      ),
    );
    expect(find.text('E2E BTC chart'), findsOneWidget);
  });

  testWidgets('video shows url', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: UiVideo(
            part: UiVideoPart(
              name: 'e2e',
              url: 'https://example.com/intro.mp4',
              playing: true,
            ),
          ),
        ),
      ),
    );
    expect(find.text('Playing'), findsOneWidget);
    expect(find.text('https://example.com/intro.mp4'), findsOneWidget);
  });

  testWidgets('browser shows title and uri', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: UiWebBrowser(
            part: UiWebBrowserPart(
              name: 'e2e',
              title: 'WinUI',
              uri: 'https://learn.microsoft.com/',
            ),
          ),
        ),
      ),
    );
    expect(find.text('WinUI'), findsOneWidget);
    expect(find.text('https://learn.microsoft.com/'), findsOneWidget);
  });

  testWidgets('expander shows collapsed status', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: UiExpander(
            part: UiExpanderPart(name: 'e2e', header: 'More', expanded: false),
          ),
        ),
      ),
    );
    expect(find.text('More'), findsOneWidget);
    expect(find.text('Collapsed'), findsOneWidget);
  });
}
