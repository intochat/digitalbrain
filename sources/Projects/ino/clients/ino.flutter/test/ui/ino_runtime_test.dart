import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/ui/ino_runtime.dart';
import 'package:ino_flutter/ui/rive/rive_design_registry.dart';
import 'package:mocktail/mocktail.dart';
import 'package:rfw/rfw.dart';

class MockRegistry extends Mock implements RiveDesignRegistry {}

void main() {
  test('createInoRuntime exposes ino.rive after registry injection', () {
    final runtime = createInoRuntime(riveRegistry: MockRegistry());
    final lib = runtime.libraryNamed(const LibraryName(['ino', 'rive']));
    expect(lib, isNotNull);
  });

  test('createInoRuntime without registry has no ino.rive library', () {
    final runtime = createInoRuntime();
    final lib = runtime.libraryNamed(const LibraryName(['ino', 'rive']));
    expect(lib, isNull);
  });
}
