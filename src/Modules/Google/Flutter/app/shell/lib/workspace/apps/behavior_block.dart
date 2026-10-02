import 'package:flutter/material.dart';

import 'app_document.dart';
import 'app_verification_summary.dart';
import 'spec_vocabulary.dart';
import 'registry_token_sheet.dart';
import 'scenario_list.dart';
import 'app_source_sheet.dart';

class BehaviorBlock extends StatefulWidget {
  const BehaviorBlock({
    super.key,
    required this.behavior,
    required this.scenarios,
    this.run,
    this.current = true,
    this.vocabulary = const [],
    this.files = const {},
    this.sourceRevision,
    this.onEdit,
    this.onDuplicate,
    this.onDelete,
  });
  final AppBehavior behavior;
  final List<AppScenario> scenarios;
  final VerificationRun? run;
  final bool current;
  final List<SpecToken> vocabulary;
  final Map<String, String> files;
  final String? sourceRevision;
  final VoidCallback? onEdit, onDuplicate, onDelete;
  @override
  State<BehaviorBlock> createState() => _BehaviorBlockState();
}

class _BehaviorBlockState extends State<BehaviorBlock> {
  bool expanded = false;
  @override
  Widget build(BuildContext context) {
    final b = widget.behavior;
    final linked = widget.scenarios
        .where((s) => b.scenarioIds.contains(s.id))
        .toList();
    final status = behaviorStatus(
      linked,
      widget.scenarios,
      widget.run,
      widget.current,
    );
    final scheme = Theme.of(context).colorScheme;
    return Container(
      margin: const EdgeInsets.only(bottom: 12),
      padding: const EdgeInsets.all(20),
      decoration: BoxDecoration(
        border: Border.all(color: scheme.outlineVariant),
        borderRadius: BorderRadius.circular(16),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  b.title,
                  style: Theme.of(context).textTheme.titleMedium
                      ?.copyWith(fontWeight: FontWeight.w600),
                ),
              ),
              PopupMenuButton<String>(
                tooltip: 'Options for ${b.title}',
                icon: const Icon(Icons.more_horiz),
                onSelected: (action) {
                  switch (action) {
                    case 'source':
                      showAppSource(
                        context,
                        b.sourcePaths,
                        widget.files,
                        widget.sourceRevision,
                      );
                    case 'scenarios':
                      setState(() => expanded = !expanded);
                    case 'edit':
                      widget.onEdit?.call();
                    case 'duplicate':
                      widget.onDuplicate?.call();
                    case 'delete':
                      widget.onDelete?.call();
                  }
                },
                itemBuilder: (_) => [
                  if (widget.onEdit != null)
                    const PopupMenuItem(
                      value: 'edit',
                      child: Text('Edit description'),
                    ),
                  const PopupMenuItem(
                    value: 'source',
                    child: Text('View source'),
                  ),
                  const PopupMenuItem(
                    value: 'scenarios',
                    child: Text('View scenarios'),
                  ),
                  if (widget.onDuplicate != null)
                    const PopupMenuItem(
                      value: 'duplicate',
                      child: Text('Duplicate'),
                    ),
                  if (widget.onDelete != null)
                    const PopupMenuItem(value: 'delete', child: Text('Delete')),
                ],
              ),
            ],
          ),
          const SizedBox(height: 4),
          SpecProse(b.description, vocabulary: widget.vocabulary),
          const SizedBox(height: 12),
          Wrap(
            spacing: 16,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Icon(
                    status == 'Checks passed'
                        ? Icons.check_circle_outline
                        : status == 'Checks failed'
                        ? Icons.error_outline
                        : Icons.radio_button_unchecked,
                    size: 18,
                    color: status == 'Checks failed'
                        ? scheme.error
                        : scheme.onSurfaceVariant,
                  ),
                  const SizedBox(width: 6),
                  Text(status, style: Theme.of(context).textTheme.bodySmall),
                ],
              ),
              TextButton(
                onPressed: () => setState(() => expanded = !expanded),
                child: Text(
                  '${linked.length} ${linked.length == 1 ? 'scenario' : 'scenarios'}',
                ),
              ),
            ],
          ),
          if (expanded) ...[
            const Divider(),
            if (linked.isEmpty) const Text('No scenarios linked yet.'),
            ScenarioList(
              scenarios: linked,
              all: widget.scenarios,
              run: widget.run,
              current: widget.current,
              vocabulary: widget.vocabulary,
            ),
          ],
        ],
      ),
    );
  }
}
