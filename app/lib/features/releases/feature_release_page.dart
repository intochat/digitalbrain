import 'dart:async';

import 'package:flutter/material.dart';

import 'feature_release_controller.dart';
import 'feature_release_gateway.dart';
import 'feature_release_models.dart';

const featureReleaseRetryButtonKey = Key('feature-release-retry');
const featureReleaseReloadButtonKey = Key('feature-release-reload');
const featureReleaseRollbackButtonKey = Key('feature-release-rollback');
const featureReleaseConfirmRollbackButtonKey = Key(
  'feature-release-confirm-rollback',
);
const featureReleaseCancelRollbackButtonKey = Key(
  'feature-release-cancel-rollback',
);
const featureReleaseReferencedAutomationKey = Key(
  'feature-release-referenced-automation',
);
const featureReleaseMissingAutomationKey = Key(
  'feature-release-missing-automation',
);

class FeatureAutomationId {
  const FeatureAutomationId._(this.value);

  static FeatureAutomationId? tryParse(String? value) {
    if (value == null ||
        value.isEmpty ||
        value.length > 256 ||
        value.trim() != value ||
        value.runes.any(
          (character) => character < 32 || character >= 127 && character <= 159,
        )) {
      return null;
    }
    return FeatureAutomationId._(value);
  }

  final String value;
}

class FeatureReleasePage extends StatefulWidget {
  const FeatureReleasePage({
    required this.featureId,
    this.expectedReleaseDigest,
    this.onVersionRestored,
    this.restoredOnArrival = false,
    this.automationId,
    required this.gateway,
    super.key,
  });

  final String featureId;
  final String? expectedReleaseDigest;
  final ValueChanged<String>? onVersionRestored;
  final bool restoredOnArrival;
  final FeatureAutomationId? automationId;
  final FeatureReleaseGateway gateway;

  @override
  State<FeatureReleasePage> createState() => _FeatureReleasePageState();
}

class _FeatureReleasePageState extends State<FeatureReleasePage> {
  late final FeatureReleaseController _controller;
  final GlobalKey _automationSectionKey = GlobalKey();
  bool _automationRevealed = false;

  @override
  void initState() {
    super.initState();
    _controller = FeatureReleaseController(
      featureId: widget.featureId,
      expectedReleaseDigest: widget.expectedReleaseDigest,
      gateway: widget.gateway,
    )..addListener(_refresh);
    unawaited(_controller.load());
  }

  @override
  void dispose() {
    _controller
      ..removeListener(_refresh)
      ..dispose();
    super.dispose();
  }

  @override
  void didUpdateWidget(covariant FeatureReleasePage oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.automationId?.value != widget.automationId?.value) {
      _automationRevealed = false;
    }
  }

  void _refresh() {
    if (mounted) setState(() {});
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Feature Version')),
      body: SafeArea(child: _body(context)),
    );
  }

  Widget _body(BuildContext context) {
    final details = _controller.details;
    if (_controller.status == FeatureReleaseStatus.loading && details == null) {
      return const Center(child: CircularProgressIndicator());
    }
    if (_controller.status == FeatureReleaseStatus.loadFailed &&
        details == null) {
      return _CenteredFailure(
        failure: _controller.failure!,
        onRetry: _controller.retry,
      );
    }
    if (details == null) return const SizedBox.shrink();
    _scheduleAutomationReveal();
    return SelectionArea(
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(24, 20, 24, 40),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (_controller.status == FeatureReleaseStatus.rollingBack)
              const Padding(
                padding: EdgeInsets.only(bottom: 16),
                child: LinearProgressIndicator(),
              ),
            if (_controller.status == FeatureReleaseStatus.restored ||
                widget.restoredOnArrival)
              const _SuccessBanner(),
            if (_controller.status == FeatureReleaseStatus.rollbackFailed)
              _InlineFailure(
                failure: _controller.failure!,
                onRetry: _controller.retry,
              ),
            _StatusCard(details: details),
            const SizedBox(height: 16),
            _VersionCard(version: details.activeVersion),
            const SizedBox(height: 16),
            _RequestCard(request: details.originatingRequest),
            const SizedBox(height: 16),
            _AccessCard(grants: details.activeGrants),
            const SizedBox(height: 16),
            _AutomationCard(
              key: _automationSectionKey,
              subscriptions: details.subscriptions,
              automationId: widget.automationId,
            ),
            const SizedBox(height: 16),
            _RollbackCard(
              previousVersion: details.previousVersion,
              enabled: _controller.canRollback,
              onRollback: _confirmRollback,
            ),
          ],
        ),
      ),
    );
  }

  void _scheduleAutomationReveal() {
    if (widget.automationId == null || _automationRevealed) return;
    _automationRevealed = true;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      final automationContext = _automationSectionKey.currentContext;
      if (automationContext == null) return;
      unawaited(
        Scrollable.ensureVisible(
          automationContext,
          alignment: 0.1,
          duration: Duration.zero,
        ),
      );
    });
  }

  Future<void> _confirmRollback() async {
    final target = _controller.details?.previousVersion;
    if (target == null) return;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Roll back to the previous Version?'),
        content: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Text(
                'This restores the previous Version and its immutable source.',
              ),
              const SizedBox(height: 16),
              const Text('Version identity'),
              const SizedBox(height: 4),
              Text(target.digest),
              const SizedBox(height: 12),
              const Text('Source'),
              const SizedBox(height: 4),
              Text(target.sourceReference),
            ],
          ),
        ),
        actions: [
          TextButton(
            key: featureReleaseCancelRollbackButtonKey,
            onPressed: () => Navigator.of(dialogContext).pop(false),
            child: const Text('Keep current Version'),
          ),
          FilledButton(
            key: featureReleaseConfirmRollbackButtonKey,
            onPressed: () => Navigator.of(dialogContext).pop(true),
            child: const Text('Roll back'),
          ),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;
    await _controller.rollback();
    if (!mounted || _controller.status != FeatureReleaseStatus.restored) return;
    final restored = _controller.details;
    if (restored == null) return;
    widget.onVersionRestored?.call(restored.activeVersion.digest);
  }
}

