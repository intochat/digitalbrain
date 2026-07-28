import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import 'brain_theme.dart';
import 'brain_topology_canvas.dart';

final class BrainScreen extends StatefulWidget {
  const BrainScreen({
    super.key,
    required this.chatName,
    required this.turns,
    this.topology,
    this.statusMessage,
  });

  final String chatName;
  final List<ChatTurnEvent> turns;
  final BrainTopologySnapshot? topology;
  final String? statusMessage;

  @override
  State<BrainScreen> createState() => _BrainScreenState();
}

final class _BrainScreenState extends State<BrainScreen> {
  static const _generalAssistant = 'assistant.general';
  static const _accountEnrichment =
      'account-enrichment.gmail-salesforce-description';

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
      final selection => selection,
    };
  }

  ({bool generalAssistant, bool accountEnrichment}) _capabilities() {
    final capabilities = widget.topology?.capabilities
        .map((capability) => capability.id)
        .toSet();
    return (
      generalAssistant: capabilities?.contains(_generalAssistant) == true,
      accountEnrichment: capabilities?.contains(_accountEnrichment) == true,
    );
  }

  List<Widget> _capabilityPanels() {
    final capabilities = _capabilities();
    if (!capabilities.generalAssistant && !capabilities.accountEnrichment) {
      return const [];
    }

    return [
      const SizedBox(height: 30),
      const _SectionLabel('CAPABILITIES'),
      const SizedBox(height: 12),
      if (capabilities.generalAssistant)
        const _CapabilityCard(
          icon: Icons.chat_bubble_outline_rounded,
          title: 'General assistant',
          body:
              'Conversation, explanation, drafting, and reasoning in the current chat.',
        ),
      if (capabilities.generalAssistant && capabilities.accountEnrichment)
        const SizedBox(height: 12),
      if (capabilities.accountEnrichment)
        const _CapabilityCard(
          icon: Icons.compare_arrows_rounded,
          title: 'Gmail message → Salesforce Account description',
          body:
              'Creates a reviewable enrichment proposal from an exact Gmail message ID and a Salesforce Account ID.',
          badge: 'Approval required',
        ),
      if (capabilities.accountEnrichment) ...[
        const SizedBox(height: 30),
        const _SectionLabel('BOUNDARIES'),
        const SizedBox(height: 12),
        const _BoundaryCard(),
      ],
    ];
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
                  _MetricCard(
                    label: 'Runtime',
                    value: connected ? 'Connected' : 'Offline',
                    accent: connected
                        ? BrainPalette.success
                        : BrainPalette.signal,
                  ),
                  _MetricCard(
                    label: 'Modules',
                    value: '${widget.topology?.modules.length ?? 0}',
                  ),
                  _MetricCard(
                    label: 'Active neurons',
                    value: '${widget.topology?.neurons.length ?? 0}',
                  ),
                  _MetricCard(label: 'Last sequence', value: lastSequence),
                ],
              ),
              const SizedBox(height: 24),
              _TopologyPanel(
                topology: widget.topology,
                pulse: pulse,
                selection: _selection,
                onSelected: (selection) =>
                    setState(() => _selection = selection),
              ),
              ..._capabilityPanels(),
              if (!connected) ...[
                const SizedBox(height: 20),
                _ConnectionNotice(message: widget.statusMessage!),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

final class _TopologyPanel extends StatelessWidget {
  const _TopologyPanel({
    required this.topology,
    required this.pulse,
    required this.selection,
    required this.onSelected,
  });

  final BrainTopologySnapshot? topology;
  final ChatTurnEvent? pulse;
  final BrainTopologySelection? selection;
  final ValueChanged<BrainTopologySelection> onSelected;

  @override
  Widget build(BuildContext context) {
    final snapshot = topology;
    if (snapshot == null) {
      return Container(
        height: 260,
        decoration: _panelDecoration(),
        child: const Center(
          child: Text('Waiting for live topology…', style: BrainType.bodyMuted),
        ),
      );
    }

    return LayoutBuilder(
      builder: (context, constraints) {
        final wide = constraints.maxWidth >= 860;
        final canvas = Container(
          height: 430,
          decoration: _panelDecoration(),
          clipBehavior: Clip.antiAlias,
          child: BrainTopologyCanvas(
            topology: snapshot,
            pulse: pulse,
            onSelected: onSelected,
          ),
        );
        final explorer = _TopologyExplorer(
          topology: snapshot,
          selection: selection,
          onSelected: onSelected,
        );

        if (wide) {
          return Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(flex: 3, child: canvas),
              const SizedBox(width: 14),
              SizedBox(width: 320, child: explorer),
            ],
          );
        }

        return Column(children: [canvas, const SizedBox(height: 14), explorer]);
      },
    );
  }
}

