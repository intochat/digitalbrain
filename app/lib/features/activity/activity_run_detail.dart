import 'package:flutter/material.dart';

import 'activity_formatting.dart';
import 'activity_models.dart';

const activityTechnicalDetailsKey = ValueKey('activity-technical-details');
const activityBackToActivityButtonKey = ValueKey('activity-back-to-activity');
const activityOpenFeatureButtonKey = ValueKey('activity-open-feature');
const activityOpenChatButtonKey = ValueKey('activity-open-chat');
const activityOpenRequestButtonKey = ValueKey('activity-open-request');
const activityOpenAutomationButtonKey = ValueKey('activity-open-automation');
const activityOpenResultButtonKey = ValueKey('activity-open-result');
const activityApprovalCardKey = ValueKey('activity-approval-card');
const activityReviewChangeButtonKey = ValueKey('activity-review-change');

typedef ActivityAutomationReferenceCallback =
    void Function(String featureId, String automationId);

class ActivityRunDetailPage extends StatelessWidget {
  const ActivityRunDetailPage({
    super.key,
    required this.run,
    this.onOpenFeature,
    this.onOpenConversation,
    this.onOpenRequest,
    this.onOpenAutomation,
    this.onOpenResultSurface,
  });

  final ActivityRun run;
  final ValueChanged<String>? onOpenFeature;
  final ValueChanged<String>? onOpenConversation;
  final ValueChanged<String>? onOpenRequest;
  final ActivityAutomationReferenceCallback? onOpenAutomation;
  final ValueChanged<String>? onOpenResultSurface;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Run details')),
      body: SafeArea(
        child: ActivityRunDetailView(
          run: run,
          onOpenFeature: onOpenFeature,
          onOpenConversation: onOpenConversation,
          onOpenRequest: onOpenRequest,
          onOpenAutomation: onOpenAutomation,
          onOpenResultSurface: onOpenResultSurface,
        ),
      ),
    );
  }
}

class ActivityRunDetailView extends StatelessWidget {
  const ActivityRunDetailView({
    super.key,
    required this.run,
    this.onBackToActivity,
    this.onOpenFeature,
    this.onOpenConversation,
    this.onOpenRequest,
    this.onOpenAutomation,
    this.onOpenResultSurface,
  });

  final ActivityRun run;
  final VoidCallback? onBackToActivity;
  final ValueChanged<String>? onOpenFeature;
  final ValueChanged<String>? onOpenConversation;
  final ValueChanged<String>? onOpenRequest;
  final ActivityAutomationReferenceCallback? onOpenAutomation;
  final ValueChanged<String>? onOpenResultSurface;

  @override
  Widget build(BuildContext context) {
    return SelectionArea(
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 20, 20, 32),
        children: [
          if (onBackToActivity case final backToActivity?) ...[
            Align(
              alignment: Alignment.centerLeft,
              child: TextButton.icon(
                key: activityBackToActivityButtonKey,
                onPressed: backToActivity,
                icon: const Icon(Icons.arrow_back),
                label: const Text('Back to Activity'),
              ),
            ),
            const SizedBox(height: 12),
          ],
          _RunHeading(run: run),
          if (_isWaitingForApproval(run)) ...[
            const SizedBox(height: 16),
            _ApprovalWaitingCard(
              run: run,
              onOpenConversation: onOpenConversation,
              onOpenRequest: onOpenRequest,
            ),
          ],
          const SizedBox(height: 20),
          _OverviewCard(run: run),
          if (run.safeFailure != null || run.failureGuidance != null) ...[
            const SizedBox(height: 16),
            _FailureCard(run: run),
          ],
          const SizedBox(height: 16),
          _NavigationCard(
            run: run,
            onOpenFeature: onOpenFeature,
            onOpenConversation: onOpenConversation,
            onOpenRequest: onOpenRequest,
            onOpenAutomation: onOpenAutomation,
            onOpenResultSurface: onOpenResultSurface,
          ),
          const SizedBox(height: 16),
          _TechnicalCard(run: run),
        ],
      ),
    );
  }
}

