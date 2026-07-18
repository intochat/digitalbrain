import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/screens/brain/brain_roles.dart';
import 'package:ino_flutter/screens/brain/brain_topology.dart';
import 'package:ino_flutter/state/brain_inspector_bloc.dart';

class BrainInspectorDrawer extends StatelessWidget {
  const BrainInspectorDrawer({super.key});

  @override
  Widget build(BuildContext context) {
    return BlocBuilder<BrainInspectorBloc, BrainInspectorState>(
      buildWhen: (a, b) =>
          a.selected != b.selected ||
          a.recentByNodeId != b.recentByNodeId ||
          a.pausedPulse != b.pausedPulse,
      builder: (context, state) {
        final sel = state.selected;
        if (sel == null) return const SizedBox.shrink();
        return Align(
          alignment: Alignment.topRight,
          child: Container(
            key: const Key('brain-inspector-drawer-panel'),
            width: 360,
            margin: const EdgeInsets.only(top: 60, right: 12, bottom: 80),
            padding: const EdgeInsets.all(16),
            decoration: BoxDecoration(
              color: Colors.black.withAlpha(170),
              borderRadius: BorderRadius.circular(14),
              border: Border.all(color: Colors.white.withAlpha(30)),
            ),
            child: switch (sel) {
              NeuronSelection s => _NeuronView(nodeId: s.nodeId, recent: state.recentByNodeId),
              SynapseTypeSelection s => _SynapseTypeView(nodeId: s.nodeId, recent: state.recentByNodeId),
              PulseSelection s => _PulseView(pulse: s.pulse),
            },
          ),
        );
      },
    );
  }
}

class _DrawerHeader extends StatelessWidget {
  const _DrawerHeader({required this.title, required this.dotColor});
  final String title;
  final Color dotColor;
  @override
  Widget build(BuildContext context) {
    return Row(children: [
      Container(width: 12, height: 12, decoration: BoxDecoration(color: dotColor, shape: BoxShape.circle)),
      const SizedBox(width: 10),
      Expanded(
        child: Text(
          title,
          style: const TextStyle(color: Colors.white, fontSize: 16, fontWeight: FontWeight.w600),
        ),
      ),
      IconButton(
        tooltip: 'Close',
        icon: const Icon(Icons.close, color: Colors.white70, size: 18),
        onPressed: () => context.read<BrainInspectorBloc>().add(Deselect()),
      ),
    ]);
  }
}

class _NeuronView extends StatelessWidget {
  const _NeuronView({required this.nodeId, required this.recent});
  final String nodeId;
  final Map<String, List<FireEvent>> recent;

  @override
  Widget build(BuildContext context) {
    final node = BrainTopology.load().nodes.firstWhere(
          (n) => n.id == nodeId,
          orElse: () => throw StateError('unknown node $nodeId'),
        );
    final hasRole = roleByNodeId.containsKey(nodeId);
    final role = roleByNodeId[nodeId] ?? 'no role declared';
    final traffic = (recent[nodeId] ?? const <FireEvent>[]).take(10).toList();

    return Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        _DrawerHeader(title: node.label, dotColor: Color(0xFF000000 | domainColor(node.domain))),
        const SizedBox(height: 4),
        Text(node.domain,
            style: TextStyle(color: Colors.white.withAlpha(140), fontSize: 11, letterSpacing: 0.5)),
        const SizedBox(height: 12),
        Text(role,
            style: TextStyle(
              color: hasRole ? Colors.white70 : Colors.white24,
              fontSize: 13,
            )),
        const SizedBox(height: 16),
        const Text('Recent traffic',
            style: TextStyle(color: Colors.white60, fontSize: 11, letterSpacing: 0.6)),
        const SizedBox(height: 6),
        if (traffic.isEmpty)
          const Text('no traffic yet — interact to populate',
              style: TextStyle(color: Colors.white24, fontSize: 12))
        else
          ...traffic.map((e) => _TrafficRow(event: e, anchorId: nodeId)),
      ],
    );
  }
}

