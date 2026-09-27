import 'package:flutter/material.dart';

// Numeric values of DigitalBrain.Specs.Verdict on the wire.
const _passed = 0;
const _failed = 1;
const _unbound = 2;

Map<String, dynamic> _map(dynamic value) =>
    value is Map ? Map<String, dynamic>.from(value) : <String, dynamic>{};

List<Map<String, dynamic>> _list(dynamic value) =>
    value is List ? value.map(_map).toList() : <Map<String, dynamic>>[];

// An app as its scenarios: each step with its keyword and parameters highlighted, steps the brain does
// not understand greyed out, and the verdict of the last verification next to every scenario and step.
class AppSpecView extends StatelessWidget {
  const AppSpecView({super.key, required this.feature, this.run});

  // FeatureSnapshot and FeatureRun as the brain serializes them.
  final Map<String, dynamic> feature;
  final Map<String, dynamic>? run;

  @override
  Widget build(BuildContext context) {
    final problem = _map(feature['problem']);
    final results = {
      for (final scenario in _list(run?['scenarios'])) scenario['line']: scenario,
    };
    final background = _list(feature['background']);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          'Feature: ${feature['name'] ?? ''}',
          style: Theme.of(context).textTheme.titleMedium,
        ),
        if (problem.isNotEmpty)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 8),
            child: Text(
              'Line ${problem['line']}: ${problem['message']}',
              key: const ValueKey('spec-problem'),
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
          ),
        if (background.isNotEmpty)
          _ScenarioBlock(title: 'Background', steps: background, result: null),
        for (final scenario in _list(feature['scenarios']))
          _ScenarioBlock(
            title: 'Scenario: ${scenario['name']}',
            tags: (scenario['tags'] as List? ?? const []).cast<Object?>(),
            steps: _list(scenario['steps']),
            result: results[scenario['line']],
          ),
      ],
    );
  }
}

class _ScenarioBlock extends StatelessWidget {
  const _ScenarioBlock({
    required this.title,
    required this.steps,
    required this.result,
    this.tags = const [],
  });

  final String title;
  final List<Map<String, dynamic>> steps;
  final Map<String, dynamic>? result;
  final List<Object?> tags;

  @override
  Widget build(BuildContext context) {
    final stepResults = {
      for (final step in _list(result?['steps'])) step['line']: step,
    };
    return Padding(
      padding: const EdgeInsets.only(top: 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              _VerdictDot(verdict: result?['verdict'] as int?),
              const SizedBox(width: 8),
              Expanded(
                child: Text(
                  title,
                  style: const TextStyle(fontWeight: FontWeight.w600),
                ),
              ),
              for (final tag in tags)
                Padding(
                  padding: const EdgeInsets.only(left: 4),
                  child: Chip(
                    label: Text('$tag'),
                    visualDensity: VisualDensity.compact,
                  ),
                ),
            ],
          ),
          for (final step in steps)
            _StepLine(step: step, result: stepResults[step['line']]),
        ],
      ),
    );
  }
}

class _StepLine extends StatelessWidget {
  const _StepLine({required this.step, required this.result});

  final Map<String, dynamic> step;
  final Map<String, dynamic>? result;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final bound = step['pattern'] != null;
    final text = '${step['text'] ?? ''}';
    final spans = <InlineSpan>[
      TextSpan(
        text: '${step['keyword']} ',
        style: TextStyle(fontWeight: FontWeight.bold, color: scheme.primary),
      ),
    ];
    var cursor = 0;
    final parameters = _list(step['parameters'])
      ..sort((a, b) => (a['start'] as int).compareTo(b['start'] as int));
    for (final parameter in parameters) {
      final start = parameter['start'] as int;
      final end = start + (parameter['length'] as int);
      if (start < cursor || end > text.length) continue;
      spans.add(TextSpan(text: text.substring(cursor, start)));
      spans.add(
        TextSpan(
          text: text.substring(start, end),
          style: TextStyle(
            color: scheme.onTertiaryContainer,
            backgroundColor: scheme.tertiaryContainer,
          ),
        ),
      );
      cursor = end;
    }
    spans.add(TextSpan(text: text.substring(cursor)));
    final message = result?['message'] as String?;
    final docString = step['docString'] as String?;
    return Padding(
      padding: const EdgeInsets.only(left: 24, top: 4),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Padding(
                padding: const EdgeInsets.only(top: 5),
                child: _VerdictDot(
                  verdict: result?['verdict'] as int?,
                  size: 8,
                ),
              ),
              const SizedBox(width: 8),
              Expanded(
                child: Tooltip(
                  message: bound
                      ? 'Step: ${step['pattern']}'
                      : 'No step in the brain matches this phrasing.',
                  child: Text.rich(
                    TextSpan(children: spans),
                    key: ValueKey('step-${step['line']}'),
                    style: bound
                        ? null
                        : TextStyle(
                            color: scheme.outline,
                            fontStyle: FontStyle.italic,
                          ),
                  ),
                ),
              ),
            ],
          ),
          if (docString != null)
            Container(
              margin: const EdgeInsets.only(left: 16, top: 4),
              padding: const EdgeInsets.all(8),
              color: scheme.surfaceContainerHighest,
              child: Text(
                docString,
                style: const TextStyle(fontFamily: 'monospace'),
              ),
            ),
          if (message != null)
            Padding(
              padding: const EdgeInsets.only(left: 16, top: 2),
              child: Text(
                message,
                key: ValueKey('step-message-${step['line']}'),
                style: TextStyle(color: scheme.error),
              ),
            ),
        ],
      ),
    );
  }
}

class _VerdictDot extends StatelessWidget {
  const _VerdictDot({required this.verdict, this.size = 10});

  final int? verdict;
  final double size;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final (color, label) = switch (verdict) {
      _passed => (Colors.green, 'Passed'),
      _failed => (scheme.error, 'Failed'),
      _unbound => (scheme.outline, 'Not understood'),
      null => (scheme.outlineVariant, 'Not run yet'),
      _ => (scheme.outlineVariant, 'Skipped'),
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
