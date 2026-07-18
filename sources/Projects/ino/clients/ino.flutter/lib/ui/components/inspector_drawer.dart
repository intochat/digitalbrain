import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/grpc/generated/ino.pb.dart' as pb;
import 'package:ino_flutter/state/ino_bloc.dart';
import 'package:ino_flutter/state/proposals_bloc.dart';
import 'package:ino_flutter/state/routing_bloc.dart';
import 'package:ino_flutter/state/timeline_bloc.dart';

/// Launches the inspector drawer. Pass [entry] to inspect a specific synapse
/// firing (tapped from the Trace view); pass null to inspect the gateway itself
/// (tapped from the Mind orb). The drawer reads its backing data off the
/// already-running TimelineBloc — the server-side introspection RPCs
/// (GetJournal / GetMetrics / GetReasoning, slice 13) wire in once the dart
/// stubs regenerate.
Future<void> showInspectorDrawer(BuildContext context, {TimelineEntry? entry}) {
  return showModalBottomSheet<void>(
    context: context,
    backgroundColor: Colors.transparent,
    isScrollControlled: true,
    useRootNavigator: true,
    builder: (_) => MultiBlocProvider(
      providers: [
        BlocProvider.value(value: context.read<TimelineBloc>()),
        BlocProvider.value(value: context.read<ProposalsBloc>()),
        BlocProvider.value(value: context.read<RoutingBloc>()),
        BlocProvider.value(value: context.read<InoBloc>()),
      ],
      child: InspectorDrawer(entry: entry),
    ),
  );
}

class InspectorDrawer extends StatelessWidget {
  const InspectorDrawer({super.key, this.entry});

  final TimelineEntry? entry;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    final title = entry == null ? 'Ino gateway' : entry!.target;
    final subtitle = entry == null
        ? 'Kernel silo · system'
        : '${entry!.source} \u2192 ${entry!.target}';

    return DraggableScrollableSheet(
      initialChildSize: 0.7,
      minChildSize: 0.4,
      maxChildSize: 0.95,
      expand: false,
      builder: (context, scrollController) {
        return Container(
          decoration: BoxDecoration(
            color: colorScheme.surface,
            borderRadius: const BorderRadius.vertical(top: Radius.circular(20)),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              _DragHandle(),
              _Header(title: title, subtitle: subtitle),
              const Divider(height: 1),
              Expanded(
                child: SingleChildScrollView(
                  controller: scrollController,
                  padding: const EdgeInsets.symmetric(vertical: 12),
                  child: Column(
                    children: [
                      _IdentityPanel(entry: entry),
                      _StatePanel(entry: entry),
                      _ReasoningPanel(entry: entry),
                      const _RoutingPanel(),
                      const _ProposalsPanel(),
                      _MetricsPanel(entry: entry),
                      const _StubPanel(
                        title: 'Actions',
                        icon: Icons.bolt,
                        hint: 'Fire an ad-hoc synapse · post-v0.1',
                      ),
                      const _StubPanel(
                        title: 'Scheduling',
                        icon: Icons.schedule,
                        hint: 'Arm reminders and cadence triggers · post-v0.1',
                      ),
                      const _StubPanel(
                        title: 'Integrations',
                        icon: Icons.extension,
                        hint: 'External SDK / MCP bridges · post-v0.1',
                      ),
                      const SizedBox(height: 24),
                    ],
                  ),
                ),
              ),
            ],
          ),
        );
      },
    );
  }
}

class _DragHandle extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    return Center(
      child: Container(
        margin: const EdgeInsets.only(top: 8, bottom: 4),
        width: 36,
        height: 4,
        decoration: BoxDecoration(
          color: Colors.white.withAlpha(60),
          borderRadius: BorderRadius.circular(2),
        ),
      ),
    );
  }
}

