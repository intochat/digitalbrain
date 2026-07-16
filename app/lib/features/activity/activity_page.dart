import 'dart:async';

import 'package:flutter/material.dart';

import 'activity_controller.dart';
import 'activity_formatting.dart';
import 'activity_gateway.dart';
import 'activity_models.dart';
import 'activity_run_detail.dart';

export 'activity_run_detail.dart';

const activityListKey = ValueKey('activity-list');
const activityStatusFilterKey = ValueKey('activity-status-filter');
const activityOriginFilterKey = ValueKey('activity-origin-filter');
const activityFeatureFilterKey = ValueKey('activity-feature-filter');
const activityClearFiltersButtonKey = ValueKey('activity-clear-filters');
const activityRetryButtonKey = ValueKey('activity-retry');
const activityRefreshButtonKey = ValueKey('activity-refresh');

Key activityRunCardKey(String runId) => ValueKey('activity-run-$runId');

class ActivityPage extends StatefulWidget {
  const ActivityPage({
    super.key,
    this.gateway,
    this.controller,
    this.onRunSelected,
    this.onOpenFeature,
    this.onOpenConversation,
    this.onOpenRequest,
    this.onOpenConversationContext,
    this.onOpenAutomation,
    this.onOpenResultSurface,
  }) : assert(gateway != null || controller != null),
       assert(gateway == null || controller == null);

  final ActivityGateway? gateway;
  final ActivityController? controller;
  final ValueChanged<ActivityRun>? onRunSelected;
  final ValueChanged<String>? onOpenFeature;
  final ValueChanged<String>? onOpenConversation;
  final ValueChanged<String>? onOpenRequest;
  final void Function(String conversationId, String requestId)?
  onOpenConversationContext;
  final ActivityAutomationReferenceCallback? onOpenAutomation;
  final ValueChanged<String>? onOpenResultSurface;

  @override
  State<ActivityPage> createState() => _ActivityPageState();
}

class _ActivityPageState extends State<ActivityPage> {
  late final ActivityController _controller;
  late final bool _ownsController;

  @override
  void initState() {
    super.initState();
    _ownsController = widget.controller == null;
    _controller =
        widget.controller ?? ActivityController(gateway: widget.gateway!);
    _controller.addListener(_refresh);
    if (_controller.state == ActivityLoadState.idle) {
      unawaited(_controller.load());
    }
  }

  @override
  void dispose() {
    _controller.removeListener(_refresh);
    if (_ownsController) _controller.dispose();
    super.dispose();
  }

