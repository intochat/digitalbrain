import 'package:flutter/material.dart';

import 'spec_vocabulary.dart';
import 'spec_highlighter.dart';

class SpecProse extends StatelessWidget {
  const SpecProse(this.text, {super.key, this.vocabulary = const []});
  final String text;
  final List<SpecToken> vocabulary;
  @override
  Widget build(BuildContext context) {
    final spans = highlightSpec(text, vocabulary);
    if (spans.isEmpty) return SelectableText(text);
    final scheme = Theme.of(context).colorScheme;
    final children = <InlineSpan>[];
    var offset = 0;
    for (final span in spans) {
      children.add(TextSpan(text: text.substring(offset, span.start)));
      final color = switch (span.kind) {
        'signal' => scheme.tertiary,
        'neuron' => scheme.secondary,
        _ => scheme.primary,
      };
      final tokenText = text.substring(span.start, span.end);
      if (span.tokens.isEmpty) {
        children.add(
          TextSpan(
            text: tokenText,
            style: TextStyle(
              color: color,
              backgroundColor: scheme.surfaceContainerHighest,
            ),
          ),
        );
      } else {
        children.add(
          WidgetSpan(
            alignment: PlaceholderAlignment.baseline,
            baseline: TextBaseline.alphabetic,
            child: InkWell(
              onTap: () => showModalBottomSheet<void>(
                context: context,
                showDragHandle: true,
                isScrollControlled: true,
                builder: (context) => SafeArea(
                  child: SingleChildScrollView(
                    child: Padding(
                      padding: const EdgeInsets.all(24),
                      child: Column(
                        mainAxisSize: MainAxisSize.min,
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            span.tokens.length > 1
                                ? 'Matching registry entries'
                                : tokenText,
                            style: Theme.of(context).textTheme.titleLarge,
                          ),
                          for (final token in span.tokens)
                            ListTile(
                              contentPadding: EdgeInsets.zero,
                              title: Text(
                                '${token.kind} · ${token.qualifiedName.isEmpty ? token.name : token.qualifiedName}',
                              ),
                              subtitle: Text(
                                [
                                  token.module,
                                  if (token.description != null)
                                    token.description!,
                                ].where((s) => s.isNotEmpty).join('\n'),
                              ),
                            ),
                          const Text(
                            'Registry references describe available contracts. They do not verify behavior.',
                          ),
                        ],
                      ),
                    ),
                  ),
                ),
              ),
              child: Text(
                tokenText,
                style: TextStyle(
                  color: color,
                  decoration: TextDecoration.underline,
                  decorationColor: color,
                ),
              ),
            ),
          ),
        );
      }
      offset = span.end;
    }
    children.add(TextSpan(text: text.substring(offset)));
    return SelectableText.rich(
      TextSpan(
        style: Theme.of(context).textTheme.bodyMedium?.copyWith(height: 1.6),
        children: children,
      ),
    );
  }
}