class _TrafficRow extends StatelessWidget {
  const _TrafficRow({required this.event, required this.anchorId});
  final FireEvent event;
  final String anchorId;
  @override
  Widget build(BuildContext context) {
    final fired = event.fromId == anchorId;
    final counterpartId = fired ? event.toId : event.fromId;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 3),
      child: Row(children: [
        Icon(fired ? Icons.arrow_upward : Icons.arrow_downward, color: Colors.white54, size: 14),
        const SizedBox(width: 8),
        Expanded(
          child: Text('${event.synapseType} · $counterpartId',
              style: const TextStyle(color: Colors.white70, fontSize: 12), overflow: TextOverflow.ellipsis),
        ),
      ]),
    );
  }
}

class _SynapseTypeView extends StatelessWidget {
  const _SynapseTypeView({required this.nodeId, required this.recent});
  final String nodeId;
  final Map<String, List<FireEvent>> recent;

  @override
  Widget build(BuildContext context) {
    final topo = BrainTopology.load();
    final node = topo.nodes.firstWhere((n) => n.id == nodeId);
    final consumers = topo.edges
        .where((e) => e.from == nodeId && e.kind == EdgeKind.handler)
        .map((e) => e.to)
        .toList();
    final fires = (recent[nodeId] ?? const <FireEvent>[]).take(10).toList();

    return Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        _DrawerHeader(title: node.label, dotColor: const Color(0xFF5EEAD4)),
        const SizedBox(height: 14),
        const Text('Consumers',
            style: TextStyle(color: Colors.white60, fontSize: 11, letterSpacing: 0.6)),
        const SizedBox(height: 6),
        Wrap(spacing: 6, runSpacing: 6, children: [
          for (final c in consumers) _Chip(text: c),
        ]),
        const SizedBox(height: 16),
        const Text('Recent fires',
            style: TextStyle(color: Colors.white60, fontSize: 11, letterSpacing: 0.6)),
        const SizedBox(height: 6),
        if (fires.isEmpty)
          const Text('no traffic yet', style: TextStyle(color: Colors.white24, fontSize: 12))
        else
          ...fires.map((e) => Padding(
                padding: const EdgeInsets.symmetric(vertical: 3),
                child: Text('${e.fromId} → ${e.toId}',
                    style: const TextStyle(color: Colors.white70, fontSize: 12)),
              )),
      ],
    );
  }
}

class _PulseView extends StatelessWidget {
  const _PulseView({required this.pulse});
  final FireEvent pulse;
  @override
  Widget build(BuildContext context) {
    return Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        _DrawerHeader(title: 'Pulse · ${pulse.synapseType}', dotColor: Colors.white),
        const SizedBox(height: 12),
        Text('${pulse.fromId} → ${pulse.toId}',
            style: const TextStyle(color: Colors.white70, fontSize: 13)),
        const SizedBox(height: 8),
        SelectableText(pulse.traceParent,
            style: const TextStyle(color: Colors.white54, fontFamily: 'monospace', fontSize: 11)),
        const SizedBox(height: 12),
        const Text('Payload',
            style: TextStyle(color: Colors.white60, fontSize: 11, letterSpacing: 0.6)),
        const SizedBox(height: 6),
        Container(
          padding: const EdgeInsets.all(8),
          decoration: BoxDecoration(
            color: Colors.white.withAlpha(8),
            border: Border.all(color: Colors.white.withAlpha(20)),
            borderRadius: BorderRadius.circular(6),
          ),
          child: SelectableText(
            pulse.payloadJson.isEmpty ? '{}' : pulse.payloadJson,
            style: const TextStyle(color: Colors.white70, fontFamily: 'monospace', fontSize: 11),
          ),
        ),
      ],
    );
  }
}

class _Chip extends StatelessWidget {
  const _Chip({required this.text});
  final String text;
  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
        decoration: BoxDecoration(
          color: Colors.white.withAlpha(15),
          borderRadius: BorderRadius.circular(6),
          border: Border.all(color: Colors.white.withAlpha(25)),
        ),
        child: Text(text, style: const TextStyle(color: Colors.white70, fontSize: 11)),
      );
}