class _Header extends StatelessWidget {
  const _Header({required this.title, required this.subtitle});
  final String title;
  final String subtitle;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.fromLTRB(20, 8, 20, 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            title,
            style: TextStyle(
              color: colorScheme.onSurface,
              fontSize: 17,
              fontWeight: FontWeight.w600,
            ),
          ),
          const SizedBox(height: 2),
          Text(
            subtitle,
            style: TextStyle(
              color: colorScheme.onSurface.withAlpha(140),
              fontSize: 12,
            ),
          ),
        ],
      ),
    );
  }
}

class _PanelFrame extends StatelessWidget {
  const _PanelFrame({
    required this.icon,
    required this.title,
    required this.child,
    this.badge,
  });

  final IconData icon;
  final String title;
  final Widget child;
  final Widget? badge;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: Container(
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(
          color: colorScheme.surfaceContainerHighest.withAlpha(80),
          borderRadius: BorderRadius.circular(10),
          border: Border.all(color: Colors.white.withAlpha(14)),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(icon, size: 16, color: colorScheme.primary),
                const SizedBox(width: 8),
                Text(
                  title,
                  style: TextStyle(
                    color: colorScheme.onSurface,
                    fontSize: 13,
                    fontWeight: FontWeight.w600,
                    letterSpacing: 0.2,
                  ),
                ),
                const Spacer(),
                if (badge != null) badge!,
              ],
            ),
            const SizedBox(height: 10),
            child,
          ],
        ),
      ),
    );
  }
}

class _KvRow extends StatelessWidget {
  const _KvRow({required this.label, required this.value});
  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 2),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: 110,
            child: Text(
              label,
              style: TextStyle(
                color: colorScheme.onSurface.withAlpha(120),
                fontSize: 12,
                fontWeight: FontWeight.w500,
              ),
            ),
          ),
          Expanded(
            child: Text(
              value,
              style: TextStyle(
                color: colorScheme.onSurface,
                fontSize: 12,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _IdentityPanel extends StatelessWidget {
  const _IdentityPanel({required this.entry});
  final TimelineEntry? entry;

  @override
  Widget build(BuildContext context) {
    return _PanelFrame(
      icon: Icons.fingerprint,
      title: 'Identity',
      child: Column(
        children: [
          _KvRow(
            label: 'Neuron',
            value: entry?.target ?? 'Ino.Gateway',
          ),
          _KvRow(
            label: 'Source',
            value: entry?.source ?? 'system-silo',
          ),
          if (entry != null)
            _KvRow(
              label: 'Verb',
              value: entry!.verb ?? entry!.kind,
            ),
          _KvRow(
            label: 'Kind',
            value: entry?.kind ?? 'grain · system',
          ),
        ],
      ),
    );
  }
}

class _StatePanel extends StatelessWidget {
  const _StatePanel({required this.entry});
  final TimelineEntry? entry;

  @override
  Widget build(BuildContext context) {
    final e = entry;
    return _PanelFrame(
      icon: Icons.show_chart,
      title: 'State',
      child: Column(
        children: [
          if (e != null) ...[
            _KvRow(label: 'Sequence', value: '${e.sequence}'),
            _KvRow(label: 'Decay', value: '${e.decay}'),
            _KvRow(
              label: 'Correlation',
              value: e.correlationId ?? '—',
            ),
            _KvRow(
              label: 'Fired',
              value: _formatTimestamp(e.timestamp),
            ),
          ] else
            BlocBuilder<TimelineBloc, TimelineBlocState>(
              buildWhen: (a, b) =>
                  a.events.length != b.events.length || a.isLive != b.isLive,
              builder: (context, state) {
                return Column(
                  children: [
                    _KvRow(
                      label: 'Live tail',
                      value: state.isLive ? 'streaming' : 'paused',
                    ),
                    _KvRow(
                      label: 'Events seen',
                      value: '${state.events.length}',
                    ),
                    _KvRow(
                      label: 'Max sequence',
                      value: '${state.maxSequence}',
                    ),
                  ],
                );
              },
            ),
        ],
      ),
    );
  }
}

String _formatTimestamp(int ms) {
  final dt = DateTime.fromMillisecondsSinceEpoch(ms);
  String p(int n) => n.toString().padLeft(2, '0');
  return '${p(dt.hour)}:${p(dt.minute)}:${p(dt.second)}.${p(dt.millisecond ~/ 10)}';
}

class _ReasoningPanel extends StatelessWidget {
  const _ReasoningPanel({required this.entry});
  final TimelineEntry? entry;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;

    return BlocBuilder<TimelineBloc, TimelineBlocState>(
      buildWhen: (a, b) => a.events.length != b.events.length,
      builder: (context, state) {
        final hit = _pickReasoning(entry, state);
        final sourceLabel = hit?.reasoningSource ?? 'bdd-mock';
        return _PanelFrame(
          icon: Icons.psychology_alt,
          title: 'Reasoning',
          badge: Container(
            padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
            decoration: BoxDecoration(
              color: (hit == null ? Colors.orange : Colors.greenAccent)
                  .withAlpha(40),
              borderRadius: BorderRadius.circular(6),
            ),
            child: Text(
              sourceLabel,
              style: TextStyle(
                color: hit == null ? Colors.orange : Colors.greenAccent,
                fontSize: 10,
                fontWeight: FontWeight.w700,
              ),
            ),
          ),
          child: hit == null
              ? Text(
                  'No BDD scenario has matched a prompt yet. Send a chat that '
                  'a neuron Features/*.feature scenario covers — the '
                  'matched scenario name will render here.',
                  style: TextStyle(
                    color: colorScheme.onSurface.withAlpha(180),
                    fontSize: 12,
                    height: 1.4,
                  ),
                )
              : Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      'mocked via BDD · ${hit.feature ?? '?'} — ${hit.scenario ?? '?'}',
                      style: TextStyle(
                        color: colorScheme.onSurface,
                        fontSize: 12,
                        fontWeight: FontWeight.w600,
                        height: 1.4,
                      ),
                    ),
                    const SizedBox(height: 6),
                    Text(
                      'Target: ${hit.target}',
                      style: TextStyle(
                        color: colorScheme.onSurface.withAlpha(160),
                        fontSize: 11,
                        height: 1.3,
                      ),
                    ),
                  ],
                ),
        );
      },
    );
  }

  static TimelineEntry? _pickReasoning(
    TimelineEntry? focus,
    TimelineBlocState state,
  ) {
    if (focus != null && focus.scenario != null && focus.scenario!.isNotEmpty) {
      return focus;
    }
    // Mind-orb / focus-less inspect: surface the most recent annotated entry.
    for (final e in state.events.reversed) {
      if (e.scenario != null && e.scenario!.isNotEmpty) return e;
    }
    return null;
  }
}

