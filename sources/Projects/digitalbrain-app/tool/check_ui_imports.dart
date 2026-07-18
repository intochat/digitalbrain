// src/clients/flutter/tool/check_ui_imports.dart
//
// Dart program (NOT a flutter_test) that walks lib/digital_brain_ui/**
// and fails (exit 1) if any file imports from forbidden areas of the app.
//
// Run: `dart run tool/check_ui_imports.dart`

import 'dart:io';

const _allowedPrefixes = <String>[
  'dart:',
  'package:flutter/',
  'package:material/', // 3.41 standalone
  'package:cupertino/', // 3.41 standalone
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
      // Relative imports inside digital_brain_ui are fine
      // (both './foo.dart' and bare 'foo.dart' — no scheme, no package: prefix).
      if (!uri.contains(':')) continue;
      // Same-package import into digital_brain_ui is fine.
      if (uri.startsWith('package:digitalbrain_flutter/digital_brain_ui/')) continue;
      // Allowed external prefixes.
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
