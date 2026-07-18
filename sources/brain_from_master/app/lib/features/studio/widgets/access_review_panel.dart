import 'package:flutter/material.dart';

import '../feature_studio_controller.dart';
import '../feature_studio_models.dart';
import 'version_review_panel.dart';

const Key featureStudioAccessReviewPanelKey = Key(
  'feature-studio-access-review-panel',
);
const Key featureStudioApproveInstallButtonKey = Key(
  'feature-studio-approve-install-button',
);
const Key featureStudioRetryInstallButtonKey = Key(
  'feature-studio-retry-install-button',
);
const Key featureStudioResetAuthorityReviewButtonKey = Key(
  'feature-studio-reset-authority-review-button',
);

class AccessReviewPanel extends StatelessWidget {
  const AccessReviewPanel({super.key, required this.controller});

  final FeatureStudioController controller;

  @override
  Widget build(BuildContext context) {
    final review = controller.accessReview;
    final theme = Theme.of(context);
    return Card(
      key: featureStudioAccessReviewPanelKey,
      margin: EdgeInsets.zero,
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('Review access', style: theme.textTheme.headlineSmall),
            const SizedBox(height: 4),
            Text(
              'Review the exact access and automations for this Version.',
              style: theme.textTheme.bodyMedium?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
            const SizedBox(height: 16),
            if (review == null)
              _MissingReview(controller: controller)
            else ...[
              _InstallationTargetReview(review: review),
              const SizedBox(height: 18),
              Text(
                'Candidate release digest',
                style: theme.textTheme.titleSmall,
              ),
              const SizedBox(height: 4),
              SelectableText(
                review.version.digest,
                style: theme.textTheme.bodySmall?.copyWith(
                  fontFamily: 'monospace',
                ),
              ),
              const SizedBox(height: 12),
              Text('Current source digest', style: theme.textTheme.titleSmall),
              const SizedBox(height: 4),
              SelectableText(
                review.version.sourceReference,
                style: theme.textTheme.bodySmall?.copyWith(
                  fontFamily: 'monospace',
                ),
              ),
              const SizedBox(height: 18),
              Text(
                'Changes from Previous Version',
                style: theme.textTheme.titleMedium,
              ),
              const SizedBox(height: 8),
              FeatureStudioVersionComparison(
                currentVersion: review.version,
                accessReview: review,
              ),
              const SizedBox(height: 18),
              Text('Access needed', style: theme.textTheme.titleMedium),
              const SizedBox(height: 8),
              if (review.grants.isEmpty)
                const Text('No access needed.')
              else
                for (final grant in review.grants) _GrantCard(grant: grant),
              const SizedBox(height: 12),
              Text('Automations', style: theme.textTheme.titleMedium),
              const SizedBox(height: 8),
              if (review.subscriptions.isEmpty)
                const Text('No automations requested.')
              else
                for (final subscription in review.subscriptions)
                  _TriggerBinding(value: subscription),
              const SizedBox(height: 18),
              _InstallAction(controller: controller),
            ],
          ],
        ),
      ),
    );
  }
}

class _InstallationTargetReview extends StatelessWidget {
  const _InstallationTargetReview({required this.review});

  final FeatureStudioAccessReview review;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final previous = review.previousVersion;
    final update = previous != null;
    return Semantics(
      container: true,
      label: update
          ? 'Update existing installation ${review.installationId}'
          : 'Installation target ${review.installationId}',
      child: Container(
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(
          color: theme.colorScheme.surfaceContainerHighest,
          borderRadius: BorderRadius.circular(12),
          border: Border.all(color: theme.colorScheme.outlineVariant),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              update ? 'Update existing installation' : 'Installation target',
              style: theme.textTheme.titleMedium,
            ),
            const SizedBox(height: 4),
            Text(
              update
                  ? 'Approving will replace the installed Version shown below.'
                  : 'Confirm this exact target and candidate Version before approving.',
              style: theme.textTheme.bodyMedium,
            ),
            const SizedBox(height: 12),
            Text('Target installation ID', style: theme.textTheme.titleSmall),
            const SizedBox(height: 4),
            SelectableText(
              review.installationId,
              style: theme.textTheme.bodySmall?.copyWith(
                fontFamily: 'monospace',
              ),
            ),
            if (previous != null) ...[
              const SizedBox(height: 12),
              Text(
                'Installed release digest',
                style: theme.textTheme.titleSmall,
              ),
              const SizedBox(height: 4),
              SelectableText(
                previous.digest,
                style: theme.textTheme.bodySmall?.copyWith(
                  fontFamily: 'monospace',
                ),
              ),
              const SizedBox(height: 12),
              Text(
                'Installed source digest',
                style: theme.textTheme.titleSmall,
              ),
              const SizedBox(height: 4),
              SelectableText(
                previous.sourceReference,
                style: theme.textTheme.bodySmall?.copyWith(
                  fontFamily: 'monospace',
                ),
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class _MissingReview extends StatelessWidget {
  const _MissingReview({required this.controller});

  final FeatureStudioController controller;

  @override
  Widget build(BuildContext context) {
    if (controller.accessReviewPhase ==
        FeatureStudioAccessReviewPhase.retryableFailure) {
      return Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const Text('Access review is temporarily unavailable.'),
          const SizedBox(height: 8),
          OutlinedButton(
            onPressed: controller.retryAccessReviewIsSafe
                ? controller.retryAccessReview
                : null,
            child: const Text('Try again'),
          ),
        ],
      );
    }
    if (controller.accessReviewPhase == FeatureStudioAccessReviewPhase.failed) {
      return Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            'Access review could not be completed.',
            style: TextStyle(color: Theme.of(context).colorScheme.error),
          ),
          const SizedBox(height: 10),
          _ResetAuthorityReviewAction(controller: controller),
        ],
      );
    }
    return const Text('Access review is not available.');
  }
}

