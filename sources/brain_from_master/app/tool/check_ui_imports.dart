import 'dart:io';

const _allowedPrefixes = <String>[
  'dart:',
  'package:flutter/',
  'package:material/',
  'package:cupertino/',
  'package:meta/',
  'package:vector_math/',
  'package:digital_brain_sdk_flutter/',
];

const _forbiddenAppDirs = <String>[
  'package:digitalbrain_flutter/features/',
  'package:digitalbrain_flutter/widgets/',
  'package:digitalbrain_flutter/grpc/',
  'package:digitalbrain_flutter/theme/',
  'package:digitalbrain_flutter/shell/',
  'package:digitalbrain_flutter/rfw_host/',
  'package:digitalbrain_flutter/telemetry/',
  'package:digitalbrain_flutter/router.dart',
  'package:digitalbrain_flutter/app.dart',
  'package:digitalbrain_flutter/main.dart',
];

Future<int> main(List<String> args) async {
  final root = Directory('lib/digital_brain_ui');
  if (!await root.exists()) {
    stderr.writeln('Boundary check: ${root.path} does not exist.');
    return 1;
  }
  final violations = <String>[];
  await for (final entity in root.list(recursive: true)) {
    if (entity is! File || !entity.path.endsWith('.dart')) continue;
    final lines = await entity.readAsLines();
    for (var i = 0; i < lines.length; i++) {
      final line = lines[i].trimLeft();
      if (!line.startsWith('import ')) continue;
      final match = RegExp("import\\s+['\"](.+?)['\"]").firstMatch(line);
      if (match == null) continue;
      final uri = match.group(1)!;

      if (!uri.contains(':')) continue;

      if (uri.startsWith('package:digitalbrain_flutter/digital_brain_ui/')) {
        continue;
      }

      final allowed = _allowedPrefixes.any(uri.startsWith);
      final forbidden = _forbiddenAppDirs.any(uri.startsWith);
      if (forbidden || !allowed) {
        violations.add('${entity.path}:${i + 1}  $uri');
      }
    }
  }
  if (violations.isEmpty) {
    stdout.writeln('Boundary check: OK');
    return 0;
  }
  stderr.writeln(
    'Boundary check: FAIL — forbidden imports in digital_brain_ui:',
  );
  for (final v in violations) {
    stderr.writeln('  $v');
  }
  return 1;
}
