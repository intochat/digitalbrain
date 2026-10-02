import 'package:flutter/material.dart';

import 'app_document.dart';
import 'app_verification_summary.dart';
import 'behavior_block.dart';
import 'scenario_list.dart';
import 'spec_document.dart';
import 'spec_vocabulary.dart';
import 'registry_token_sheet.dart';

class AppSpecView extends StatelessWidget {
  const AppSpecView({
    super.key,
    required this.spec,
    this.run,
    this.document,
    this.vocabulary = const [],
    this.files = const {},
    this.sourceRevision,
    this.current = true,
    this.onEdit,
    this.onDuplicate,
    this.onDelete,
  });
  final String spec;
  final Map<String, dynamic>? run;
  final AppDocument? document;
  final List<SpecToken> vocabulary;
  final Map<String, String> files;
  final String? sourceRevision;
  final bool current;
  final ValueChanged<AppBehavior>? onEdit, onDuplicate, onDelete;

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: childrenFor(context),
  );

  List<Widget> childrenFor(BuildContext context) {
    final parsed = document == null ? parseSpec(spec) : null;
    final all = document?.scenarios ?? parsed!.scenarios;
    final result = run == null ? null : VerificationRun.fromJson(run!);
    final linked =
        document?.behaviors.expand((b) => b.scenarioIds).toSet() ?? <String>{};
    final unlinked = all.where((s) => !linked.contains(s.id)).toList();
    final preamble = document?.preamble ?? parsed!.preamble;
    return [
      if (!current && result != null)
        const Padding(
          padding: EdgeInsets.only(bottom: 12),
          child: Text(
            'Changes need checking. Previous results do not cover these changes.',
          ),
        ),
      if (current && result != null && !result.green)
        const Padding(
          padding: EdgeInsets.only(bottom: 12),
          child: Text('The app check did not pass. Review the results below.'),
        ),
      for (final paragraph
          in preamble
              .trim()
              .split(RegExp(r'\r?\n\s*\r?\n'))
              .where((p) => p.isNotEmpty))
        Padding(
          padding: const EdgeInsets.only(bottom: 12),
          child: SpecProse(
            paragraph.startsWith('# ') ? paragraph.substring(2) : paragraph,
            vocabulary: vocabulary,
          ),
        ),
      if (document != null) ...[
        for (final behavior in document!.behaviors)
          BehaviorBlock(
            key: ValueKey('behavior-${behavior.id}'),
            behavior: behavior,
            scenarios: all,
            run: result,
            current: current,
            vocabulary: vocabulary,
            files: files,
            sourceRevision: sourceRevision,
            onEdit: onEdit == null ? null : () => onEdit!(behavior),
            onDuplicate: onDuplicate == null
                ? null
                : () => onDuplicate!(behavior),
            onDelete: onDelete == null ? null : () => onDelete!(behavior),
          ),
        if (unlinked.isNotEmpty)
          Text('App scenarios', style: Theme.of(context).textTheme.titleMedium),
      ],
      for (final scenario in unlinked)
        ScenarioList(
          scenarios: [scenario],
          all: all,
          run: result,
          current: current,
          vocabulary: vocabulary,
        ),
    ];
  }
}
