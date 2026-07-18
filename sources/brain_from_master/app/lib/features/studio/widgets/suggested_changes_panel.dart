import 'package:flutter/material.dart';

import '../feature_studio_controller.dart';
import '../feature_studio_models.dart';

const Key featureStudioSuggestionsPanelKey = Key(
  'feature-studio-suggestions-panel',
);
const Key featureStudioSuggestionGuidanceKey = Key(
  'feature-studio-suggestion-guidance',
);

class SuggestedChangesPanel extends StatefulWidget {
  const SuggestedChangesPanel({super.key, required this.controller});

  final FeatureStudioController controller;

  @override
  State<SuggestedChangesPanel> createState() => _SuggestedChangesPanelState();
}

class _SuggestedChangesPanelState extends State<SuggestedChangesPanel> {
  final TextEditingController _guidance = TextEditingController();

  @override
  void dispose() {
    _guidance.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final controller = widget.controller;
    final theme = Theme.of(context);
    return Card(
      key: featureStudioSuggestionsPanelKey,
      margin: EdgeInsets.zero,
      child: SingleChildScrollView(
        padding: const EdgeInsets.all(20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('Suggested changes', style: theme.textTheme.headlineSmall),
            const SizedBox(height: 4),
            Text(
              'Ask for a complete alternative, then review every addition and removal before deciding.',
              style: theme.textTheme.bodyMedium?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
            const SizedBox(height: 16),
            TextField(
              key: featureStudioSuggestionGuidanceKey,
              controller: _guidance,
              enabled:
                  controller.isMutableDraft &&
                  !controller.conflictRecoveryInFlight &&
                  controller.suggestionPhase !=
                      FeatureStudioSuggestionPhase.requesting,
              minLines: 2,
              maxLines: 5,
              decoration: const InputDecoration(
                labelText: 'What should improve?',
                hintText: 'Make the expected outcome more specific',
              ),
              onChanged: (_) => setState(() {}),
            ),
            const SizedBox(height: 12),
            OutlinedButton.icon(
              onPressed:
                  controller.canRequestSuggestion &&
                      _guidance.text.trim().isNotEmpty
                  ? () =>
                        controller.requestSuggestedChange(_guidance.text.trim())
                  : null,
              icon: const Icon(Icons.auto_awesome_outlined),
              label: const Text('Suggest changes'),
            ),
            if (controller.suggestionPhase ==
                FeatureStudioSuggestionPhase.requesting) ...[
              const SizedBox(height: 18),
              const _LiveStatus(
                label: 'Preparing Suggested changes.',
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    LinearProgressIndicator(),
                    SizedBox(height: 8),
                    Text('Preparing a reviewable alternative…'),
                  ],
                ),
              ),
            ],
            if (controller.suggestionPhase ==
                FeatureStudioSuggestionPhase.retryableFailure) ...[
              const SizedBox(height: 16),
              const _LiveStatus(
                label: 'Suggested changes are temporarily unavailable.',
                child: Text('Suggested changes are temporarily unavailable.'),
              ),
              const SizedBox(height: 8),
              OutlinedButton(
                onPressed: controller.retrySuggestedChange,
                child: const Text('Try again'),
              ),
            ],
            if (controller.suggestionPhase ==
                FeatureStudioSuggestionPhase.failed) ...[
              const SizedBox(height: 16),
              _LiveStatus(
                label: 'Suggested changes could not be prepared.',
                child: Text(
                  'Suggested changes could not be prepared.',
                  style: TextStyle(color: theme.colorScheme.error),
                ),
              ),
            ],
            if (controller.suggestionPhase ==
                FeatureStudioSuggestionPhase.stale) ...[
              const SizedBox(height: 16),
              const _LiveStatus(
                label: 'This suggestion is out of date.',
                child: Text('This suggestion is out of date.'),
              ),
              const SizedBox(height: 8),
              TextButton(
                onPressed: controller.dismissStaleSuggestedChange,
                child: const Text('Dismiss'),
              ),
            ],
            if (controller.suggestionPhase ==
                    FeatureStudioSuggestionPhase.ready ||
                controller.suggestionPhase ==
                    FeatureStudioSuggestionPhase.deciding) ...[
              const SizedBox(height: 20),
              const _LiveStatus(
                label: 'Suggested changes are ready for review.',
                child: SizedBox.shrink(),
              ),
              Text(
                controller.suggestion?.summary ?? 'Suggested alternative',
                style: theme.textTheme.titleMedium,
              ),
              const SizedBox(height: 12),
              for (final entry
                  in controller.suggestionDiff?.entries ??
                      const <FeatureStudioDiffEntry>[]) ...[
                _DiffEntry(entry: entry),
                const SizedBox(height: 8),
              ],
              const SizedBox(height: 8),
              Row(
                children: [
                  Expanded(
                    child: OutlinedButton(
                      onPressed: controller.canRejectSuggestion
                          ? controller.rejectSuggestedChange
                          : null,
                      child: const Text('Reject'),
                    ),
                  ),
                  const SizedBox(width: 8),
                  Expanded(
                    child: OutlinedButton.icon(
                      onPressed: controller.canAcceptSuggestion
                          ? controller.acceptSuggestedChange
                          : null,
                      icon: const Icon(Icons.check),
                      label: const Text('Accept'),
                    ),
                  ),
                ],
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class _DiffEntry extends StatelessWidget {
  const _DiffEntry({required this.entry});

  final FeatureStudioDiffEntry entry;

  @override
  Widget build(BuildContext context) {
    final addition = entry.kind == FeatureStudioDiffKind.addition;
    final color = addition
        ? Theme.of(context).colorScheme.tertiary
        : Theme.of(context).colorScheme.error;
    return Semantics(
      label:
          '${addition ? 'Addition' : 'Removal'} in ${entry.area.name}: ${entry.displayLabel}',
      child: Material(
        color: color.withValues(alpha: 0.1),
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(10),
          side: BorderSide(color: color.withValues(alpha: 0.35)),
        ),
        clipBehavior: Clip.antiAlias,
        child: ExpansionTile(
          key: ValueKey(
            'suggestion-diff-${entry.kind.name}-${entry.area.name}-${entry.identity}',
          ),
          leading: Icon(
            addition ? Icons.add : Icons.remove,
            color: color,
            size: 18,
          ),
          title: Text(
            entry.displayLabel,
            style: const TextStyle(fontWeight: FontWeight.w600),
          ),
          subtitle: const Text('Show complete change'),
          childrenPadding: const EdgeInsets.fromLTRB(16, 0, 16, 14),
          children: [
            Align(
              alignment: Alignment.centerLeft,
              child: SelectableText(
                entry.value,
                style: entry.area == FeatureStudioDiffArea.source
                    ? const TextStyle(fontFamily: 'monospace')
                    : null,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _LiveStatus extends StatelessWidget {
  const _LiveStatus({required this.label, required this.child});

  final String label;
  final Widget child;

  @override
  Widget build(BuildContext context) =>
      Semantics(liveRegion: true, container: true, label: label, child: child);
}