  void _refresh() {
    if (mounted) setState(() {});
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: SafeArea(
        child: LayoutBuilder(
          builder: (context, constraints) {
            final horizontalPadding = constraints.maxWidth < 600 ? 16.0 : 28.0;
            final wide = constraints.maxWidth >= 960;
            return Padding(
              padding: EdgeInsets.fromLTRB(
                horizontalPadding,
                20,
                horizontalPadding,
                0,
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  _ActivityHeader(
                    isRefreshing: _controller.isRefreshing,
                    onRefresh: _controller.refresh,
                  ),
                  const SizedBox(height: 18),
                  _ActivityFilters(controller: _controller),
                  const SizedBox(height: 16),
                  Expanded(child: _content(wide)),
                ],
              ),
            );
          },
        ),
      ),
    );
  }

  Widget _content(bool wide) {
    if (_controller.isInitialLoading) return const _ActivityLoading();
    if (_controller.runs.isEmpty &&
        _controller.state == ActivityLoadState.failed) {
      return _ActivityFailureState(
        failure: _controller.failure!,
        onRetry: _controller.retry,
      );
    }
    if (_controller.runs.isEmpty) {
      return _controller.hasActiveFilters
          ? const _FilteredEmpty()
          : const _ActivityEmpty();
    }
    if (_controller.filteredRuns.isEmpty) {
      return const _FilteredEmpty();
    }
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (_controller.isRefreshing) const LinearProgressIndicator(),
        if (_controller.state == ActivityLoadState.failed)
          Padding(
            padding: const EdgeInsets.only(bottom: 12),
            child: _InlineFailure(
              failure: _controller.failure!,
              onRetry: _controller.retry,
            ),
          ),
        Expanded(
          child: wide
              ? Row(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Expanded(
                      flex: 11,
                      child: _ActivityRunList(
                        controller: _controller,
                        onOpenRun: (run) => _openRun(run, wide: true),
                      ),
                    ),
                    const SizedBox(width: 16),
                    Expanded(
                      flex: 9,
                      child: DecoratedBox(
                        decoration: BoxDecoration(
                          border: Border.all(
                            color: Theme.of(context).colorScheme.outlineVariant,
                          ),
                          borderRadius: BorderRadius.circular(16),
                        ),
                        child: ActivityRunDetailView(
                          key: ValueKey(_controller.selectedRun!.runId),
                          run: _controller.selectedRun!,
                          onOpenFeature: widget.onOpenFeature,
                          onOpenConversation: _openConversation(
                            _controller.selectedRun!,
                          ),
                          onOpenRequest: _openRequest(_controller.selectedRun!),
                          onOpenAutomation: widget.onOpenAutomation,
                          onOpenResultSurface: widget.onOpenResultSurface,
                        ),
                      ),
                    ),
                  ],
                )
              : _ActivityRunList(
                  controller: _controller,
                  onOpenRun: (run) => _openRun(run, wide: false),
                ),
        ),
      ],
    );
  }

  void _openRun(ActivityRun run, {required bool wide}) {
    if (wide) {
      _controller.selectRun(run.runId);
      return;
    }
    final onRunSelected = widget.onRunSelected;
    if (onRunSelected != null) {
      onRunSelected(run);
      return;
    }
    unawaited(
      Navigator.of(context).push<void>(
        MaterialPageRoute(
          builder: (context) => ActivityRunDetailPage(
            run: run,
            onOpenFeature: widget.onOpenFeature,
            onOpenConversation: _openConversation(run),
            onOpenRequest: _openRequest(run),
            onOpenAutomation: widget.onOpenAutomation,
            onOpenResultSurface: widget.onOpenResultSurface,
          ),
        ),
      ),
    );
  }

  ValueChanged<String>? _openConversation(ActivityRun run) {
    final openContext = widget.onOpenConversationContext;
    final requestId = run.requestId;
    if (openContext == null || requestId == null) {
      return widget.onOpenConversation;
    }
    return (conversationId) => openContext(conversationId, requestId);
  }

  ValueChanged<String>? _openRequest(ActivityRun run) {
    final openContext = widget.onOpenConversationContext;
    final conversationId = run.conversationId;
    if (openContext == null || conversationId == null) {
      return widget.onOpenRequest;
    }
    return (requestId) => openContext(conversationId, requestId);
  }
}

class _ActivityHeader extends StatelessWidget {
  const _ActivityHeader({required this.isRefreshing, required this.onRefresh});

  final bool isRefreshing;
  final Future<void> Function() onRefresh;

  @override
  Widget build(BuildContext context) {
    final copy = Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text('Activity', style: Theme.of(context).textTheme.headlineLarge),
        const SizedBox(height: 6),
        Text(
          'Every Run, from request to result.',
          style: Theme.of(context).textTheme.bodyLarge?.copyWith(
            color: Theme.of(context).colorScheme.onSurfaceVariant,
          ),
        ),
      ],
    );
    final refresh = IconButton.filledTonal(
      key: activityRefreshButtonKey,
      tooltip: isRefreshing ? 'Refreshing Activity' : 'Refresh Activity',
      onPressed: isRefreshing ? null : onRefresh,
      icon: const Icon(Icons.refresh),
    );
    return LayoutBuilder(
      builder: (context, constraints) {
        if (constraints.maxWidth < 600) {
          return Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              copy,
              const SizedBox(height: 8),
              Align(alignment: Alignment.centerRight, child: refresh),
            ],
          );
        }
        return Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(child: copy),
            const SizedBox(width: 12),
            refresh,
          ],
        );
      },
    );
  }
}

