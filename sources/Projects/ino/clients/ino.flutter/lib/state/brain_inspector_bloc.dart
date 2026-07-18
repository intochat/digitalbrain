import 'package:flutter_bloc/flutter_bloc.dart';

/// Snapshot of a paused shell-synapse comet for the floating tooltip.
class PausedSynapseInfo {
  const PausedSynapseInfo({
    required this.from,
    required this.to,
    required this.payload,
    required this.gold,
    required this.screenX,
    required this.screenY,
  });

  final String from;
  final String to;
  final Map<String, dynamic> payload;
  final bool gold;
  final double screenX;
  final double screenY;
}

class FireEvent {
  const FireEvent({
    required this.id,
    required this.traceParent,
    required this.synapseType,
    required this.fromId,
    required this.toId,
    required this.payloadJson,
    required this.timestampUnixMs,
  });

  final String id;
  final String traceParent;
  final String synapseType;
  final String fromId;
  final String toId;
  final String payloadJson;
  final int timestampUnixMs;

  factory FireEvent.fromBrainPulse({
    required String fromGrain,
    required String toGrain,
    required String methodName,
    required String payloadJson,
    required String traceParent,
    required int timestampUnixMs,
  }) {
    // Empty fromGrain ("system" calls) folds to toGrain so the buffer still keys
    // on something useful for the drawer.
    return FireEvent(
      id: '$traceParent#$timestampUnixMs',
      traceParent: traceParent,
      synapseType: methodName,
      fromId: fromGrain.isEmpty ? toGrain : fromGrain,
      toId: toGrain,
      payloadJson: payloadJson,
      timestampUnixMs: timestampUnixMs,
    );
  }
}

sealed class Selection {}
class NeuronSelection extends Selection {
  NeuronSelection(this.nodeId);
  final String nodeId;
}
class SynapseTypeSelection extends Selection {
  SynapseTypeSelection(this.nodeId);
  final String nodeId;
}
class PulseSelection extends Selection {
  PulseSelection(this.pulse);
  final FireEvent pulse;
}

class BrainInspectorState {
  const BrainInspectorState({
    this.selected,
    this.pausedPulse,
    this.pausedSynapse,
    this.recentByNodeId = const {},
  });

  final Selection? selected;
  final FireEvent? pausedPulse;
  final PausedSynapseInfo? pausedSynapse;
  final Map<String, List<FireEvent>> recentByNodeId;

  BrainInspectorState copyWith({
    Selection? selected,
    bool clearSelected = false,
    FireEvent? pausedPulse,
    bool clearPaused = false,
    PausedSynapseInfo? pausedSynapse,
    bool clearPausedSynapse = false,
    Map<String, List<FireEvent>>? recentByNodeId,
  }) =>
      BrainInspectorState(
        selected: clearSelected ? null : (selected ?? this.selected),
        pausedPulse: clearPaused ? null : (pausedPulse ?? this.pausedPulse),
        pausedSynapse: clearPausedSynapse ? null : (pausedSynapse ?? this.pausedSynapse),
        recentByNodeId: recentByNodeId ?? this.recentByNodeId,
      );
}

sealed class BrainInspectorEvent {}
class IngestFire extends BrainInspectorEvent {
  IngestFire(this.fire);
  final FireEvent fire;
}
class SelectNeuron extends BrainInspectorEvent {
  SelectNeuron({required this.nodeId});
  final String nodeId;
}
class SelectSynapseType extends BrainInspectorEvent {
  SelectSynapseType({required this.nodeId});
  final String nodeId;
}
class PausePulse extends BrainInspectorEvent {
  PausePulse({required this.pulse});
  final FireEvent pulse;
}
class PauseShellSynapse extends BrainInspectorEvent {
  PauseShellSynapse({required this.info});
  final PausedSynapseInfo info;
}
class ResumeShellSynapse extends BrainInspectorEvent {}
class Deselect extends BrainInspectorEvent {}

const int _ringBufferCap = 25;

class BrainInspectorBloc extends Bloc<BrainInspectorEvent, BrainInspectorState> {
  BrainInspectorBloc() : super(const BrainInspectorState()) {
    on<IngestFire>((e, emit) {
      final next = Map<String, List<FireEvent>>.from(state.recentByNodeId);
      _push(next, e.fire.fromId, e.fire);
      _push(next, e.fire.toId, e.fire);
      emit(state.copyWith(recentByNodeId: next));
    });
    on<SelectNeuron>((e, emit) =>
        emit(state.copyWith(selected: NeuronSelection(e.nodeId))));
    on<SelectSynapseType>((e, emit) =>
        emit(state.copyWith(selected: SynapseTypeSelection(e.nodeId))));
    on<PausePulse>((e, emit) => emit(state.copyWith(
        selected: PulseSelection(e.pulse), pausedPulse: e.pulse)));
    on<PauseShellSynapse>((e, emit) =>
        emit(state.copyWith(pausedSynapse: e.info)));
    on<ResumeShellSynapse>((e, emit) =>
        emit(state.copyWith(clearPausedSynapse: true)));
    on<Deselect>((e, emit) => emit(state.copyWith(
        clearSelected: true, clearPaused: true, clearPausedSynapse: true)));
  }

  static void _push(Map<String, List<FireEvent>> map, String key, FireEvent fire) {
    final list = List<FireEvent>.from(map[key] ?? const [])..insert(0, fire);
    if (list.length > _ringBufferCap) list.removeRange(_ringBufferCap, list.length);
    map[key] = list;
  }
}
