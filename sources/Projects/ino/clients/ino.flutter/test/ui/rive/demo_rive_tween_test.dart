import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/ui/rive/demo_rive_design_registry.dart';
import 'package:ino_flutter/ui/rive/rive_handles.dart';

// Walks the widget tree under the test environment and returns every Text
// whose data matches '<int>%' — Badge's percentage indicator. AnimatedSwitcher
// keeps both the outgoing and incoming text alive during transitions, so the
// finder may return more than one entry mid-tween.
List<int> ringPercentTexts(WidgetTester tester) => tester
    .widgetList<Text>(find.byType(Text))
    .map((t) => t.data ?? '')
    .where((s) => RegExp(r'^\d+%$').hasMatch(s))
    .map((s) => int.parse(s.replaceAll('%', '')))
    .toList();

void main() {
  testWidgets('Badge ring snaps to target on bare writeNumber (no AnimSpec)',
      (tester) async {
    final registry = DemoRiveDesignRegistry();
    final resolution =
        await registry.resolveController(domain: 'kernel', artboard: 'Badge');
    addTearDown(resolution.dispose);

    resolution.viewModel.writeString('label', 'Streak');
    resolution.viewModel.writeNumber('value0to1', 0);

    await tester.pumpWidget(
      MaterialApp(home: Scaffold(body: resolution.buildWidget())),
    );
    await tester.pump();
    expect(ringPercentTexts(tester), contains(0));

    resolution.viewModel.writeNumber('value0to1', 0.5);
    await tester.pump();
    // Bare write — no animation scheduled, AnimatedSwitcher already swapped.
    expect(ringPercentTexts(tester), contains(50));
  });

  testWidgets(
      'Badge ring interpolates when AnimSpec is supplied: holds start, settles at target',
      (tester) async {
    final registry = DemoRiveDesignRegistry();
    final resolution =
        await registry.resolveController(domain: 'kernel', artboard: 'Badge');
    addTearDown(resolution.dispose);

    resolution.viewModel.writeString('label', 'Budget');
    resolution.viewModel.writeNumber('value0to1', 0);

    await tester.pumpWidget(
      MaterialApp(home: Scaffold(body: resolution.buildWidget())),
    );
    await tester.pump();
    expect(ringPercentTexts(tester), contains(0));

    resolution.viewModel.writeNumber(
      'value0to1',
      0.5,
      anim: const AnimSpec(duration: Duration(milliseconds: 400)),
    );

    // Frame 0 — controller scheduled but tween at start.
    await tester.pump();
    expect(ringPercentTexts(tester).every((p) => p < 10), isTrue,
        reason: 'tween must start near 0%, not snap to 50%');

    // Pump past the tween duration plus the AnimatedSwitcher cross-fade.
    await tester.pump(const Duration(milliseconds: 600));
    expect(ringPercentTexts(tester), contains(50));
  });

  testWidgets(
      'Badge ring takes intermediate values during the tween window',
      (tester) async {
    final registry = DemoRiveDesignRegistry();
    final resolution =
        await registry.resolveController(domain: 'kernel', artboard: 'Badge');
    addTearDown(resolution.dispose);

    resolution.viewModel.writeString('label', 'Budget');
    resolution.viewModel.writeNumber('value0to1', 0);

    await tester.pumpWidget(
      MaterialApp(home: Scaffold(body: resolution.buildWidget())),
    );
    await tester.pump();

    resolution.viewModel.writeNumber(
      'value0to1',
      0.8,
      anim: AnimSpec(
        duration: const Duration(milliseconds: 400),
        curve: Curves.linear,
      ),
    );
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 200));

    // Linear curve at t=0.5 → painter value 0.4 → "40%". Allow drift between
    // 20% and 60% to absorb scheduling jitter and AnimatedSwitcher overlap.
    final mid = ringPercentTexts(tester);
    expect(mid.any((p) => p >= 20 && p <= 60), isTrue,
        reason: 'expected an intermediate ring text in 20–60%, got $mid');
  });

  testWidgets('Spacer height interpolates from 0 to target with AnimSpec',
      (tester) async {
    final registry = DemoRiveDesignRegistry();
    final resolution =
        await registry.resolveController(domain: 'kernel', artboard: 'Spacer');
    addTearDown(resolution.dispose);

    resolution.viewModel.writeString('motif', 'rain');
    resolution.viewModel.writeNumber('height', 0);

    await tester.pumpWidget(
      MaterialApp(home: Scaffold(body: resolution.buildWidget())),
    );
    await tester.pump();

    double spacerHeight() => tester
        .widgetList<SizedBox>(find.byType(SizedBox))
        .where((s) => s.height != null && s.width == double.infinity)
        .first
        .height!;

    expect(spacerHeight(), 0);

    resolution.viewModel.writeNumber(
      'height',
      48,
      anim: AnimSpec(
        duration: const Duration(milliseconds: 600),
        curve: Curves.easeOutCubic,
      ),
    );
    await tester.pump();
    expect(spacerHeight(), lessThan(2),
        reason: 'first frame after tween start should still be near 0');

    await tester.pump(const Duration(milliseconds: 700));
    expect(spacerHeight(), closeTo(48, 0.001));
  });

  test('animSpecFromBindings returns null for null/zero/negative durMs', () {
    expect(animSpecFromBindings(durMs: null, curve: 'easeOut'), isNull);
    expect(animSpecFromBindings(durMs: 0, curve: 'easeOut'), isNull);
    expect(animSpecFromBindings(durMs: -1, curve: 'easeOut'), isNull);
  });

  test('animSpecFromBindings parses known curves and falls back to easeOut',
      () {
    final linearSpec = animSpecFromBindings(durMs: 200, curve: 'linear');
    expect(linearSpec?.duration, const Duration(milliseconds: 200));
    expect(linearSpec?.curve, Curves.linear);

    final unknownSpec = animSpecFromBindings(durMs: 200, curve: 'wibble');
    expect(unknownSpec?.curve, Curves.easeOut);

    final cubicSpec = animSpecFromBindings(durMs: 600, curve: 'easeOutCubic');
    expect(cubicSpec?.curve, Curves.easeOutCubic);
  });
}
