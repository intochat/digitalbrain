import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/ui/rive/rive_artboard.dart';
import 'package:ino_flutter/ui/rive/rive_design_registry.dart';
import 'package:ino_flutter/ui/rive/rive_widgets.dart';
import 'package:mocktail/mocktail.dart';
import 'package:rfw/formats.dart' show parseLibraryFile;
import 'package:rfw/rfw.dart';

import '_fakes.dart';

class MockRegistry extends Mock implements RiveDesignRegistry {}

void main() {
  setUpAll(registerRiveFallbacks);

  testWidgets('Hero wrapper resolves to a RiveArtboard with kernel domain', (
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

    final runtime = Runtime()
      ..update(const LibraryName(['core', 'widgets']), createCoreWidgets())
      ..update(
        const LibraryName(['ino', 'rive']),
        createRiveWidgets(registry),
      );

    final data = DynamicContent({'title': 'Tokyo'});

    final remote = parseLibraryFile('''
      import core.widgets;
      import ino.rive;
      widget root = Hero(domain: "kernel", title: data.title);
    ''');
    runtime.update(const LibraryName(['main']), remote);

    await tester.pumpWidget(
      MaterialApp(
        home: RemoteWidget(
          runtime: runtime,
          data: data,
          widget: const FullyQualifiedWidgetName(
            LibraryName(['main']),
            'root',
          ),
        ),
      ),
    );
    await tester.pump();

    expect(find.byType(RiveArtboard), findsOneWidget);
    verify(
      () => registry.resolveController(domain: 'kernel', artboard: 'Hero'),
    ).called(1);
  });

  testWidgets('Tile wrapper resolves to a RiveArtboard with the Tile artboard',
      (tester) async {
    final registry = MockRegistry();
    final resolution = MockRiveResolution();
    final vm = MockViewModelHandle();

    when(() => registry.resolveController(domain: 'kernel', artboard: 'Tile'))
        .thenAnswer((_) async => resolution);
    when(() => resolution.viewModel).thenReturn(vm);
    when(() => resolution.buildWidget()).thenReturn(const SizedBox.shrink());
    when(() => resolution.dispose()).thenAnswer((_) {});
    when(() => vm.writeString(any(), any())).thenAnswer((_) {});
    when(() => vm.writeNumber(any(), any())).thenAnswer((_) {});
    when(() => vm.writeColor(any(), any())).thenAnswer((_) {});
    when(() => vm.onTrigger(any(), any())).thenAnswer((_) {});
    when(() => vm.dispose()).thenAnswer((_) {});

    final runtime = Runtime()
      ..update(const LibraryName(['core', 'widgets']), createCoreWidgets())
      ..update(const LibraryName(['ino', 'rive']), createRiveWidgets(registry));

    final data = DynamicContent({'k': 'flight', 'a': 'Itami'});
    runtime.update(const LibraryName(['main']), parseLibraryFile('''
    import core.widgets;
    import ino.rive;
    widget root = Tile(domain: "kernel", kind: data.k, line1: data.a);
  '''));

    await tester.pumpWidget(MaterialApp(
      home: RemoteWidget(
        runtime: runtime,
        data: data,
        widget: const FullyQualifiedWidgetName(LibraryName(['main']), 'root'),
      ),
    ));
    await tester.pump();

    expect(find.byType(RiveArtboard), findsOneWidget);
    verify(() => registry.resolveController(domain: 'kernel', artboard: 'Tile'))
        .called(1);
  });

  testWidgets(
      'Badge wrapper resolves to a RiveArtboard with the Badge artboard',
      (tester) async {
    final registry = MockRegistry();
    final resolution = MockRiveResolution();
    final vm = MockViewModelHandle();

    when(() => registry.resolveController(domain: 'kernel', artboard: 'Badge'))
        .thenAnswer((_) async => resolution);
    when(() => resolution.viewModel).thenReturn(vm);
    when(() => resolution.buildWidget()).thenReturn(const SizedBox.shrink());
    when(() => resolution.dispose()).thenAnswer((_) {});
    when(() => vm.writeString(any(), any())).thenAnswer((_) {});
    when(() => vm.writeNumber(any(), any())).thenAnswer((_) {});
    when(() => vm.writeColor(any(), any())).thenAnswer((_) {});
    when(() => vm.dispose()).thenAnswer((_) {});

    final runtime = Runtime()
      ..update(const LibraryName(['core', 'widgets']), createCoreWidgets())
      ..update(const LibraryName(['ino', 'rive']), createRiveWidgets(registry));

    final data = DynamicContent({'l': 'urgent', 'v': 0.7});
    runtime.update(const LibraryName(['main']), parseLibraryFile('''
    import core.widgets;
    import ino.rive;
    widget root = Badge(domain: "kernel", label: data.l, value0to1: data.v);
  '''));

    await tester.pumpWidget(MaterialApp(
      home: RemoteWidget(
        runtime: runtime,
        data: data,
        widget: const FullyQualifiedWidgetName(LibraryName(['main']), 'root'),
      ),
    ));
    await tester.pump();

    expect(find.byType(RiveArtboard), findsOneWidget);
    verify(
            () => registry.resolveController(domain: 'kernel', artboard: 'Badge'))
        .called(1);
  });

  testWidgets(
      'PersonaInline wrapper resolves to a RiveArtboard with the PersonaInline artboard',
      (tester) async {
    final registry = MockRegistry();
    final resolution = MockRiveResolution();
    final vm = MockViewModelHandle();

    when(() => registry.resolveController(
          domain: 'kernel', artboard: 'PersonaInline'))
        .thenAnswer((_) async => resolution);
    when(() => resolution.viewModel).thenReturn(vm);
    when(() => resolution.buildWidget()).thenReturn(const SizedBox.shrink());
    when(() => resolution.dispose()).thenAnswer((_) {});
    when(() => vm.writeString(any(), any())).thenAnswer((_) {});
    when(() => vm.writeNumber(any(), any())).thenAnswer((_) {});
    when(() => vm.onTrigger(any(), any())).thenAnswer((_) {});
    when(() => vm.dispose()).thenAnswer((_) {});

    final runtime = Runtime()
      ..update(const LibraryName(['core', 'widgets']), createCoreWidgets())
      ..update(const LibraryName(['ino', 'rive']), createRiveWidgets(registry));

    final data = DynamicContent({'m': 'curious', 'e': 0.5});
    runtime.update(const LibraryName(['main']), parseLibraryFile('''
    import core.widgets;
    import ino.rive;
    widget root = PersonaInline(domain: "kernel", mood: data.m, energy: data.e);
  '''));

    await tester.pumpWidget(MaterialApp(
      home: RemoteWidget(
        runtime: runtime,
        data: data,
        widget: const FullyQualifiedWidgetName(LibraryName(['main']), 'root'),
      ),
    ));
    await tester.pump();

    expect(find.byType(RiveArtboard), findsOneWidget);
    verify(() => registry.resolveController(
          domain: 'kernel', artboard: 'PersonaInline'))
        .called(1);
  });

  testWidgets(
      'Spacer wrapper resolves to a RiveArtboard with the Spacer artboard',
      (tester) async {
    final registry = MockRegistry();
    final resolution = MockRiveResolution();
    final vm = MockViewModelHandle();

    when(() =>
            registry.resolveController(domain: 'kernel', artboard: 'Spacer'))
        .thenAnswer((_) async => resolution);
    when(() => resolution.viewModel).thenReturn(vm);
    when(() => resolution.buildWidget()).thenReturn(const SizedBox.shrink());
    when(() => resolution.dispose()).thenAnswer((_) {});
    when(() => vm.writeString(any(), any())).thenAnswer((_) {});
    when(() => vm.writeNumber(any(), any())).thenAnswer((_) {});
    when(() => vm.dispose()).thenAnswer((_) {});

    final runtime = Runtime()
      ..update(const LibraryName(['core', 'widgets']), createCoreWidgets())
      ..update(const LibraryName(['ino', 'rive']), createRiveWidgets(registry));

    final data = DynamicContent({'h': 24, 'm': 'wave'});
    // rfw resolves widget-name collisions by first-import-wins. core.widgets
    // also exports Spacer, so ino.rive must be imported first for ours to win.
    runtime.update(const LibraryName(['main']), parseLibraryFile('''
    import ino.rive;
    import core.widgets;
    widget root = Spacer(domain: "kernel", height: data.h, motif: data.m);
  '''));

    await tester.pumpWidget(MaterialApp(
      home: RemoteWidget(
        runtime: runtime,
        data: data,
        widget: const FullyQualifiedWidgetName(LibraryName(['main']), 'root'),
      ),
    ));
    await tester.pump();

    expect(find.byType(RiveArtboard), findsOneWidget);
    verify(() =>
            registry.resolveController(domain: 'kernel', artboard: 'Spacer'))
        .called(1);
  });
}
