// CLI tool: dart run tool/rive_schema_gen.dart [--root <dir>]
//
// Walks <root>/assets/rive/ for *-design.riv files and writes a sibling
// *-design.schema.json with at minimum {domain, artboards:[]} per file.
//
// Must stay free of `package:rive` (which imports dart:ui and therefore
// cannot load under a plain Dart VM). Schema enrichment (artboard name
// extraction) is deferred to slice U.4 once a headless rive path is available.

import 'dart:convert';
import 'dart:io';

Future<void> main(List<String> args) async {
  final rootArg = _argValue(args, '--root') ?? Directory.current.path;
  final assetsDir = Directory('$rootArg/assets/rive');
  if (!assetsDir.existsSync()) {
    stdout.writeln('rive_schema_gen: no assets/rive at $rootArg — nothing to do');
    return;
  }

  final files = assetsDir
      .listSync()
      .whereType<File>()
      .where((f) => f.path.endsWith('-design.riv'))
      .toList()
    ..sort((a, b) => a.path.compareTo(b.path));

  if (files.isEmpty) {
    stdout.writeln('rive_schema_gen: no *-design.riv in $assetsDir — nothing to do');
    return;
  }

  for (final file in files) {
    final domain = _domainFromFilename(file.path);
    final schema = _minimalSchema(domain);
    final out = File(file.path.replaceFirst('.riv', '.schema.json'));
    out.writeAsStringSync(const JsonEncoder.withIndent('  ').convert(schema));
    stdout.writeln('rive_schema_gen: wrote ${out.path}');
  }
}

String? _argValue(List<String> args, String key) {
  final i = args.indexOf(key);
  if (i < 0 || i + 1 >= args.length) return null;
  return args[i + 1];
}

String _domainFromFilename(String path) {
  final base = path.split(RegExp(r'[/\\]')).last;
  return base.replaceAll('-design.riv', '');
}

// Minimal schema shape guaranteed by the tool contract.
// Artboard enumeration requires dart:ui (package:rive) and is deferred
// to slice U.4 where a headless flutter_test runner will be used instead.
Map<String, Object?> _minimalSchema(String domain) => {
      'domain': domain,
      'artboards': const <Map<String, Object?>>[],
    };
