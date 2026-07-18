import 'package:flutter/material.dart';

import '../feature_studio_controller.dart';
import '../feature_studio_models.dart';

const Key featureStudioDraftIdKey = Key('feature-studio-draft-id');
const Key featureStudioBackToChatButtonKey = Key(
  'feature-studio-back-to-chat-button',
);

class OriginRequestBar extends StatelessWidget {
  const OriginRequestBar({
    super.key,
    required this.draft,
    required this.savePhase,
    required this.onBackToChat,
  });

  final FeatureStudioDraft draft;
  final FeatureStudioSavePhase savePhase;
  final VoidCallback onBackToChat;

  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, constraints) {
      final saveLabel = _saveLabel(savePhase);
      return Material(
        color: Theme.of(context).colorScheme.surface,
        child: constraints.maxWidth < 600
            ? _compact(context, saveLabel)
            : _wide(context, saveLabel),
      );
    },
  );

  Widget _compact(BuildContext context, String saveLabel) => Padding(
    padding: const EdgeInsets.fromLTRB(8, 8, 12, 12),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      mainAxisSize: MainAxisSize.min,
      children: [
        Row(
          children: [
            IconButton(
              key: featureStudioBackToChatButtonKey,
              tooltip: 'Back to Chat',
              onPressed: onBackToChat,
              icon: const Icon(Icons.arrow_back),
            ),
            const SizedBox(width: 4),
            _DraftBadge(key: featureStudioDraftIdKey, theme: Theme.of(context)),
            const Spacer(),
            Semantics(
              liveRegion: true,
              label: 'Save status: $saveLabel',
              excludeSemantics: true,
              child: Tooltip(
                message: saveLabel,
                child: Padding(
                  padding: const EdgeInsets.all(12),
                  child: Icon(
                    savePhase == FeatureStudioSavePhase.saved
                        ? Icons.cloud_done_outlined
                        : Icons.cloud_upload_outlined,
                  ),
                ),
              ),
            ),
          ],
        ),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 8),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                draft.originatingRequest.text,
                style: Theme.of(context).textTheme.titleMedium,
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
              ),
              const SizedBox(height: 4),
              Text(
                draft.goal,
                style: Theme.of(context).textTheme.bodySmall?.copyWith(
                  color: Theme.of(context).colorScheme.onSurfaceVariant,
                ),
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
              ),
              const SizedBox(height: 4),
              Text(saveLabel, style: Theme.of(context).textTheme.labelMedium),
            ],
          ),
        ),
      ],
    ),
  );

  Widget _wide(BuildContext context, String saveLabel) => Padding(
    padding: const EdgeInsets.fromLTRB(16, 12, 20, 12),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        IconButton(
          key: featureStudioBackToChatButtonKey,
          tooltip: 'Back to Chat',
          onPressed: onBackToChat,
          icon: const Icon(Icons.arrow_back),
        ),
        const SizedBox(width: 8),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              Wrap(
                spacing: 8,
                runSpacing: 6,
                crossAxisAlignment: WrapCrossAlignment.center,
                children: [
                  _DraftBadge(
                    key: featureStudioDraftIdKey,
                    theme: Theme.of(context),
                  ),
                ],
              ),
              const SizedBox(height: 6),
              Text(
                draft.originatingRequest.text,
                style: Theme.of(context).textTheme.titleMedium,
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
              ),
              const SizedBox(height: 2),
              Text(
                draft.goal,
                style: Theme.of(context).textTheme.bodySmall?.copyWith(
                  color: Theme.of(context).colorScheme.onSurfaceVariant,
                ),
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
              ),
            ],
          ),
        ),
        const SizedBox(width: 12),
        Semantics(
          liveRegion: true,
          label: 'Save status: $saveLabel',
          excludeSemantics: true,
          child: Chip(
            avatar: Icon(
              savePhase == FeatureStudioSavePhase.saved
                  ? Icons.cloud_done_outlined
                  : Icons.cloud_upload_outlined,
              size: 18,
            ),
            label: Text(saveLabel),
          ),
        ),
      ],
    ),
  );
}

class _DraftBadge extends StatelessWidget {
  const _DraftBadge({super.key, required this.theme});

  final ThemeData theme;

  @override
  Widget build(BuildContext context) => DecoratedBox(
    decoration: BoxDecoration(
      color: theme.colorScheme.primaryContainer,
      borderRadius: BorderRadius.circular(999),
    ),
    child: Padding(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
      child: Text(
        'Draft',
        style: theme.textTheme.labelLarge?.copyWith(
          color: theme.colorScheme.onPrimaryContainer,
        ),
      ),
    ),
  );
}

String _saveLabel(FeatureStudioSavePhase phase) => switch (phase) {
  FeatureStudioSavePhase.saved => 'Saved',
  FeatureStudioSavePhase.debouncing => 'Unsaved changes',
  FeatureStudioSavePhase.saving => 'Saving…',
  FeatureStudioSavePhase.invalid => 'Needs attention',
  FeatureStudioSavePhase.retryableFailure => 'Save paused',
  FeatureStudioSavePhase.conflict => 'Changes need review',
  FeatureStudioSavePhase.failed => 'Could not save',
};
