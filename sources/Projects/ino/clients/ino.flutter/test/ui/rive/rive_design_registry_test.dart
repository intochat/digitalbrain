import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/ui/rive/rive_design_registry.dart';
import 'package:mocktail/mocktail.dart';

import '_fakes.dart';

class MockFileLoader extends Mock implements RiveFileLoader {}

void main() {
  setUpAll(registerRiveFallbacks);

  test('preloads kernel baseline on construction', () async {
    final loader = MockFileLoader();
    final fakeFile = FakeRiveFile();
    when(
      () => loader.load('assets/rive/ino-design.riv'),
    ).thenAnswer((_) async => fakeFile);

    final registry = AssetRiveDesignRegistry(loader: loader);
    await registry.ready;

    verify(() => loader.load('assets/rive/ino-design.riv')).called(1);
  });

  test('resolveController falls back to kernel for unknown domain', () async {
    final loader = MockFileLoader();
    final kernelFile = FakeRiveFile();
    when(
      () => loader.load('assets/rive/ino-design.riv'),
    ).thenAnswer((_) async => kernelFile);
    when(
      () => loader.load('assets/rive/unknown-design.riv'),
    ).thenAnswer((_) async => null);

    final registry = AssetRiveDesignRegistry(loader: loader);
    await registry.ready;

    expect(
      registry.resolvedFileFor(domain: 'unknown', artboard: 'Hero'),
      same(kernelFile),
    );
  });

  test(
    'resolveController throws StateError when neither domain nor kernel file is available',
    () async {
      final loader = MockFileLoader();
      when(
        () => loader.load('assets/rive/ino-design.riv'),
      ).thenAnswer((_) async => null);
      when(
        () => loader.load('assets/rive/ghost-design.riv'),
      ).thenAnswer((_) async => null);

      final registry = AssetRiveDesignRegistry(loader: loader);
      await registry.ready;

      expect(
        () => registry.resolveController(domain: 'ghost', artboard: 'Hero'),
        throwsStateError,
      );
    },
  );
}
