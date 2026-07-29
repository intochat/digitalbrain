import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';

sealed class BrainTopologySelection {
  const BrainTopologySelection();
}

final class BrainModuleSelection extends BrainTopologySelection {
  const BrainModuleSelection(this.module);

  final BrainModule module;
}

final class BrainNeuronSelection extends BrainTopologySelection {
  const BrainNeuronSelection(this.neuron);

  final BrainNeuron neuron;
}

final class BrainPulseSelection extends BrainTopologySelection {
  const BrainPulseSelection(this.turn);

  final ChatTurnEvent turn;
}

String brainModuleLabel(BrainModule module) {
  final type = module.id.split('.').last;
  return type.endsWith('Module')
      ? type.substring(0, type.length - 'Module'.length)
      : type;
}
