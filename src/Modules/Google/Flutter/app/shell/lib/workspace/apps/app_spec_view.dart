import 'package:flutter/material.dart';

import 'app_document.dart';
import 'app_verification_summary.dart';
import 'behavior_block.dart';
import 'scenario_list.dart';
import 'spec_document.dart';
import 'spec_vocabulary.dart';
import 'registry_token_sheet.dart';
import 'app_source_sheet.dart';

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
    this.onEditScenario,
    this.onDuplicateScenario,
    this.onDeleteScenario,
    this.onMoveScenario,
  });
  final String spec;
  final Map<String, dynamic>? run;
  final AppDocument? document;
  final List<SpecToken> vocabulary;
  final Map<String, String> files;
  final String? sourceRevision;
  final bool current;
  final ValueChanged<AppBehavior>? onEdit, onDuplicate, onDelete;
  final ValueChanged<AppScenario>? onEditScenario,
      onDuplicateScenario,
      onDeleteScenario;
  final void Function(AppScenario, int)? onMoveScenario;

  List<String> sourcePaths(AppScenario scenario) => document == null
      ? files.keys.toList()
      : document!.behaviors
            .where((b) => b.scenarioIds.contains(scenario.id))
            .expand((b) => b.sourcePaths)
            .toSet()
            .toList();

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: childrenFor(context),
  );

  List<Widget> childrenFor(BuildContext context) {
    final parsed = document == null ? parseSpec(spec) : null;
    final all = document?.scenarios ?? parsed!.scenarios;
    final result = run == null ? null : VerificationRun.fromJson(run!);

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
      for (final (index, scenario) in all.indexed)
        Card(
          key: ValueKey('scenario-block-${scenario.id}'),
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                ScenarioList(
                  scenarios: [scenario],
                  all: all,
                  run: result,
                  current: current,
                  vocabulary: vocabulary,
                ),
                Wrap(
                  spacing: 8,
                  children: [
                    if (sourcePaths(scenario).isNotEmpty)
                      TextButton.icon(
                        key: ValueKey('scenario-source-${scenario.id}'),
                        onPressed: () => showAppSource(
                          context,
                          sourcePaths(scenario),
                          files,
                          sourceRevision,
                        ),
                        icon: const Icon(Icons.code),
                        label: Text(
                          document == null
                              ? 'Package source'
                              : 'View linked source',
                        ),
                      ),
                    if (onEditScenario != null)
                      TextButton(
                        onPressed: () => onEditScenario!(scenario),
                        child: const Text('Edit'),
                      ),
                    if (onDuplicateScenario != null)
                      TextButton(
                        onPressed: () => onDuplicateScenario!(scenario),
                        child: const Text('Duplicate'),
                      ),
                    if (onDeleteScenario != null)
                      TextButton(
                        onPressed: () => onDeleteScenario!(scenario),
                        child: const Text('Remove'),
                      ),
                    if (onMoveScenario != null) ...[
                      IconButton(
                        tooltip: 'Move scenario up',
                        onPressed: index == 0
                            ? null
                            : () => onMoveScenario!(scenario, -1),
                        icon: const Icon(Icons.arrow_upward),
                      ),
                      IconButton(
                        tooltip: 'Move scenario down',
                        onPressed: index == all.length - 1
                            ? null
                            : () => onMoveScenario!(scenario, 1),
                        icon: const Icon(Icons.arrow_downward),
                      ),
                    ],
                  ],
                ),
              ],
            ),
          ),
        ),
      if (document != null && document!.behaviors.isNotEmpty)
        ExpansionTile(
          title: const Text('Behavior implementations'),
          initiallyExpanded: all.isEmpty,
          children: [
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
          ],
        ),
    ];
  }
}