class _ActivityFilters extends StatelessWidget {
  const _ActivityFilters({required this.controller});

  final ActivityController controller;

  @override
  Widget build(BuildContext context) {
    final featureOptions = controller.availableFeatures
        .map(
          (feature) =>
              _FilterOption<String>(value: feature.id, label: feature.name),
        )
        .toList(growable: false);
    final filters = <Widget>[
      _FilterMenu<ActivityStatus>(
        key: activityStatusFilterKey,
        name: 'Status',
        selected: controller.statusFilter,
        options: ActivityStatus.values
            .map((status) => _FilterOption(value: status, label: status.label))
            .toList(growable: false),
        onSelected: controller.setStatusFilter,
      ),
      _FilterMenu<ActivityOrigin>(
        key: activityOriginFilterKey,
        name: 'Origin',
        selected: controller.originFilter,
        options: ActivityOrigin.values
            .map((origin) => _FilterOption(value: origin, label: origin.label))
            .toList(growable: false),
        onSelected: controller.setOriginFilter,
      ),
      _FilterMenu<String>(
        key: activityFeatureFilterKey,
        name: 'Feature',
        selected: controller.featureFilter,
        options: featureOptions,
        onSelected: controller.setFeatureFilter,
      ),
      if (controller.hasActiveFilters)
        TextButton.icon(
          key: activityClearFiltersButtonKey,
          onPressed: controller.clearFilters,
          icon: const Icon(Icons.filter_alt_off_outlined),
          label: const Text('Clear filters'),
        ),
    ];
    return Semantics(
      container: true,
      label: 'Activity filters',
      child: LayoutBuilder(
        builder: (context, constraints) {
          if (constraints.maxWidth < 600) {
            return SingleChildScrollView(
              scrollDirection: Axis.horizontal,
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  for (var index = 0; index < filters.length; index++) ...[
                    if (index > 0) const SizedBox(width: 8),
                    filters[index],
                  ],
                ],
              ),
            );
          }
          return Wrap(
            spacing: 8,
            runSpacing: 8,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: filters,
          );
        },
      ),
    );
  }
}

class _FilterMenu<T extends Object> extends StatelessWidget {
  const _FilterMenu({
    super.key,
    required this.name,
    required this.selected,
    required this.options,
    required this.onSelected,
  });

  final String name;
  final T? selected;
  final List<_FilterOption<T>> options;
  final ValueChanged<T?> onSelected;

  @override
  Widget build(BuildContext context) {
    final all = _FilterOption<T>(value: null, label: 'All');
    final selectedLabel = selected == null
        ? all.label
        : options
                  .where((option) => option.value == selected)
                  .map((option) => option.label)
                  .firstOrNull ??
              all.label;
    final menuOptions = [all, ...options];
    return PopupMenuButton<_FilterOption<T>>(
      tooltip: 'Filter by $name',
      onSelected: (option) => onSelected(option.value),
      itemBuilder: (context) => [
        for (final option in menuOptions)
          PopupMenuItem(
            value: option,
            child: Row(
              children: [
                SizedBox(
                  width: 24,
                  child: option.value == selected
                      ? const Icon(Icons.check, size: 18)
                      : null,
                ),
                const SizedBox(width: 8),
                Flexible(
                  child: Text(
                    option.label,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
              ],
            ),
          ),
      ],
      child: Semantics(
        button: true,
        label: '$name filter: $selectedLabel',
        child: Container(
          constraints: const BoxConstraints(maxWidth: 230),
          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
          decoration: BoxDecoration(
            border: Border.all(color: Theme.of(context).colorScheme.outline),
            borderRadius: BorderRadius.circular(24),
          ),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Flexible(
                child: Text(
                  '$name: $selectedLabel',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
              ),
              const SizedBox(width: 6),
              const Icon(Icons.arrow_drop_down, size: 20),
            ],
          ),
        ),
      ),
    );
  }
}

class _FilterOption<T> {
  const _FilterOption({required this.value, required this.label});