bool _isWaitingForApproval(ActivityRun run) =>
    run.status == ActivityStatus.waitingForApproval ||
    run.authority == ActivityAuthority.waitingForApproval;

class _ApprovalWaitingCard extends StatelessWidget {
  const _ApprovalWaitingCard({
    required this.run,
    required this.onOpenConversation,
    required this.onOpenRequest,
  });

  final ActivityRun run;
  final ValueChanged<String>? onOpenConversation;
  final ValueChanged<String>? onOpenRequest;

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    final reviewAction = _reviewChangeAction();
    final hasOriginRefs =
        run.conversationId != null || run.requestId != null;
    final guidance = hasOriginRefs
        ? 'A change is waiting for your approval in the originating conversation. '
              'Open it to review the Effect before this Activity continues.'
        : 'A change is waiting for your approval. Complete it in the originating '
              'Ask or Chat session. This Activity surface does not approve Effects directly.';

    return Semantics(
      container: true,
      liveRegion: true,
      label: 'Waiting for approval',
      child: Card(
        key: activityApprovalCardKey,
        color: colors.tertiaryContainer,
        child: Padding(
          padding: const EdgeInsets.all(18),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Icon(
                    Icons.approval_outlined,
                    color: colors.onTertiaryContainer,
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: Text(
                      'Waiting for approval',
                      style: Theme.of(context).textTheme.titleMedium?.copyWith(
                        color: colors.onTertiaryContainer,
                      ),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 12),
              Text(
                guidance,
                style: TextStyle(color: colors.onTertiaryContainer),
              ),
              if (reviewAction != null) ...[
                const SizedBox(height: 16),
                FilledButton.icon(
                  key: activityReviewChangeButtonKey,
                  onPressed: reviewAction,
                  icon: const Icon(Icons.open_in_new),
                  label: const Text('Review change'),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }

  VoidCallback? _reviewChangeAction() {
    final conversationId = run.conversationId;
    final openConversation = onOpenConversation;
    if (conversationId != null && openConversation != null) {
      return () => openConversation(conversationId);
    }
    final requestId = run.requestId;
    final openRequest = onOpenRequest;
    if (requestId != null && openRequest != null) {
      return () => openRequest(requestId);
    }
    return null;
  }
}

class _RunHeading extends StatelessWidget {
  const _RunHeading({required this.run});

  final ActivityRun run;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(run.featureName, style: Theme.of(context).textTheme.headlineSmall),
        const SizedBox(height: 10),
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            _LabelChip(
              icon: _statusIcon(run.status),
              label: run.status.label,
              color: _statusColor(context, run.status),
            ),
            _LabelChip(
              icon: _originIcon(run.origin),
              label: run.origin.label,
              color: Theme.of(context).colorScheme.secondary,
            ),
            _LabelChip(
              icon: _authorityIcon(run.authority),
              label: run.authority.label,
              color: _authorityColor(context, run.authority),
            ),
          ],
        ),
      ],
    );
  }
}

class _OverviewCard extends StatelessWidget {
  const _OverviewCard({required this.run});

  final ActivityRun run;

  @override
  Widget build(BuildContext context) {
    return _SectionCard(
      title: 'Run overview',
      child: LayoutBuilder(
        builder: (context, constraints) {
          final fieldWidth = constraints.maxWidth >= 500
              ? (constraints.maxWidth - 16) / 2
              : constraints.maxWidth;
          return Wrap(
            spacing: 16,
            runSpacing: 18,
            children: [
              SizedBox(
                width: fieldWidth,
                child: _DetailField(
                  label: 'Occurred',
                  value: formatActivityTimestamp(run.occurredAt),
                ),
              ),
              SizedBox(
                width: fieldWidth,
                child: _DetailField(
                  label: 'Duration',
                  value: formatActivityDuration(run),
                ),
              ),
              SizedBox(
                width: fieldWidth,
                child: _DetailField(
                  label: 'Attempts',
                  value: run.attempts.toString(),
                ),
              ),
              if (run.retryAt case final retryAt?)
                SizedBox(
                  width: fieldWidth,
                  child: _DetailField(
                    label: 'Retry after',
                    value: formatActivityTimestamp(retryAt),
                  ),
                ),
            ],
          );
        },
      ),
    );
  }
}