class _StatusCard extends StatelessWidget {
  const _StatusCard({required this.details});

  final FeatureReleaseDetails details;

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(
              details.paused ? Icons.pause_circle_outline : Icons.check_circle,
              color: details.paused ? colors.error : colors.primary,
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    details.paused ? 'Paused' : 'Active',
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  if (details.pauseReason case final reason?) ...[
                    const SizedBox(height: 4),
                    Text(reason),
                  ],
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _VersionCard extends StatelessWidget {
  const _VersionCard({required this.version});

  final FeatureReleaseVersion version;

  @override
  Widget build(BuildContext context) {
    return _SectionCard(
      title: 'Active Version',
      children: [
        const _FieldLabel('Version identity'),
        Text(version.digest),
        const SizedBox(height: 16),
        const _FieldLabel('Source'),
        Text(version.sourceKindLabel),
        const SizedBox(height: 4),
        Text(version.sourceReference),
        const SizedBox(height: 16),
        const _FieldLabel('Implementation'),
        Text(version.source.implementationProjectPath),
        const SizedBox(height: 8),
        const _FieldLabel('Scenarios'),
        Text(version.source.scenarioProjectPath),
      ],
    );
  }
}

class _RequestCard extends StatelessWidget {
  const _RequestCard({required this.request});

  final FeatureReleaseOriginatingRequest request;

  @override
  Widget build(BuildContext context) {
    return _SectionCard(
      title: 'Original request',
      children: [Text(request.text)],
    );
  }
}

class _AccessCard extends StatelessWidget {
  const _AccessCard({required this.grants});

  final List<FeatureReleaseGrant> grants;

  @override
  Widget build(BuildContext context) {
    return _SectionCard(
      title: 'Access',
      children: grants.isEmpty
          ? const [Text('No access is required.')]
          : [
              for (var index = 0; index < grants.length; index++) ...[
                if (index > 0) const Divider(height: 28),
                Text(
                  '${grants[index].capabilityId} · v${grants[index].capabilityVersion}',
                  style: Theme.of(context).textTheme.titleSmall,
                ),
                const SizedBox(height: 4),
                Text(
                  '${grants[index].provider ?? 'Built-in'} · '
                  '${grants[index].connectionId ?? 'No connection'}',
                ),
                const SizedBox(height: 4),
                Text(grants[index].constraintSummary),
              ],
            ],
    );
  }
}

class _AutomationCard extends StatelessWidget {
  const _AutomationCard({
    required this.subscriptions,
    required this.automationId,
    super.key,
  });

  final List<String> subscriptions;
  final FeatureAutomationId? automationId;

  @override
  Widget build(BuildContext context) {
    final requestedId = automationId?.value;
    final containsRequestedId = subscriptions.contains(requestedId);
    return _SectionCard(
      title: 'Automation',
      highlighted: requestedId != null,
      children: subscriptions.isEmpty
          ? [
              const Text('No Automations are configured.'),
              if (requestedId != null) ...[
                const SizedBox(height: 12),
                _MissingAutomation(automationId: requestedId),
              ],
            ]
          : [
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  for (final subscription in subscriptions)
                    _AutomationChip(
                      subscription: subscription,
                      referenced: subscription == requestedId,
                    ),
                ],
              ),
              if (requestedId != null && !containsRequestedId) ...[
                const SizedBox(height: 12),
                _MissingAutomation(automationId: requestedId),
              ],
            ],
    );
  }
}

class _AutomationChip extends StatelessWidget {
  const _AutomationChip({required this.subscription, required this.referenced});