final class _TopologyExplorer extends StatelessWidget {
  const _TopologyExplorer({
    required this.topology,
    required this.selection,
    required this.onSelected,
  });

  final BrainTopologySnapshot topology;
  final BrainTopologySelection? selection;
  final ValueChanged<BrainTopologySelection> onSelected;

  @override
  Widget build(BuildContext context) {
    return Container(
      key: const Key('brain_inspector'),
      constraints: const BoxConstraints(minHeight: 430),
      padding: const EdgeInsets.all(18),
      decoration: _panelDecoration(),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text('INSPECTOR', style: BrainType.meta),
          const SizedBox(height: 14),
          _SelectionDetails(selection: selection),
          const SizedBox(height: 20),
          const Text('MODULES', style: BrainType.meta),
          const SizedBox(height: 8),
          Wrap(
            spacing: 7,
            runSpacing: 7,
            children: [
              for (var index = 0; index < topology.modules.length; index++)
                ActionChip(
                  key: Key('topology_module_$index'),
                  label: Text(brainModuleLabel(topology.modules[index])),
                  onPressed: () =>
                      onSelected(BrainModuleSelection(topology.modules[index])),
                ),
            ],
          ),
          const SizedBox(height: 18),
          const Text('ACTIVE NEURONS', style: BrainType.meta),
          const SizedBox(height: 8),
          for (var index = 0; index < topology.neurons.length; index++)
            Padding(
              padding: const EdgeInsets.only(bottom: 6),
              child: TextButton(
                key: Key('topology_neuron_$index'),
                onPressed: () =>
                    onSelected(BrainNeuronSelection(topology.neurons[index])),
                child: Align(
                  alignment: Alignment.centerLeft,
                  child: Text(
                    topology.neurons[index].id,
                    overflow: TextOverflow.ellipsis,
                    style: BrainType.meta,
                  ),
                ),
              ),
            ),
        ],
      ),
    );
  }
}

final class _SelectionDetails extends StatelessWidget {
  const _SelectionDetails({required this.selection});

  final BrainTopologySelection? selection;

  @override
  Widget build(BuildContext context) {
    return switch (selection) {
      BrainPulseSelection(:final turn) => _PulseDetails(turn: turn),
      BrainNeuronSelection(:final neuron) => _NeuronDetails(neuron: neuron),
      BrainModuleSelection(:final module) => _ModuleDetails(module: module),
      null => const Text(
        'Select a module or neuron. New chat turns open their causal pulse automatically.',
        style: BrainType.bodyMuted,
      ),
    };
  }
}

final class _PulseDetails extends StatelessWidget {
  const _PulseDetails({required this.turn});

  final ChatTurnEvent turn;

  @override
  Widget build(BuildContext context) {
    return Column(
      key: const Key('brain_pulse_details'),
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(turn.synapse, style: BrainType.cardTitle),
        const SizedBox(height: 10),
        _InspectorField(label: 'neuron', value: turn.neuronId),
        _InspectorField(label: 'caller', value: turn.caller),
        _InspectorField(label: 'correlation', value: turn.correlationId),
        _InspectorField(label: 'command', value: turn.commandId),
        _InspectorField(label: 'sequence', value: '${turn.sequence}'),
        _InspectorField(
          label: 'timestamp',
          value: turn.timestamp.toIso8601String(),
        ),
      ],
    );
  }
}

final class _NeuronDetails extends StatelessWidget {
  const _NeuronDetails({required this.neuron});

