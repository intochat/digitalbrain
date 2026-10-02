import 'app_document.dart';

class SpecDocument {
  const SpecDocument(this.original, this.preamble, this.scenarios);
  final String original, preamble;
  final List<AppScenario> scenarios;
}

SpecDocument parseSpec(String raw) {
  final preamble = StringBuffer();
  var body = StringBuffer();
  final scenarios = <AppScenario>[];
  String? name, fence;
  var live = false;
  void flush() {
    if (name == null) return;
    scenarios.add(
      AppScenario(
        id: 'legacy-${scenarios.length}',
        name: name,
        body: body.toString(),
        isLive: live,
      ),
    );
    body = StringBuffer();
  }

  for (final match in RegExp(r'[^\n]*\n|[^\n]+$').allMatches(raw)) {
    final line = match.group(0)!;
    final trimmed = line.trimLeft();
    if (trimmed.startsWith('```') || trimmed.startsWith('~~~')) {
      fence = fence == null
          ? trimmed.substring(0, 3)
          : trimmed.startsWith(fence)
          ? null
          : fence;
    }
    final heading = fence == null
        ? RegExp(r'^\s*##\s+Scenario:\s*(\S[^\r\n]*)').firstMatch(line)
        : null;
    if (heading == null) {
      (name == null ? preamble : body).write(line);
    } else {
      flush();
      name = heading.group(1)!.trim();
      live = name.endsWith(' (live)');
      if (live) name = name.substring(0, name.length - 7);
    }
  }
  flush();
  return SpecDocument(raw, preamble.toString(), scenarios);
}