  final T? value;
  final String label;
}

class _ActivityRunList extends StatelessWidget {
  const _ActivityRunList({required this.controller, required this.onOpenRun});

  final ActivityController controller;
  final ValueChanged<ActivityRun> onOpenRun;

  @override
  Widget build(BuildContext context) {
    final runs = controller.filteredRuns;
    return ListView.separated(
      key: activityListKey,
      padding: const EdgeInsets.only(bottom: 28),
      itemCount: runs.length,
      separatorBuilder: (context, index) => const SizedBox(height: 10),
      itemBuilder: (context, index) {
        final run = runs[index];
        return _ActivityRunCard(
          run: run,
          selected: controller.selectedRun?.runId == run.runId,
          autofocus: index == 0,
          onPressed: () => onOpenRun(run),
        );
      },
    );
  }
}

class _ActivityRunCard extends StatelessWidget {
  const _ActivityRunCard({
    required this.run,
    required this.selected,
    required this.autofocus,
    required this.onPressed,
  });

  final ActivityRun run;
  final bool selected;
  final bool autofocus;
  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    final statusColor = _statusColor(context, run.status);
    return Semantics(
      button: true,
      selected: selected,
      label:
          '${run.featureName}, ${run.status.label}, ${run.origin.label}, '
          '${selected ? 'selected' : 'not selected'}',
      child: Card(
        margin: EdgeInsets.zero,
        clipBehavior: Clip.antiAlias,
        color: selected
            ? colors.primaryContainer.withValues(alpha: 0.45)
            : null,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(14),
          side: BorderSide(
            color: selected ? colors.primary : colors.outlineVariant,
          ),
        ),
        child: InkWell(
          key: activityRunCardKey(run.runId),
          autofocus: autofocus,
          onTap: onPressed,
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Icon(_statusIcon(run.status), color: statusColor, size: 22),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Text(
                        run.featureName,
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                        style: Theme.of(context).textTheme.titleMedium,
                      ),
                    ),
                    const SizedBox(width: 8),
                    _CompactStatus(label: run.status.label, color: statusColor),
                  ],
                ),
                const SizedBox(height: 12),
                Wrap(
                  spacing: 12,
                  runSpacing: 6,
                  children: [
                    _InlineMetadata(
                      icon: _originIcon(run.origin),
                      label: run.origin.label,
                    ),
                    _InlineMetadata(
                      icon: Icons.schedule_outlined,
                      label: formatActivityTimestamp(run.occurredAt),
                    ),
                    _InlineMetadata(
                      icon: Icons.replay_outlined,
                      label:
                          '${run.attempts} ${run.attempts == 1 ? 'attempt' : 'attempts'}',
                    ),
                  ],
                ),
                if (run.safeFailure case final failure?) ...[
                  const SizedBox(height: 10),
                  Text(
                    failure,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(color: colors.error),
                  ),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _CompactStatus extends StatelessWidget {
  const _CompactStatus({required this.label, required this.color});

  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      constraints: const BoxConstraints(maxWidth: 160),
      padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 5),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.1),
        borderRadius: BorderRadius.circular(99),
      ),
      child: Text(
        label,
        maxLines: 2,
        overflow: TextOverflow.ellipsis,
        style: Theme.of(context).textTheme.labelMedium?.copyWith(color: color),
      ),
    );
  }
}

class _InlineMetadata extends StatelessWidget {
  const _InlineMetadata({required this.icon, required this.label});

  final IconData icon;
  final String label;

  @override
  Widget build(BuildContext context) {
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(
          icon,
          size: 16,
          color: Theme.of(context).colorScheme.onSurfaceVariant,
        ),
        const SizedBox(width: 5),
        Flexible(
          child: Text(
            label,
            style: Theme.of(context).textTheme.bodySmall?.copyWith(
              color: Theme.of(context).colorScheme.onSurfaceVariant,
            ),
          ),
        ),
      ],
    );
  }
}

