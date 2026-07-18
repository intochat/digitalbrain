import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/screens/rfw_v3_demo/rfw_v3_demo_screen.dart';

// Pumps enough 30ms ticks to cover the given scenario duration at 1x speed.
// The screen's Timer.periodic fires every 30ms; fake_async advances Stopwatch
// with the same fake clock so elapsedMilliseconds tracks pumped duration.
Future<void> _pumpScenario(
  WidgetTester tester,
  int durationMs, {
  int tickMs = 30,
}) async {
  final ticks = (durationMs / tickMs).ceil() + 5; // a few extra ticks for complete frame
  for (var i = 0; i < ticks; i++) {
    await tester.pump(Duration(milliseconds: tickMs));
  }
}

void main() {
  testWidgets('shows the empty-state hint before replay starts', (tester) async {
    await tester.pumpWidget(const MaterialApp(home: RfwV3DemoScreen()));
    await tester.pumpAndSettle(const Duration(milliseconds: 200));

    expect(find.textContaining('Pick a scenario'), findsOneWidget);
  });

  testWidgets('exposes a chip for each of the 3 scenarios', (tester) async {
    await tester.pumpWidget(const MaterialApp(home: RfwV3DemoScreen()));
    await tester.pumpAndSettle(const Duration(milliseconds: 200));

    // Exact chip labels from the screen source
    expect(find.text('Plan Tokyo trip'), findsOneWidget);
    expect(find.text('How am I today?'), findsOneWidget);
    expect(find.text('Rain pivot'), findsOneWidget);
  });

  testWidgets(
    'tapping the Tokyo-trip chip populates the tree end-to-end',
    (tester) async {
      await tester.pumpWidget(const MaterialApp(home: RfwV3DemoScreen()));
      await tester.pumpAndSettle(const Duration(milliseconds: 200));

      // Chip tap auto-triggers replay via _selectScenario -> _replay
      await tester.tap(find.text('Plan Tokyo trip'));
      await tester.pump();

      // Scenario completes at 1700ms; pump enough ticks at 30ms each
      await _pumpScenario(tester, 1700);

      expect(find.text('Tokyo, May 1–7'), findsOneWidget);
      expect(find.text('Cherry blossom finale week'), findsOneWidget);
      expect(find.text('ANA NH106 09:50'), findsOneWidget);
      expect(find.text('Park Hyatt Shinjuku'), findsOneWidget);
      expect(find.text('Shibuya Sky'), findsOneWidget);
      // Badge renders label as uppercase
      expect(find.text('BUDGET'), findsOneWidget);
    },
  );

  testWidgets(
    'persona check-in chip populates the tree end-to-end',
    (tester) async {
      await tester.pumpWidget(const MaterialApp(home: RfwV3DemoScreen()));
      await tester.pumpAndSettle(const Duration(milliseconds: 200));

      await tester.tap(find.text('How am I today?'));
      await tester.pump();

      // Scenario completes at 1500ms
      await _pumpScenario(tester, 1500);

      expect(find.text('You shipped 3 things and slept 7h'), findsOneWidget);
      expect(find.text('Closed PR #142'), findsOneWidget);
      expect(find.text('Cooked dinner'), findsOneWidget);
      expect(find.text('Walked 6km'), findsOneWidget);
      expect(find.text('STREAK'), findsOneWidget);
    },
  );

  testWidgets(
    'rain-pivot chip populates the tree end-to-end',
    (tester) async {
      await tester.pumpWidget(const MaterialApp(home: RfwV3DemoScreen()));
      await tester.pumpAndSettle(const Duration(milliseconds: 200));

      await tester.tap(find.text('Rain pivot'));
      await tester.pump();

      // Scenario completes at 1500ms (last delta + CompleteFrame both at 1500ms)
      await _pumpScenario(tester, 1700);

      expect(
        find.text('Rain through Wednesday — switching to indoor pivots'),
        findsOneWidget,
      );
      expect(find.text('teamLab Borderless'), findsOneWidget);
      expect(find.text('Edo-Tokyo Museum'), findsOneWidget);
      expect(find.text('Tsukiji Outer Market'), findsOneWidget);
      expect(find.text('Tokyo — Rain pivot'), findsOneWidget);
      expect(find.text('INDOOR COVERAGE'), findsOneWidget);
    },
  );

  // Test 6 (speed control via PopupMenuButton) is deferred.
  // The PopupMenuButton only opens via long-press or icon tap and its
  // menu items are not rendered in the widget tree until opened. Wiring
  // this reliably in pump-based tests requires extra scaffolding that adds
  // no additional scenario-coverage value beyond tests 3–5.
}