class _MetricsPanel extends StatelessWidget {
  const _MetricsPanel({required this.entry});
  final TimelineEntry? entry;

  @override
  Widget build(BuildContext context) {
    return _PanelFrame(
      icon: Icons.analytics,
      title: 'Metrics',
      child: BlocBuilder<TimelineBloc, TimelineBlocState>(
        buildWhen: (a, b) => a.events.length != b.events.length,
        builder: (context, state) {
          final counts = <String, int>{};
          for (final e in state.events) {
            counts.update(e.target, (v) => v + 1, ifAbsent: () => 1);
          }

          final focus = entry?.target;
          if (focus != null && focus.isNotEmpty) {
            final mine = counts[focus] ?? 0;
            return Column(
              children: [
                _KvRow(label: 'Activations', value: '$mine'),
                _KvRow(label: 'Session total', value: '${state.events.length}'),
              ],
            );
          }

          final top = counts.entries.toList()
            ..sort((a, b) => b.value.compareTo(a.value));
          if (top.isEmpty) {
            return Text(
              'No activations observed in this session yet.',
              style: TextStyle(
                color: Theme.of(context).colorScheme.onSurface.withAlpha(140),
                fontSize: 12,
              ),
            );
          }
          return Column(
            children: [
              for (final e in top.take(5))
                _KvRow(label: _shortName(e.key), value: '${e.value}'),
            ],
          );
        },
      ),
    );
  }

