import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/ui/rive/rive_artboard.dart';
import 'package:ino_flutter/ui/rive/rive_design_registry.dart';
import 'package:mocktail/mocktail.dart';
import '_fakes.dart';

class MockRegistry extends Mock implements RiveDesignRegistry {}

void main() {
  setUpAll(registerRiveFallbacks);

  testWidgets('mounts the resolved widget once registry resolves the artboard', (
    tester,
  ) async {
    final registry = MockRegistry();
    final resolution = MockRiveResolution();

    when(
      () => registry.resolveController(domain: 'kernel', artboard: 'Hero'),
    ).thenAnswer((_) async => resolution);
    // viewModel must be stubbed — non-nullable getter would throw on mocktail null.
    // bindings is empty so _applyBindings returns after the null-guard never fires.
    when(() => resolution.viewModel).thenReturn(MockViewModelHandle());
    when(
      () => resolution.buildWidget(),
    ).thenReturn(const SizedBox(key: ValueKey('resolved-widget')));
    when(() => resolution.dispose()).thenAnswer((_) {});

    await tester.pumpWidget(
      MaterialApp(
        home: RiveArtboard(
          registry: registry,
          domain: 'kernel',
          artboard: 'Hero',
          bindings: const {},
          triggers: const {},
        ),
      ),
    );
    await tester.pump();

    expect(find.byKey(const ValueKey('resolved-widget')), findsOneWidget);
  });

  testWidgets('writes string, number, and color bindings to the ViewModel', (
    tester,
  ) async {
    final registry = MockRegistry();
    final resolution = MockRiveResolution();
    final vm = MockViewModelHandle();

    when(
      () => registry.resolveController(domain: 'kernel', artboard: 'Hero'),
    ).thenAnswer((_) async => resolution);
    when(() => resolution.viewModel).thenReturn(vm);
    when(() => resolution.buildWidget()).thenReturn(const SizedBox.shrink());
    when(() => resolution.dispose()).thenAnswer((_) {});

    await tester.pumpWidget(
      MaterialApp(
        home: RiveArtboard(
          registry: registry,
          domain: 'kernel',
          artboard: 'Hero',
          bindings: const {
            'title': 'Tokyo',
            'index': 3,
            'tint': Color(0xFFFF0000),
          },
          triggers: const {},
        ),
      ),
    );
    await tester.pump();

    verify(() => vm.writeString('title', 'Tokyo')).called(1);
    verify(() => vm.writeNumber('index', 3.0)).called(1);
    verify(() => vm.writeColor('tint', const Color(0xFFFF0000))).called(1);
  });

  testWidgets('re-applies bindings when widget rebuilds with new map', (
    tester,
  ) async {
    final registry = MockRegistry();
    final resolution = MockRiveResolution();
    final vm = MockViewModelHandle();

    when(
      () => registry.resolveController(domain: 'kernel', artboard: 'Hero'),
    ).thenAnswer((_) async => resolution);
    when(() => resolution.viewModel).thenReturn(vm);
    when(() => resolution.buildWidget()).thenReturn(const SizedBox.shrink());
    when(() => resolution.dispose()).thenAnswer((_) {});

    Widget host(Map<String, Object?> bindings) => MaterialApp(
      home: RiveArtboard(
        registry: registry,
        domain: 'kernel',
        artboard: 'Hero',
        bindings: bindings,
        triggers: const {},
      ),
    );

    await tester.pumpWidget(host(const {'title': 'Tokyo'}));
    await tester.pump();
    await tester.pumpWidget(host(const {'title': 'Paris'}));
    await tester.pump();

    verify(() => vm.writeString('title', 'Tokyo')).called(1);
    verify(() => vm.writeString('title', 'Paris')).called(1);
  });

  testWidgets('wires VoidCallback triggers via ViewModelHandle.onTrigger', (
    tester,
  ) async {
    final registry = MockRegistry();
    final resolution = MockRiveResolution();
    final vm = MockViewModelHandle();

    when(
      () => registry.resolveController(domain: 'kernel', artboard: 'Hero'),
    ).thenAnswer((_) async => resolution);
    when(() => resolution.viewModel).thenReturn(vm);
    when(() => resolution.buildWidget()).thenReturn(const SizedBox.shrink());
    when(() => resolution.dispose()).thenAnswer((_) {});

    VoidCallback? captured;
    when(() => vm.onTrigger(any(), any())).thenAnswer((inv) {
      captured = inv.positionalArguments[1] as VoidCallback;
    });

    var fired = 0;
    await tester.pumpWidget(
      MaterialApp(
        home: RiveArtboard(
          registry: registry,
          domain: 'kernel',
          artboard: 'Hero',
          bindings: const {},
          triggers: {'tap': () => fired++},
        ),
      ),
    );
    await tester.pump();

    verify(() => vm.onTrigger('tap', any())).called(1);
    expect(captured, isNotNull);
    captured!();
    expect(fired, 1);
  });
}
