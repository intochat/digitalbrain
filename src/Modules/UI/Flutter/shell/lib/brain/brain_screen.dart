import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import '../brain_theme.dart';
import 'brain_inspector.dart';
import 'brain_panel.dart';
import 'topology_selection.dart';

final class BrainScreen extends StatefulWidget {
  const BrainScreen({
    super.key,
    required this.chatName,
    required this.turns,
    this.topology,
    this.graphChange,
    this.statusMessage,
  });

  final String chatName;
  final List<ChatTurnEvent> turns;
  final BrainTopologySnapshot? topology;
  final GraphChangeEvent? graphChange;
  final String? statusMessage;

  @override
  State<BrainScreen> createState() => _BrainScreenState();
}

final class _BrainScreenState extends State<BrainScreen> {
  BrainTopologySelection? _selection;

  @override
  void initState() {
    super.initState();
    if (widget.turns.isNotEmpty) {
      _selection = BrainPulseSelection(widget.turns.last);
    }
  }

  @override
  void didUpdateWidget(covariant BrainScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (widget.chatName != oldWidget.chatName) {
      _selection = null;
      return;
    }

    if (widget.turns.isNotEmpty &&
        (oldWidget.turns.isEmpty ||
            widget.turns.last.sequence != oldWidget.turns.last.sequence ||
            widget.turns.last.correlationId !=
                oldWidget.turns.last.correlationId)) {
      _selection = BrainPulseSelection(widget.turns.last);
      return;
    }

    final topology = widget.topology;
    _selection = switch (_selection) {
      BrainPulseSelection(:final turn)
          when !widget.turns.any(
            (candidate) =>
                candidate.sequence == turn.sequence &&
                candidate.correlationId == turn.correlationId,
          ) =>
        null,
      BrainNeuronSelection(:final neuron)
          when topology == null ||
              !topology.neurons.any((candidate) => candidate.id == neuron.id) =>
        null,
      BrainModuleSelection(:final module)
          when topology == null ||
              !topology.modules.any((candidate) => candidate.id == module.id) =>
        null,
      BrainConnectionSelection(:final connection)
          when topology == null ||
              !topology.connections.any(
                (candidate) =>
                    candidate.connectionId == connection.connectionId,
              ) =>
        null,
      final selection => selection,
    };
  }

  @override
  Widget build(BuildContext context) {
    final connected =
        widget.statusMessage == null || widget.statusMessage!.isEmpty;
    final lastSequence = widget.turns.isEmpty
        ? '—'
        : '${widget.turns.last.sequence}';
    final pulse = widget.turns.lastOrNull;

    return ColoredBox(
      key: const Key('brain_screen'),
      color: BrainPalette.surface,
      child: Align(
        alignment: Alignment.topCenter,
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 1120),
          child: ListView(
            padding: const EdgeInsets.symmetric(horizontal: 32, vertical: 32),
            children: [
              const Text('Brain', style: BrainType.heading),
              const SizedBox(height: 8),
              const Text(
                'Live modules, owner-scoped neurons, and durable causal pulses.',
                style: BrainType.bodyMuted,
              ),
              const SizedBox(height: 26),
              Wrap(
                spacing: 12,
                runSpacing: 12,
                children: [
                  BrainMetricCard(
                    label: 'Runtime',
                    value: connected ? 'Connected' : 'Offline',
                    accent: connected
                        ? BrainPalette.success
                        : BrainPalette.signal,
                  ),
                  BrainMetricCard(
                    label: 'Modules',
                    value: '${widget.topology?.modules.length ?? 0}',
                  ),
                  BrainMetricCard(
                    label: 'Active neurons',
                    value: '${widget.topology?.neurons.length ?? 0}',
                  ),
                  BrainMetricCard(label: 'Last sequence', value: lastSequence),
                ],
              ),
              const SizedBox(height: 24),
              TopologyPanel(
                topology: widget.topology,
                pulse: pulse,
                graphChange: widget.graphChange,
                selection: _selection,
                onSelected: (selection) =>
                    setState(() => _selection = selection),
              ),
              if (!connected) ...[
                const SizedBox(height: 20),
                BrainConnectionNotice(message: widget.statusMessage!),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