class _ActivityLoading extends StatelessWidget {
  const _ActivityLoading();

  @override
  Widget build(BuildContext context) {
    return Semantics(
      container: true,
      liveRegion: true,
      label: 'Loading Activity',
      child: const Center(child: CircularProgressIndicator()),
    );
  }
}

class _ActivityEmpty extends StatelessWidget {
  const _ActivityEmpty();

  @override
  Widget build(BuildContext context) {
    return const _EmptyContent(
      icon: Icons.history_toggle_off_outlined,
      title: 'No activity yet',
      message: 'Runs will appear here when a Feature starts.',
    );
  }
}

class _FilteredEmpty extends StatelessWidget {
  const _FilteredEmpty();

  @override
  Widget build(BuildContext context) {
    return const _EmptyContent(
      icon: Icons.filter_alt_off_outlined,
      title: 'No runs match these filters',
      message: 'Clear one or more filters to see other Runs.',
    );
  }
}

class _EmptyContent extends StatelessWidget {
  const _EmptyContent({
    required this.icon,
    required this.title,
    required this.message,
  });

  final IconData icon;
  final String title;
  final String message;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: SingleChildScrollView(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              icon,
              size: 44,
              color: Theme.of(context).colorScheme.onSurfaceVariant,
            ),
            const SizedBox(height: 14),
            Text(title, style: Theme.of(context).textTheme.titleLarge),
            const SizedBox(height: 6),
            Text(message, textAlign: TextAlign.center),
          ],
        ),
      ),
    );
  }
}

class _ActivityFailureState extends StatelessWidget {
  const _ActivityFailureState({required this.failure, required this.onRetry});

  final ActivityFailure failure;
  final Future<void> Function() onRetry;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: SingleChildScrollView(
        padding: const EdgeInsets.all(24),
        child: Semantics(
          container: true,
          liveRegion: true,
          label: 'Activity is unavailable. ${failure.message}',
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(
                Icons.cloud_off_outlined,
                size: 44,
                color: Theme.of(context).colorScheme.error,
              ),
              const SizedBox(height: 14),
              Text(
                'Activity is unavailable',
                style: Theme.of(context).textTheme.titleLarge,
              ),
              const SizedBox(height: 6),
              Text(failure.message, textAlign: TextAlign.center),
              if (failure.retryable) ...[
                const SizedBox(height: 16),
                FilledButton.icon(
                  key: activityRetryButtonKey,
                  onPressed: onRetry,
                  icon: const Icon(Icons.refresh),
                  label: const Text('Try again'),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class _InlineFailure extends StatelessWidget {
  const _InlineFailure({required this.failure, required this.onRetry});

  final ActivityFailure failure;
  final Future<void> Function() onRetry;

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    return Semantics(
      container: true,
      liveRegion: true,
      label: failure.message,
      child: Card(
        color: colors.errorContainer,
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Row(
            children: [
              Icon(
                Icons.warning_amber_outlined,
                color: colors.onErrorContainer,
              ),
              const SizedBox(width: 10),
              Expanded(
                child: Text(
                  failure.message,
                  style: TextStyle(color: colors.onErrorContainer),
                ),
              ),
              if (failure.retryable)
                TextButton(
                  key: activityRetryButtonKey,
                  onPressed: onRetry,
                  child: const Text('Retry'),
                ),
            ],
          ),
        ),
      ),
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

Color _statusColor(BuildContext context, ActivityStatus status) {
  final colors = Theme.of(context).colorScheme;
  return switch (status) {
    ActivityStatus.failed || ActivityStatus.parked => colors.error,
    ActivityStatus.waitingForApproval => colors.tertiary,
    ActivityStatus.completed => colors.primary,
    _ => colors.secondary,
  };
}