  static String _shortName(String fqn) {
    if (fqn.isEmpty) return '(unknown)';
    final i = fqn.lastIndexOf('.');
    return i < 0 ? fqn : fqn.substring(i + 1);
  }
}

class _StubPanel extends StatelessWidget {
  const _StubPanel({
    required this.title,
    required this.icon,
    required this.hint,
  });
  final String title;
  final IconData icon;
  final String hint;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    return _PanelFrame(
      icon: icon,
      title: title,
      badge: Container(
        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
        decoration: BoxDecoration(
          color: colorScheme.onSurface.withAlpha(20),
          borderRadius: BorderRadius.circular(6),
        ),
        child: Text(
          'stub',
          style: TextStyle(
            color: colorScheme.onSurface.withAlpha(160),
            fontSize: 10,
            fontWeight: FontWeight.w700,
          ),
        ),
      ),
      child: Text(
        hint,
        style: TextStyle(
          color: colorScheme.onSurface.withAlpha(150),
          fontSize: 12,
        ),
      ),
    );
  }
}

class _RoutingPanel extends StatelessWidget {
  const _RoutingPanel();

  @override
  Widget build(BuildContext context) {
    return _PanelFrame(
      icon: Icons.alt_route,
      title: 'Routing',
      child: BlocBuilder<RoutingBloc, RoutingState>(
        builder: (context, state) {
          if (state is RoutingLoading) {
            return const Padding(
              padding: EdgeInsets.symmetric(vertical: 8),
              child: Center(
                child: SizedBox(
                  width: 18,
                  height: 18,
                  child: CircularProgressIndicator(strokeWidth: 2),
                ),
              ),
            );
          }
          if (state is RoutingError) {
            return Text(
              'Error: ${state.message}',
              style: TextStyle(
                color: Theme.of(context).colorScheme.error,
                fontSize: 12,
              ),
            );
          }
          final loaded = state as RoutingLoaded;
          if (loaded.entries.isEmpty) {
            return Text(
              'No routing decisions yet — send a chat first.',
              style: TextStyle(
                color: Theme.of(context).colorScheme.onSurface.withAlpha(140),
                fontSize: 12,
              ),
            );
          }
          return Column(
            children: [
              for (final entry in loaded.entries) _RoutingCard(entry: entry),
            ],
          );
        },
      ),
    );
  }
}

class _RoutingCard extends StatelessWidget {
  const _RoutingCard({required this.entry});
  final pb.RoutingDecisionView entry;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    final color = switch (entry.source) {
      pb.RoutingSourceProto.ROUTING_SOURCE_REGEX => colorScheme.primary,
      pb.RoutingSourceProto.ROUTING_SOURCE_ML => colorScheme.tertiary,
      pb.RoutingSourceProto.ROUTING_SOURCE_LLM => colorScheme.secondary,
      pb.RoutingSourceProto.ROUTING_SOURCE_UNROUTED => colorScheme.error,
      _ => colorScheme.onSurface.withAlpha(120),
    };
    final sourceLabel = _sourceLabel(entry.source);
    return Padding(
      padding: const EdgeInsets.only(top: 6),
      child: Theme(
        data: Theme.of(context).copyWith(dividerColor: Colors.transparent),
        child: ExpansionTile(
          tilePadding: const EdgeInsets.symmetric(horizontal: 8),
          childrenPadding: const EdgeInsets.fromLTRB(12, 0, 12, 8),
          leading: Container(
            width: 10,
            height: 10,
            decoration: BoxDecoration(color: color, shape: BoxShape.circle),
          ),
          title: Text(
            entry.prompt,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(
              color: colorScheme.onSurface,
              fontSize: 12,
              fontWeight: FontWeight.w600,
            ),
          ),
          subtitle: Text(
            '$sourceLabel · ${entry.routingDurationMs}ms',
            style: TextStyle(
              color: colorScheme.onSurface.withAlpha(140),
              fontSize: 11,
            ),
          ),
          children: [
            if (entry.hasNeuronId())
              _KvRow(label: 'neuron', value: entry.neuronId),
            if (entry.hasConfidence())
              _KvRow(
                label: 'confidence',
                value: entry.confidence.toStringAsFixed(3),
              ),
            if (entry.hasMlConfidence())
              _KvRow(
                label: 'ml.confidence',
                value: entry.mlConfidence.toStringAsFixed(3),
              ),
            if (entry.hasMlPrediction())
              _KvRow(
                label: 'ml.prediction',
                value: entry.mlPrediction.toStringAsFixed(3),
              ),
            _KvRow(label: 'llm called', value: entry.llmCalled ? 'yes' : 'no'),
            _KvRow(label: 'correlation', value: entry.correlationId),
          ],
        ),
      ),
    );
  }

  static String _sourceLabel(pb.RoutingSourceProto source) {
    switch (source) {
      case pb.RoutingSourceProto.ROUTING_SOURCE_REGEX:
        return 'regex';
      case pb.RoutingSourceProto.ROUTING_SOURCE_ML:
        return 'ml';
      case pb.RoutingSourceProto.ROUTING_SOURCE_LLM:
        return 'llm';
      case pb.RoutingSourceProto.ROUTING_SOURCE_UNROUTED:
        return 'unrouted';
      default:
        return source.name;
    }
  }
}