  final BrainNeuron neuron;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(neuron.grainType, style: BrainType.cardTitle),
        const SizedBox(height: 10),
        _InspectorField(label: 'id', value: neuron.id),
        _InspectorField(label: 'identity', value: neuron.identity),
        _InspectorField(label: 'placement', value: neuron.placement),
      ],
    );
  }
}

final class _ModuleDetails extends StatelessWidget {
  const _ModuleDetails({required this.module});

  final BrainModule module;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(brainModuleLabel(module), style: BrainType.cardTitle),
        const SizedBox(height: 10),
        _InspectorField(label: 'module id', value: module.id),
      ],
    );
  }
}

final class _InspectorField extends StatelessWidget {
  const _InspectorField({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 7),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: BrainType.meta),
          const SizedBox(height: 2),
          SelectableText(value, style: BrainType.metaStrong),
        ],
      ),
    );
  }
}

final class _MetricCard extends StatelessWidget {
  const _MetricCard({
    required this.label,
    required this.value,
    this.accent = BrainPalette.textPrimary,
  });

  final String label;
  final String value;
  final Color accent;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 190,
      padding: const EdgeInsets.all(16),
      decoration: _panelDecoration(),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: BrainType.meta),
          const SizedBox(height: 9),
          Text(
            value,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: BrainType.metric.copyWith(color: accent),
          ),
        ],
      ),
    );
  }
}

final class _SectionLabel extends StatelessWidget {
  const _SectionLabel(this.text);

  final String text;

  @override
  Widget build(BuildContext context) => Text(text, style: BrainType.meta);
}

final class _CapabilityCard extends StatelessWidget {
  const _CapabilityCard({
    required this.icon,
    required this.title,
    required this.body,
    this.badge,
  });

  final IconData icon;
  final String title;
  final String body;
  final String? badge;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(20),
      decoration: _panelDecoration(),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Container(
            width: 38,
            height: 38,
            decoration: BoxDecoration(
              color: BrainPalette.signal.withValues(alpha: 0.1),
              borderRadius: BorderRadius.circular(11),
            ),
            child: Icon(icon, color: BrainPalette.signal, size: 19),
          ),
          const SizedBox(width: 16),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(child: Text(title, style: BrainType.cardTitle)),
                    if (badge != null) _Badge(label: badge!),
                  ],
                ),
                const SizedBox(height: 7),
                Text(body, style: BrainType.bodyMuted),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

final class _Badge extends StatelessWidget {
  const _Badge({required this.label});

  final String label;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 5),
      decoration: BoxDecoration(
        color: BrainPalette.owner.withValues(alpha: 0.1),
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: BrainPalette.owner.withValues(alpha: 0.3)),
      ),
      child: Text(
        label,
        style: BrainType.metaStrong.copyWith(color: BrainPalette.owner),
      ),
    );
  }
}

final class _BoundaryCard extends StatelessWidget {
  const _BoundaryCard();

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(20),
      decoration: _panelDecoration(color: BrainPalette.surfaceSunken),
      child: const Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _BoundaryLine('No Gmail search, listing, or sending'),
          SizedBox(height: 11),
          _BoundaryLine('No direct Salesforce writes'),
          SizedBox(height: 11),
          _BoundaryLine('No account, contact, or lead creation'),
        ],
      ),
    );
  }
}

final class _BoundaryLine extends StatelessWidget {
  const _BoundaryLine(this.text);

  final String text;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        const Icon(
          Icons.remove_circle_outline_rounded,
          color: BrainPalette.textMuted,
          size: 16,
        ),
        const SizedBox(width: 10),
        Expanded(child: Text(text, style: BrainType.bodyMuted)),
      ],
    );
  }
}

final class _ConnectionNotice extends StatelessWidget {
  const _ConnectionNotice({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: BrainPalette.signal.withValues(alpha: 0.08),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: BrainPalette.signal.withValues(alpha: 0.25)),
      ),
      child: Text(message, style: BrainType.bodyMuted),
    );
  }
}

BoxDecoration _panelDecoration({Color color = BrainPalette.surfaceRaised}) =>
    BoxDecoration(
      color: color,
      borderRadius: BorderRadius.circular(14),
      border: Border.all(color: BrainPalette.line),
    );