class _FailureCard extends StatelessWidget {
  const _FailureCard({required this.run});

  final ActivityRun run;

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    return Semantics(
      container: true,
      liveRegion: true,
      label: 'Run guidance',
      child: Card(
        color: colors.errorContainer,
        child: Padding(
          padding: const EdgeInsets.all(18),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Icon(Icons.info_outline, color: colors.onErrorContainer),
                  const SizedBox(width: 10),
                  Expanded(
                    child: Text(
                      'What happened',
                      style: Theme.of(context).textTheme.titleMedium?.copyWith(
                        color: colors.onErrorContainer,
                      ),
                    ),
                  ),
                ],
              ),
              if (run.safeFailure case final failure?) ...[
                const SizedBox(height: 12),
                Text(failure, style: TextStyle(color: colors.onErrorContainer)),
              ],
              if (run.failureGuidance case final guidance?) ...[
                const SizedBox(height: 10),
                Text(
                  guidance,
                  style: TextStyle(color: colors.onErrorContainer),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class _NavigationCard extends StatelessWidget {
  const _NavigationCard({
    required this.run,
    required this.onOpenFeature,
    required this.onOpenConversation,
    required this.onOpenRequest,
    required this.onOpenAutomation,
    required this.onOpenResultSurface,
  });

  final ActivityRun run;
  final ValueChanged<String>? onOpenFeature;
  final ValueChanged<String>? onOpenConversation;
  final ValueChanged<String>? onOpenRequest;
  final ActivityAutomationReferenceCallback? onOpenAutomation;
  final ValueChanged<String>? onOpenResultSurface;

  @override
  Widget build(BuildContext context) {
    final actions = <Widget>[
      if (onOpenFeature case final openFeature?)
        _ReferenceButton(
          key: activityOpenFeatureButtonKey,
          icon: Icons.extension_outlined,
          label: 'Open Feature',
          onPressed: () => openFeature(run.featureId),
        ),
      if (run.conversationId case final conversationId?)
        if (onOpenConversation case final openConversation?)
          _ReferenceButton(
            key: activityOpenChatButtonKey,
            icon: Icons.chat_bubble_outline,
            label: 'Open Chat',
            onPressed: () => openConversation(conversationId),
          ),
      if (run.requestId case final requestId?)
        if (onOpenRequest case final openRequest?)
          _ReferenceButton(
            key: activityOpenRequestButtonKey,
            icon: Icons.near_me_outlined,
            label: 'Open request',
            onPressed: () => openRequest(requestId),
          ),
      if (run.automationId case final automationId?)
        if (onOpenAutomation case final openAutomation?)
          _ReferenceButton(
            key: activityOpenAutomationButtonKey,
            icon: Icons.schedule_outlined,
            label: 'Open automation',
            onPressed: () => openAutomation(run.featureId, automationId),
          ),
      if (run.resultSurfaceReference case final resultReference?)
        if (onOpenResultSurface case final openResult?)
          _ReferenceButton(
            key: activityOpenResultButtonKey,
            icon: Icons.open_in_new,
            label: 'Open result',
            onPressed: () => openResult(resultReference),
          ),
    ];
    return _SectionCard(
      title: 'Related work',
      child: actions.isEmpty
          ? const Text('No linked destinations are available.')
          : Wrap(spacing: 8, runSpacing: 8, children: actions),
    );
  }
}

class _TechnicalCard extends StatelessWidget {
  const _TechnicalCard({required this.run});

  final ActivityRun run;

  @override
  Widget build(BuildContext context) {
    return Card(
      clipBehavior: Clip.antiAlias,
      child: ExpansionTile(
        key: activityTechnicalDetailsKey,
        initiallyExpanded: false,
        title: const Text('Technical details'),
        subtitle: const Text('Identifiers, release, and trace'),
        childrenPadding: const EdgeInsets.fromLTRB(18, 0, 18, 18),
        expandedCrossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _DetailField(label: 'Run ID', value: run.runId),
          const SizedBox(height: 14),
          _DetailField(label: 'Feature ID', value: run.featureId),
          const SizedBox(height: 14),
          _DetailField(label: 'Installation ID', value: run.installationId),
          const SizedBox(height: 14),
          _DetailField(label: 'Release identity', value: run.releaseDigest),
          const SizedBox(height: 14),
          _DetailField(label: 'Input kind', value: run.inputKind),
          const SizedBox(height: 14),
          _DetailField(label: 'Trace reference', value: run.traceReference),
          if (run.startedAt case final startedAt?) ...[
            const SizedBox(height: 14),
            _DetailField(
              label: 'Started',
              value: formatActivityTimestamp(startedAt),
            ),
          ],
          if (run.completedAt case final completedAt?) ...[
            const SizedBox(height: 14),
            _DetailField(
              label: 'Completed',
              value: formatActivityTimestamp(completedAt),
            ),
          ],
          if (run.resultSurfaceReference case final reference?) ...[
            const SizedBox(height: 14),
            _DetailField(label: 'Result reference', value: reference),
          ],
        ],
      ),
    );
  }
}

class _SectionCard extends StatelessWidget {
  const _SectionCard({required this.title, required this.child});

  final String title;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(18),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(title, style: Theme.of(context).textTheme.titleLarge),
            const SizedBox(height: 16),
            child,
          ],
        ),
      ),
    );
  }
}