class _ProposalsPanel extends StatelessWidget {
  const _ProposalsPanel();

  @override
  Widget build(BuildContext context) {
    return _PanelFrame(
      icon: Icons.auto_awesome,
      title: 'Proposals',
      child: BlocBuilder<ProposalsBloc, ProposalsState>(
        builder: (context, state) {
          if (state is ProposalsLoading) {
            return const Padding(
              padding: EdgeInsets.symmetric(vertical: 8),
              child: Center(
                child: SizedBox(
                  width: 18,
                  height: 18,
                  child: CircularProgressIndicator(strokeWidth: 2),
                ),
              ),
            );
          }
          if (state is ProposalsError) {
            return Text(
              'Error: ${state.message}',
              style: TextStyle(
                color: Theme.of(context).colorScheme.error,
                fontSize: 12,
              ),
            );
          }
          final loaded = state as ProposalsLoaded;
          if (loaded.pending.isEmpty &&
              loaded.approved.isEmpty &&
              loaded.rejected.isEmpty) {
            return Text(
              'No proposals yet — send the same unrouted prompt 3× to trigger one.',
              style: TextStyle(
                color: Theme.of(context).colorScheme.onSurface.withAlpha(140),
                fontSize: 12,
              ),
            );
          }
          return Column(
            children: [
              _ProposalSection(
                title: 'Pending',
                count: loaded.pending.length,
                initiallyExpanded: loaded.pending.isNotEmpty,
                children: [
                  for (final p in loaded.pending) _PendingTile(proposal: p),
                ],
              ),
              _ProposalSection(
                title: 'Approved',
                count: loaded.approved.length,
                children: [
                  for (final p in loaded.approved) _ApprovedTile(proposal: p),
                ],
              ),
              _ProposalSection(
                title: 'Rejected',
                count: loaded.rejected.length,
                children: [
                  for (final p in loaded.rejected) _RejectedTile(proposal: p),
                ],
              ),
            ],
          );
        },
      ),
    );
  }
}

class _ProposalSection extends StatelessWidget {
  const _ProposalSection({
    required this.title,
    required this.count,
    required this.children,
    this.initiallyExpanded = false,
  });

