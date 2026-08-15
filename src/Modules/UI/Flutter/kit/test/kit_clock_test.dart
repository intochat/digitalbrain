import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('KitClock counts down toward the due instant', (tester) async {
    final part = KitTimerPart(
      label: 'tea in five',
      dueAt: DateTime.now().toUtc().add(const Duration(minutes: 5)),
    );

    await tester.pumpWidget(MaterialApp(home: KitClock(part: part)));

    expect(find.text('tea in five'), findsOneWidget);
    expect(find.byKey(const Key('kit_clock_remaining')), findsOneWidget);

    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('KitClock shows zero once the due instant has passed', (
    tester,
  ) async {
    final part = KitTimerPart(
      label: 'tea',
      dueAt: DateTime.now().toUtc().subtract(const Duration(minutes: 1)),
    );

    await tester.pumpWidget(MaterialApp(home: KitClock(part: part)));

    expect(find.text('00:00'), findsOneWidget);

    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('KitClock without a timer part shows the wall clock face', (
    tester,
  ) async {
    await tester.pumpWidget(const MaterialApp(home: KitClock()));

    expect(find.byKey(const Key('kit_clock_face')), findsOneWidget);

    await tester.pumpWidget(const SizedBox());
  });
}
