import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/screens/shell/shell_synapse_tooltip.dart';
import 'package:ino_flutter/state/brain_inspector_bloc.dart';

void main() {
  testWidgets('tooltip renders from→to header and pretty-printed payload',
      (tester) async {
    const info = PausedSynapseInfo(
      from: 'PlanTrip',
      to: 'FindHotels',
      payload: {'city': 'Tokyo', 'tier': 'mid'},
      gold: false,
      screenX: 100,
      screenY: 80,
    );

    await tester.pumpWidget(MaterialApp(
      home: Scaffold(
        body: Stack(children: const [ShellSynapseTooltip(info: info)]),
      ),
    ));

    expect(find.textContaining('PlanTrip'), findsOneWidget);
    expect(find.textContaining('FindHotels'), findsOneWidget);
    expect(find.textContaining('"city"'), findsOneWidget);
    expect(find.textContaining('Tokyo'), findsOneWidget);
    expect(find.text('click to resume'), findsOneWidget);
  });

  testWidgets('gold variant labels decay as recall', (tester) async {
    const info = PausedSynapseInfo(
      from: 'Preferences',
      to: 'PlanTrip',
      payload: {'ryokanBias': 0.62},
      gold: true,
      screenX: 0,
      screenY: 0,
    );

    await tester.pumpWidget(MaterialApp(
      home: Scaffold(
        body: Stack(children: const [ShellSynapseTooltip(info: info)]),
      ),
    ));

    expect(find.textContaining('· recall'), findsOneWidget);
  });
}
