import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import '../brain_theme.dart';
import 'brain_panel.dart';
import 'topology_canvas.dart';
import 'topology_selection.dart';

final class TopologyExplorer extends StatelessWidget {
  const TopologyExplorer({
    super.key,
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
      decoration: brainPanelDecoration(),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text('INSPECTOR', style: BrainType.meta),
          const SizedBox(height: 14),
          SelectionDetails(selection: selection),
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

final class SelectionDetails extends StatelessWidget {
  const SelectionDetails({super.key, required this.selection});

  final BrainTopologySelection? selection;

  @override
  Widget build(BuildContext context) {
    return switch (selection) {
      BrainPulseSelection(:final turn) => PulseDetails(turn: turn),
      BrainNeuronSelection(:final neuron) => NeuronDetails(neuron: neuron),
      BrainModuleSelection(:final module) => ModuleDetails(module: module),
      null => const Text(
        'Select a module or neuron. New chat turns open their causal pulse automatically.',
        style: BrainType.bodyMuted,
      ),
    };
  }
}

final class PulseDetails extends StatelessWidget {
  const PulseDetails({super.key, required this.turn});

  final ChatTurnEvent turn;

  @override
  Widget build(BuildContext context) {
    return Column(
      key: const Key('brain_pulse_details'),
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(turn.synapse, style: BrainType.cardTitle),
        const SizedBox(height: 10),
        BrainInspectorField(label: 'neuron', value: turn.neuronId),
        BrainInspectorField(label: 'caller', value: turn.caller),
        BrainInspectorField(label: 'correlation', value: turn.correlationId),
        BrainInspectorField(label: 'command', value: turn.commandId),
        BrainInspectorField(label: 'sequence', value: '${turn.sequence}'),
        BrainInspectorField(
          label: 'timestamp',
          value: turn.timestamp.toIso8601String(),
        ),
      ],
    );
  }
}

final class NeuronDetails extends StatelessWidget {
  const NeuronDetails({super.key, required this.neuron});

  final BrainNeuron neuron;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(neuron.grainType, style: BrainType.cardTitle),
        const SizedBox(height: 10),
        BrainInspectorField(label: 'id', value: neuron.id),
        BrainInspectorField(label: 'identity', value: neuron.identity),
        BrainInspectorField(label: 'placement', value: neuron.placement),
      ],
    );
  }
}

final class ModuleDetails extends StatelessWidget {
  const ModuleDetails({super.key, required this.module});

  final BrainModule module;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(brainModuleLabel(module), style: BrainType.cardTitle),
        const SizedBox(height: 10),
        BrainInspectorField(label: 'module id', value: module.id),
      ],
    );
  }
}

final class TopologyPanel extends StatelessWidget {
  const TopologyPanel({
    super.key,
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
        decoration: brainPanelDecoration(),
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
          decoration: brainPanelDecoration(),
          clipBehavior: Clip.antiAlias,
          child: BrainTopologyCanvas(
            topology: snapshot,
            pulse: pulse,
            onSelected: onSelected,
          ),
        );
        final explorer = TopologyExplorer(
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
