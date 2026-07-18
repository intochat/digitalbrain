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
  // Fallback: flutter injects FLUTTER_ROOT; dart lives inside the bundled SDK.
  final flutterRoot = Platform.environment['FLUTTER_ROOT'];
  if (flutterRoot != null) {
    final exe = File(
      '$flutterRoot/bin/cache/dart-sdk/bin/dart${Platform.isWindows ? '.exe' : ''}',
    );
    if (exe.existsSync()) return exe.path;
  }
  return 'dart'; // last resort — works when dart is on PATH
}

Future<ProcessResult> _runTool(List<String> extraArgs) => Process.run(
      _dartExe,
      ['run', 'tool/rive_schema_gen.dart', ...extraArgs],
      workingDirectory: Directory.current.path,
    );

void main() {
  test('exits cleanly when no *-design.riv files are present', () async {
    final tempDir = await Directory.systemTemp.createTemp('rive_schema_test');
    addTearDown(() => tempDir.delete(recursive: true));
    Directory('${tempDir.path}/assets/rive').createSync(recursive: true);

    final result = await _runTool(['--root', tempDir.path]);
    expect(result.exitCode, 0, reason: result.stderr.toString());
  });

  test('exits cleanly when assets/rive directory does not exist', () async {
    final tempDir = await Directory.systemTemp.createTemp('rive_schema_no_assets');
    addTearDown(() => tempDir.delete(recursive: true));

    final result = await _runTool(['--root', tempDir.path]);
    expect(result.exitCode, 0, reason: result.stderr.toString());
  });

  test('emits a schema next to a staged *-design.riv (or skips with exit 0)',
      () async {
    final tempDir = await Directory.systemTemp.createTemp('rive_schema_real');
    addTearDown(() => tempDir.delete(recursive: true));
    final assetsDir = Directory('${tempDir.path}/assets/rive')
      ..createSync(recursive: true);

    // Stage a .riv if one exists; this may or may not trigger heavyweight
    // schema enumeration depending on whether the rive Dart API exposes it.
    // The contract is "if a *-design.riv is present, write a sibling
    // *-design.schema.json with at minimum {domain, artboards:[]}".
    final candidate = File('assets/rive/persona_orb.riv');
    if (!candidate.existsSync()) return;

    final stagedName = 'kernel-design.riv';
    candidate.copySync('${assetsDir.path}/$stagedName');

    final result = await _runTool(['--root', tempDir.path]);
    expect(result.exitCode, 0, reason: result.stderr.toString());

    final schemaFile = File('${assetsDir.path}/kernel-design.schema.json');
    expect(schemaFile.existsSync(), isTrue);
    final schema = jsonDecode(schemaFile.readAsStringSync()) as Map;
    expect(schema['domain'], 'kernel');
    expect(schema['artboards'], isA<List>());
  });
}