  final String subscription;
  final bool referenced;

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    final chip = Chip(
      avatar: referenced ? const Icon(Icons.link, size: 18) : null,
      backgroundColor: referenced ? colors.primaryContainer : null,
      side: referenced ? BorderSide(color: colors.primary, width: 2) : null,
      label: Text(subscription),
    );
    if (!referenced) return chip;
    return Semantics(
      key: featureReleaseReferencedAutomationKey,
      container: true,
      selected: true,
      label: 'Referenced Automation $subscription',
      child: ExcludeSemantics(child: chip),
    );
  }
}

class _MissingAutomation extends StatelessWidget {
  const _MissingAutomation({required this.automationId});

  final String automationId;

  @override
  Widget build(BuildContext context) {
    final message = 'Automation $automationId is not in the active Version.';
    return Semantics(
      key: featureReleaseMissingAutomationKey,
      container: true,
      label: message,
      child: ExcludeSemantics(
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Icon(Icons.info_outline, size: 20),
            const SizedBox(width: 8),
            Expanded(child: Text(message)),
          ],
        ),
      ),
    );
  }
}

class _RollbackCard extends StatelessWidget {
  const _RollbackCard({
    required this.previousVersion,
    required this.enabled,
    required this.onRollback,
  });

  final FeatureReleaseVersion? previousVersion;
  final bool enabled;
  final VoidCallback onRollback;

  @override
  Widget build(BuildContext context) {
    if (previousVersion == null) {
      return const _SectionCard(
        title: 'Rollback',
        children: [Text('No previous Version is available.')],
      );
    }
    return _SectionCard(
      title: 'Rollback',
      children: [
        const Text('A previous Version is available for exact restoration.'),
        const SizedBox(height: 16),
        Align(
          alignment: Alignment.centerLeft,
          child: FilledButton.icon(
            key: featureReleaseRollbackButtonKey,
            onPressed: enabled ? onRollback : null,
            icon: const Icon(Icons.history),
            label: const Text('Roll back'),
          ),
        ),
      ],
    );
  }
}

class _SectionCard extends StatelessWidget {
  const _SectionCard({
    required this.title,
    required this.children,
    this.highlighted = false,
  });

  final String title;
  final List<Widget> children;
  final bool highlighted;

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    return Card(
      color: highlighted ? colors.secondaryContainer : null,
      shape: highlighted
          ? RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(12),
              side: BorderSide(color: colors.primary, width: 2),
            )
          : null,
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(title, style: Theme.of(context).textTheme.titleLarge),
            const SizedBox(height: 16),
            ...children,
          ],
        ),
      ),
    );
  }
}

class _FieldLabel extends StatelessWidget {
  const _FieldLabel(this.text);

  final String text;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 4),
      child: Text(text, style: Theme.of(context).textTheme.labelLarge),
    );
  }
}

class _SuccessBanner extends StatelessWidget {
  const _SuccessBanner();

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    return Semantics(
      container: true,
      liveRegion: true,
      label: 'Previous Version restored exactly',
      child: Card(
        color: colors.primaryContainer,
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Row(
            children: [
              Icon(Icons.restore, color: colors.onPrimaryContainer),
              const SizedBox(width: 12),
              Expanded(
                child: Text(
                  'Previous Version restored exactly',
                  style: TextStyle(color: colors.onPrimaryContainer),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _CenteredFailure extends StatelessWidget {
  const _CenteredFailure({required this.failure, required this.onRetry});

  final FeatureReleaseFailure failure;
  final Future<void> Function() onRetry;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: _FailureContent(failure: failure, onRetry: onRetry),
      ),
    );
  }
}

class _InlineFailure extends StatelessWidget {
  const _InlineFailure({required this.failure, required this.onRetry});

  final FeatureReleaseFailure failure;
  final Future<void> Function() onRetry;

  @override
  Widget build(BuildContext context) {
    return Semantics(
      container: true,
      liveRegion: true,
      label: failure.message,
      child: Card(
        color: Theme.of(context).colorScheme.errorContainer,
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: _FailureContent(failure: failure, onRetry: onRetry),
        ),
      ),
    );
  }
}

class _FailureContent extends StatelessWidget {
  const _FailureContent({required this.failure, required this.onRetry});

  final FeatureReleaseFailure failure;
  final Future<void> Function() onRetry;

  @override
  Widget build(BuildContext context) {
    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        const Icon(Icons.error_outline),
        const SizedBox(height: 12),
        Text(failure.message, textAlign: TextAlign.center),
        if (failure.retryable || failure.reloadable) ...[
          const SizedBox(height: 12),
          FilledButton(
            key: failure.reloadable
                ? featureReleaseReloadButtonKey
                : featureReleaseRetryButtonKey,
            onPressed: onRetry,
            child: Text(failure.reloadable ? 'Reload' : 'Retry'),
          ),
        ],
      ],
    );
  }
}