class _GrantCard extends StatelessWidget {
  const _GrantCard({required this.grant});

  final FeatureStudioGrant grant;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final provider = grant.provider;
    final connectionId = grant.connectionId;
    final binding = provider == null && connectionId == null
        ? 'DigitalBrain · No Connection required'
        : '${provider ?? 'Provider'} · ${connectionId ?? 'No Connection selected'}';
    return Container(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        border: Border.all(color: theme.colorScheme.outlineVariant),
        borderRadius: BorderRadius.circular(12),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            '${grant.capabilityId} · v${grant.capabilityVersion}',
            style: theme.textTheme.titleSmall,
          ),
          const SizedBox(height: 5),
          Text(binding),
          const SizedBox(height: 8),
          Text(grant.constraintSummary),
          const SizedBox(height: 6),
          ExpansionTile(
            tilePadding: EdgeInsets.zero,
            childrenPadding: const EdgeInsets.only(bottom: 6),
            dense: true,
            title: const Text('Exact constraints'),
            children: [
              Align(
                alignment: Alignment.centerLeft,
                child: SelectableText(
                  grant.constraintsJson,
                  style: theme.textTheme.bodySmall?.copyWith(
                    fontFamily: 'monospace',
                  ),
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _TriggerBinding extends StatelessWidget {
  const _TriggerBinding({required this.value});

  final String value;

  @override
  Widget build(BuildContext context) => Row(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      const Icon(Icons.bolt_outlined, size: 18),
      const SizedBox(width: 7),
      Expanded(child: Text(value == 'manual' ? 'Manual' : value)),
    ],
  );
}

class _InstallAction extends StatelessWidget {
  const _InstallAction({required this.controller});

  final FeatureStudioController controller;

  @override
  Widget build(BuildContext context) {
    return switch (controller.installPhase) {
      FeatureStudioInstallPhase.idle => FilledButton.icon(
        key: featureStudioApproveInstallButtonKey,
        onPressed: controller.canApproveAndInstall
            ? controller.approveAndInstall
            : null,
        icon: const Icon(Icons.verified_user_outlined),
        label: const Text('Approve & install'),
      ),
      FeatureStudioInstallPhase.installing => Semantics(
        liveRegion: true,
        label: 'Installing the approved Version.',
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            LinearProgressIndicator(),
            SizedBox(height: 8),
            Text('Installing the approved Version…'),
          ],
        ),
      ),
      FeatureStudioInstallPhase.retryableFailure => Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Semantics(
            liveRegion: true,
            label: 'Installation can be retried safely.',
            child: const Text(
              'The approval is unchanged. Retrying is safe and will not duplicate access or install a second Version.',
            ),
          ),
          const SizedBox(height: 10),
          OutlinedButton.icon(
            key: featureStudioRetryInstallButtonKey,
            onPressed: controller.retryInstallIsSafe
                ? controller.retryInstall
                : null,
            icon: const Icon(Icons.refresh),
            label: const Text('Try install again'),
          ),
        ],
      ),
      FeatureStudioInstallPhase.failed => Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            'Installation could not be completed. Review access before trying a new installation.',
            style: TextStyle(color: Theme.of(context).colorScheme.error),
          ),
          const SizedBox(height: 10),
          _ResetAuthorityReviewAction(controller: controller),
        ],
      ),
      FeatureStudioInstallPhase.succeeded => const SizedBox.shrink(),
    };
  }
}

class _ResetAuthorityReviewAction extends StatelessWidget {
  const _ResetAuthorityReviewAction({required this.controller});

  final FeatureStudioController controller;

  @override
  Widget build(BuildContext context) => OutlinedButton.icon(
    key: featureStudioResetAuthorityReviewButtonKey,
    onPressed: controller.resetAuthorityReview,
    icon: const Icon(Icons.restart_alt),
    label: const Text('Review access again'),
  );
}
