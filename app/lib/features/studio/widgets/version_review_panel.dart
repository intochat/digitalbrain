import 'package:flutter/material.dart';

import '../feature_studio_controller.dart';
import '../feature_studio_models.dart';

const Key featureStudioVersionPanelKey = Key('feature-studio-version-panel');
const Key featureStudioReviewAccessButtonKey = Key(
  'feature-studio-review-access-button',
);

class VersionReviewPanel extends StatelessWidget {
  const VersionReviewPanel({super.key, required this.controller});

  final FeatureStudioController controller;

  @override
  Widget build(BuildContext context) {
    final version = controller.version;
    if (version == null) return const SizedBox.shrink();
    final theme = Theme.of(context);
    return Card(
      key: featureStudioVersionPanelKey,
      margin: EdgeInsets.zero,
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('Version', style: theme.textTheme.headlineSmall),
            const SizedBox(height: 14),
            Text('Version digest', style: theme.textTheme.titleSmall),
            const SizedBox(height: 4),
            SelectableText(
              version.digest,
              style: theme.textTheme.bodySmall?.copyWith(
                fontFamily: 'monospace',
              ),
            ),
            const SizedBox(height: 12),
            Text('Current source digest', style: theme.textTheme.titleSmall),
            const SizedBox(height: 4),
            SelectableText(version.sourceReference),
            if (version.requestedCapabilityIds.isNotEmpty) ...[
              const SizedBox(height: 16),
              Text('Access needed', style: theme.textTheme.titleSmall),
              const SizedBox(height: 6),
              for (final capabilityId in version.requestedCapabilityIds)
                _BulletText(capabilityId),
            ],
            if (version.dependencies.isNotEmpty) ...[
              const SizedBox(height: 16),
              Text('Dependencies', style: theme.textTheme.titleSmall),
              const SizedBox(height: 6),
              for (final dependency in version.dependencies)
                _BulletText(dependency),
            ],
            const SizedBox(height: 18),
            Text(
              'Changes from Previous Version',
              style: theme.textTheme.titleMedium,
            ),
            const SizedBox(height: 8),
            FeatureStudioVersionComparison(
              currentVersion: version,
              accessReview: controller.accessReview,
            ),
            const SizedBox(height: 18),
            _ReviewAccessAction(controller: controller),
          ],
        ),
      ),
    );
  }
}

class FeatureStudioVersionComparison extends StatelessWidget {
  const FeatureStudioVersionComparison({
    super.key,
    required this.currentVersion,
    required this.accessReview,
  });

  final FeatureStudioVersion currentVersion;
  final FeatureStudioAccessReview? accessReview;

  @override
  Widget build(BuildContext context) {
    final review = accessReview;
    if (review == null) {
      return const Text('Previous Version comparison is not loaded.');
    }
    final diff = buildFeatureStudioVersionDiff(
      currentVersion: currentVersion,
      previousVersion: review.previousVersion,
    );
    return switch (diff.status) {
      FeatureStudioVersionDiffStatus.noPreviousVersion => const Text(
        'No previous installed Version.',
      ),
      FeatureStudioVersionDiffStatus.sourceUnavailable => const Text(
        'Previous Version source is unavailable, so an exact comparison cannot be shown.',
      ),
      FeatureStudioVersionDiffStatus.compared
          when diff.files.isEmpty && diff.coordinateChanges.isEmpty =>
        const Text('No file changes from the Previous Version.'),
      FeatureStudioVersionDiffStatus.compared => Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          for (final coordinate in diff.coordinateChanges)
            _VersionCoordinateChangeTile(change: coordinate),
          for (final file in diff.files) _VersionFileChangeTile(file: file),
        ],
      ),
    };
  }
}

class _VersionCoordinateChangeTile extends StatelessWidget {
  const _VersionCoordinateChangeTile({required this.change});

  final FeatureStudioVersionCoordinateChange change;