class _DetailField extends StatelessWidget {
  const _DetailField({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(label, style: Theme.of(context).textTheme.labelLarge),
        const SizedBox(height: 4),
        Text(value),
      ],
    );
  }
}

class _LabelChip extends StatelessWidget {
  const _LabelChip({
    required this.icon,
    required this.label,
    required this.color,
  });

  final IconData icon;
  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Chip(
      avatar: Icon(icon, size: 17, color: color),
      label: Text(label),
      side: BorderSide(color: color.withValues(alpha: 0.4)),
      backgroundColor: color.withValues(alpha: 0.08),
    );
  }
}

class _ReferenceButton extends StatelessWidget {
  const _ReferenceButton({
    super.key,
    required this.icon,
    required this.label,
    required this.onPressed,
  });

  final IconData icon;
  final String label;
  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) {
    return OutlinedButton.icon(
      onPressed: onPressed,
      icon: Icon(icon),
      label: Text(label),
    );
  }
}

IconData _statusIcon(ActivityStatus status) => switch (status) {
  ActivityStatus.queued => Icons.schedule,
  ActivityStatus.running => Icons.play_circle_outline,
  ActivityStatus.waitingForApproval => Icons.approval_outlined,
  ActivityStatus.completed => Icons.check_circle_outline,
  ActivityStatus.failed => Icons.error_outline,
  ActivityStatus.parked => Icons.pause_circle_outline,
};

IconData _originIcon(ActivityOrigin origin) => switch (origin) {
  ActivityOrigin.chat => Icons.chat_bubble_outline,
  ActivityOrigin.direct => Icons.near_me_outlined,
  ActivityOrigin.schedule => Icons.schedule_outlined,
  ActivityOrigin.event => Icons.bolt_outlined,
};

IconData _authorityIcon(ActivityAuthority authority) => switch (authority) {
  ActivityAuthority.authorized => Icons.verified_user_outlined,
  ActivityAuthority.waitingForApproval => Icons.lock_clock_outlined,
  ActivityAuthority.paused => Icons.pause_outlined,
};

Color _statusColor(BuildContext context, ActivityStatus status) {
  final colors = Theme.of(context).colorScheme;
  return switch (status) {
    ActivityStatus.failed || ActivityStatus.parked => colors.error,
    ActivityStatus.waitingForApproval => colors.tertiary,
    ActivityStatus.completed => colors.primary,
    _ => colors.secondary,
  };
}

Color _authorityColor(BuildContext context, ActivityAuthority authority) {
  final colors = Theme.of(context).colorScheme;
  return switch (authority) {
    ActivityAuthority.authorized => colors.primary,
    ActivityAuthority.waitingForApproval => colors.tertiary,
    ActivityAuthority.paused => colors.error,
  };
}