  final String title;
  final int count;
  final List<Widget> children;
  final bool initiallyExpanded;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.only(top: 4),
      child: Theme(
        data: Theme.of(context).copyWith(dividerColor: Colors.transparent),
        child: ExpansionTile(
          tilePadding: const EdgeInsets.symmetric(horizontal: 8),
          childrenPadding: const EdgeInsets.fromLTRB(12, 0, 12, 8),
          initiallyExpanded: initiallyExpanded && count > 0,
          title: Text(
            '$title ($count)',
            style: TextStyle(
              color: colorScheme.onSurface,
              fontSize: 12,
              fontWeight: FontWeight.w600,
            ),
          ),
          children: children.isEmpty
              ? [
                  Padding(
                    padding: const EdgeInsets.symmetric(vertical: 4),
                    child: Text(
                      '(none)',
                      style: TextStyle(
                        color: colorScheme.onSurface.withAlpha(120),
                        fontSize: 11,
                      ),
                    ),
                  ),
                ]
              : children,
        ),
      ),
    );
  }
}

class _PendingTile extends StatelessWidget {
  const _PendingTile({required this.proposal});
  final pb.ProposalView proposal;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    return Container(
      margin: const EdgeInsets.only(top: 6),
      padding: const EdgeInsets.all(10),
      decoration: BoxDecoration(
        color: colorScheme.surfaceContainerHighest.withAlpha(120),
        borderRadius: BorderRadius.circular(8),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            proposal.examplePrompt,
            style: TextStyle(
              color: colorScheme.onSurface,
              fontSize: 12,
              fontWeight: FontWeight.w600,
            ),
          ),
          const SizedBox(height: 4),
          Text(
            'cluster: ${proposal.clusterKey} · ${proposal.occurrences}×',
            style: TextStyle(
              color: colorScheme.onSurface.withAlpha(140),
              fontSize: 11,
            ),
          ),
          const SizedBox(height: 8),
          Row(
            children: [
              FilledButton(
                onPressed: () => context
                    .read<ProposalsBloc>()
                    .add(ProposalApproved(proposal.proposalId)),
                style: FilledButton.styleFrom(
                  padding: const EdgeInsets.symmetric(
                      horizontal: 14, vertical: 6),
                  textStyle: const TextStyle(
                      fontSize: 12, fontWeight: FontWeight.w600),
                ),
                child: const Text('Approve'),
              ),
              const SizedBox(width: 8),
              OutlinedButton(
                onPressed: () => context
                    .read<ProposalsBloc>()
                    .add(ProposalRejected(proposal.proposalId)),
                style: OutlinedButton.styleFrom(
                  padding: const EdgeInsets.symmetric(
                      horizontal: 14, vertical: 6),
                  textStyle: const TextStyle(
                      fontSize: 12, fontWeight: FontWeight.w600),
                ),
                child: const Text('Reject'),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _ApprovedTile extends StatelessWidget {
  const _ApprovedTile({required this.proposal});
  final pb.ProposalView proposal;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    final activated = proposal.hasActivatedNeuronId()
        ? proposal.activatedNeuronId
        : '(awaiting activation)';
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  proposal.examplePrompt,
                  style: TextStyle(
                    color: colorScheme.onSurface,
                    fontSize: 12,
                  ),
                ),
                Text(
                  '→ $activated',
                  style: TextStyle(
                    color: colorScheme.onSurface.withAlpha(140),
                    fontSize: 11,
                  ),
                ),
              ],
            ),
          ),
          TextButton(
            onPressed: () {
              context.read<InoBloc>().add(SendMessage(proposal.examplePrompt));
              Navigator.of(context).maybePop();
            },
            style: TextButton.styleFrom(
              padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
              textStyle: const TextStyle(fontSize: 11),
            ),
            child: const Text('test it now'),
          ),
        ],
      ),
    );
  }
}

class _RejectedTile extends StatelessWidget {
  const _RejectedTile({required this.proposal});
  final pb.ProposalView proposal;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            proposal.examplePrompt,
            style: TextStyle(
              color: colorScheme.onSurface.withAlpha(140),
              fontSize: 12,
              decoration: TextDecoration.lineThrough,
            ),
          ),
          Text(
            'cluster: ${proposal.clusterKey}',
            style: TextStyle(
              color: colorScheme.onSurface.withAlpha(110),
              fontSize: 11,
            ),
          ),
        ],
      ),
    );
  }
}