  @override
  Widget build(BuildContext context) {
    final label = switch (change.kind) {
      FeatureStudioVersionCoordinateKind.implementationProjectPath =>
        'Implementation project path',
      FeatureStudioVersionCoordinateKind.scenarioProjectPath =>
        'Scenario project path',
    };
    return ExpansionTile(
      tilePadding: EdgeInsets.zero,
      childrenPadding: const EdgeInsets.only(bottom: 12),
      title: Text('Changed · $label'),
      children: [
        _SourceSnapshot(label: 'Previous', content: change.previousValue),
        const SizedBox(height: 8),
        _SourceSnapshot(label: 'Current', content: change.currentValue),
      ],
    );
  }
}

class _VersionFileChangeTile extends StatelessWidget {
  const _VersionFileChangeTile({required this.file});

  final FeatureStudioVersionFileChange file;

  @override
  Widget build(BuildContext context) {
    final label = switch (file.kind) {
      FeatureStudioVersionFileChangeKind.added => 'Added',
      FeatureStudioVersionFileChangeKind.changed => 'Changed',
      FeatureStudioVersionFileChangeKind.removed => 'Removed',
    };
    return ExpansionTile(
      tilePadding: EdgeInsets.zero,
      childrenPadding: const EdgeInsets.only(bottom: 12),
      title: Text('$label · ${file.path}'),
      children: [
        if (file.previousContent case final content?)
          _SourceSnapshot(label: 'Previous', content: content),
        if (file.currentContent case final content?) ...[
          if (file.previousContent != null) const SizedBox(height: 8),
          _SourceSnapshot(label: 'Current', content: content),
        ],
      ],
    );
  }
}

class _SourceSnapshot extends StatelessWidget {
  const _SourceSnapshot({required this.label, required this.content});

  final String label;
  final String content;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(10),
      color: theme.colorScheme.surfaceContainerHighest,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: theme.textTheme.labelMedium),
          const SizedBox(height: 4),
          SingleChildScrollView(
            scrollDirection: Axis.horizontal,
            child: SelectableText(
              content,
              style: theme.textTheme.bodySmall?.copyWith(
                fontFamily: 'monospace',
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _ReviewAccessAction extends StatelessWidget {
  const _ReviewAccessAction({required this.controller});

  final FeatureStudioController controller;

  @override
  Widget build(BuildContext context) {
    return switch (controller.accessReviewPhase) {
      FeatureStudioAccessReviewPhase.idle => OutlinedButton.icon(
        key: featureStudioReviewAccessButtonKey,
        onPressed: controller.canReviewAccess ? controller.reviewAccess : null,
        icon: const Icon(Icons.security_outlined),
        label: const Text('Review access'),
      ),
      FeatureStudioAccessReviewPhase.reviewing => Semantics(
        liveRegion: true,
        label: 'Preparing access review.',
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            LinearProgressIndicator(),
            SizedBox(height: 8),
            Text('Preparing access review…'),
          ],
        ),
      ),
      FeatureStudioAccessReviewPhase.ready => Semantics(
        liveRegion: true,
        label: 'Access review is ready.',
        child: Text('Access review is ready.'),
      ),
      FeatureStudioAccessReviewPhase.retryableFailure => Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const Text('Access review is temporarily unavailable.'),
          const SizedBox(height: 8),
          OutlinedButton(
            key: featureStudioReviewAccessButtonKey,
            onPressed: controller.retryAccessReviewIsSafe
                ? controller.retryAccessReview
                : null,
            child: const Text('Try again'),
          ),
        ],
      ),
      FeatureStudioAccessReviewPhase.failed => Text(
        'Access review could not be completed.',
        style: TextStyle(color: Theme.of(context).colorScheme.error),
      ),
    };
  }
}

class _BulletText extends StatelessWidget {
  const _BulletText(this.value);

  final String value;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: 4),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Text('• '),
        Expanded(child: Text(value)),
      ],
    ),
  );
}
