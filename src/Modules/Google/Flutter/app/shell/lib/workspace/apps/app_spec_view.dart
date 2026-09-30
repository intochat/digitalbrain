import 'package:flutter/material.dart';

Map<String, dynamic> _map(dynamic value) =>
    value is Map ? Map<String, dynamic>.from(value) : <String, dynamic>{};

List<Map<String, dynamic>> _list(dynamic value) =>
    value is List ? value.map(_map).toList() : <Map<String, dynamic>>[];

const _scenarioHeading = '## Scenario:';

// An app as its spec: the plain-language text the Author wrote, with the verdict of the last test
// run next to every scenario heading and the failure message under a red one.
class AppSpecView extends StatelessWidget {
  const AppSpecView({super.key, required this.spec, this.run});

  // The spec's Markdown text and the AppTestRun as the brain serializes it.
  final String spec;
  final Map<String, dynamic>? run;

  @override
  Widget build(BuildContext context) {
    final verdicts = {
      for (final scenario in _list(run?['scenarios']))
        '${scenario['name']}': scenario,
    };
    final children = <Widget>[];
    for (final rawLine in spec.split('\n')) {
      final line = rawLine.trimRight();
      if (line.startsWith(_scenarioHeading)) {
        final name = line.substring(_scenarioHeading.length).trim();
        final verdict = verdicts[name];
        children.add(
          Padding(
            padding: const EdgeInsets.only(top: 12),
            child: Row(
              children: [
                _VerdictDot(verdict: verdict),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    'Scenario: $name',
                    key: ValueKey('scenario-$name'),
                    style: const TextStyle(fontWeight: FontWeight.w600),
                  ),
                ),
              ],
            ),
          ),
        );
        final message = '${verdict?['message'] ?? ''}';
        if (verdict?['passed'] == false && message.isNotEmpty) {
          children.add(
            Padding(
              padding: const EdgeInsets.only(left: 18, top: 2),
              child: Text(
                message,
                key: ValueKey('scenario-message-$name'),
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
            ),
          );
        }
      } else if (line.startsWith('# ')) {
        children.add(
          Padding(
            padding: const EdgeInsets.only(bottom: 4),
            child: Text(
              line.substring(2).trim(),
              style: Theme.of(context).textTheme.titleMedium,
            ),
          ),
        );
      } else if (line.isNotEmpty) {
        children.add(Text(line));
      } else {
        children.add(const SizedBox(height: 8));
      }
    }
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: children,
    );
  }
}

class _VerdictDot extends StatelessWidget {
  const _VerdictDot({required this.verdict});

  final Map<String, dynamic>? verdict;
  static const double size = 10;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final (color, label) = switch (verdict?['passed']) {
      true => (Colors.green, 'Passed'),
      false => (scheme.error, 'Failed'),
      _ => (scheme.outlineVariant, 'Not run yet'),
    };
    return Tooltip(
      message: label,
      child: Container(
        width: size,
        height: size,
        decoration: BoxDecoration(color: color, shape: BoxShape.circle),
      ),
    );
  }
}
