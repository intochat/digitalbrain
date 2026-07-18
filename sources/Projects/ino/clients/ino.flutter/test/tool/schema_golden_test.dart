import 'dart:convert';
import 'dart:io';
import 'package:flutter_test/flutter_test.dart';

// flutter test runs under flutter_tester.exe, not dart.exe.
// Locate dart.exe via the DART_SDK_PATH env var injected by the flutter tool.
String get _dartExe {
  final sdkPath = Platform.environment['DART_SDK_PATH'];
  if (sdkPath != null) {
    final exe = File('$sdkPath/bin/dart${Platform.isWindows ? '.exe' : ''}');
    if (exe.existsSync()) return exe.path;
  }
  final flutterRoot = Platform.environment['FLUTTER_ROOT'];
  if (flutterRoot != null) {
    final exe = File(
      '$flutterRoot/bin/cache/dart-sdk/bin/dart${Platform.isWindows ? '.exe' : ''}',
    );
    if (exe.existsSync()) return exe.path;
  }
  return 'dart';
}

void main() {
  test('committed ino-design.schema.json matches the generator output',
      () async {
    final asset = File('assets/rive/ino-design.riv');
    final committed = File('assets/rive/ino-design.schema.json');
    if (!asset.existsSync()) {
      markTestSkipped('ino-design.riv not present yet');
      return;
    }

    final tempDir = await Directory.systemTemp.createTemp('schema_golden');
    addTearDown(() => tempDir.delete(recursive: true));
    final tempAssets = Directory('${tempDir.path}/assets/rive')
      ..createSync(recursive: true);
    asset.copySync('${tempAssets.path}/ino-design.riv');

    final result = await Process.run(
      _dartExe,
      ['run', 'tool/rive_schema_gen.dart', '--root', tempDir.path],
      workingDirectory: Directory.current.path,
    );
    expect(result.exitCode, 0, reason: result.stderr.toString());

    final regenerated = jsonDecode(
        File('${tempAssets.path}/ino-design.schema.json').readAsStringSync());
    final onDisk = jsonDecode(committed.readAsStringSync());
    expect(regenerated, equals(onDisk));
  });
}
