import 'package:digitalbrain_flutter_shell/behaviors/behavior_overview.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/shell_test_support.dart';

void main() {
  testWidgets('overview shows purpose without source and exposes Stop with cancel copy', (
    tester,
  ) async {
    await prepareShellSurface(tester);

    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: BehaviorOverviewView(
            document: shellBehaviorDocument(runState: 'Running', gate: true),
            onStop: () {},
            onRunOnce: () {},
            onAskAssistant: () {},
          ),
        ),
      ),
    );

    expect(find.byKey(const Key('behavior_overview')), findsOneWidget);
    expect(find.textContaining('Account enrichment'), findsWidgets);
    expect(find.textContaining('enrich account from email'), findsWidgets);
    expect(find.byKey(const Key('behavior_stop')), findsOneWidget);
    expect(find.byKey(const Key('behavior_stop_confirm')), findsOneWidget);
    expect(
      find.textContaining('cancels active Tasks'),
      findsOneWidget,
    );
    expect(find.textContaining('class '), findsNothing);
  });

  testWidgets('stopped overview shows Start', (tester) async {
    await prepareShellSurface(tester);
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: BehaviorOverviewView(
            document: shellBehaviorDocument(runState: 'Stopped', gate: false),
            onStart: () {},
          ),
        ),
      ),
    );
    expect(find.byKey(const Key('behavior_start')), findsOneWidget);
    expect(find.byKey(const Key('behavior_stop')), findsNothing);
  });
}
