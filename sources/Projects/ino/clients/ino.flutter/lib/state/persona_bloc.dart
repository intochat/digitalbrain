import 'dart:async';

import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/grpc/ino_client.dart';
import 'package:ino_flutter/grpc/generated/ino.pb.dart' as pb;
import 'package:ino_flutter/persona/persona_state.dart';

sealed class PersonaBlocEvent {}

class PersonaStarted extends PersonaBlocEvent {}

class PersonaEmotionChanged extends PersonaBlocEvent {
  PersonaEmotionChanged(this.emotion);
  final PersonaEmotion emotion;
}

class PersonaUpdated extends PersonaBlocEvent {
  PersonaUpdated(this.state);
  final PersonaStateModel state;
}

class PersonaTimelineEvent extends PersonaBlocEvent {
  PersonaTimelineEvent(this.source, this.kind, {this.verb});
  final String source;
  final String kind;
  final String? verb;
}

class PersonaSwitchRequested extends PersonaBlocEvent {
  PersonaSwitchRequested(this.personaName);
  final String personaName;
}

class PersonaActionCleared extends PersonaBlocEvent {}

class PersonaBloc extends Bloc<PersonaBlocEvent, PersonaStateModel> {
  PersonaBloc({required InoGrpcClient client})
      : _client = client,
        super(const PersonaStateModel(emotion: PersonaEmotion.sleeping)) {
    on<PersonaStarted>(_onStarted);
    on<PersonaEmotionChanged>(_onEmotionChanged);
    on<PersonaUpdated>(_onUpdated);
    on<PersonaTimelineEvent>(_onTimelineEvent);
    on<PersonaActionCleared>(_onActionCleared);
    on<PersonaSwitchRequested>(_onSwitchRequested);
  }

  final InoGrpcClient _client;
  StreamSubscription<pb.PersonaState>? _streamSubscription;
  Timer? _actionClearTimer;

  final Set<String> _activeNeurons = {};
  int _synapseCount = 0;
  DateTime _windowStart = DateTime.now();

  Future<void> _onStarted(
    PersonaStarted event,
    Emitter<PersonaStateModel> emit,
  ) async {
    emit(state.copyWith(emotion: PersonaEmotion.waking));

    await Future<void>.delayed(const Duration(milliseconds: 500));
    emit(state.copyWith(emotion: PersonaEmotion.idle));

    _streamSubscription?.cancel();
    _streamSubscription = _client.streamPersonaState().listen(
      (pbState) {
        final emotion = _parseEmotion(pbState.emotion);
        add(PersonaUpdated(PersonaStateModel(
          emotion: emotion,
          energy: pbState.energy,
          confidence: pbState.confidence,
          domainAffinity: Map<String, double>.from(pbState.domainAffinity),
        )));
      },
      onError: (_) {},
    );
  }

  void _onEmotionChanged(
    PersonaEmotionChanged event,
    Emitter<PersonaStateModel> emit,
  ) {
    emit(state.copyWith(emotion: event.emotion));
  }

  void _onUpdated(
    PersonaUpdated event,
    Emitter<PersonaStateModel> emit,
  ) {
    // preserve timeline-derived fields that the gRPC stream doesn't carry
    emit(event.state.copyWith(
      neuronCount: state.neuronCount,
      synapseRate: state.synapseRate,
      signalPulse: state.signalPulse,
      activeSkillCount: state.activeSkillCount,
      currentAction: state.currentAction,
    ));
  }

  void _onTimelineEvent(
    PersonaTimelineEvent event,
    Emitter<PersonaStateModel> emit,
  ) {
    _activeNeurons.add(event.source);
    _synapseCount++;
    final elapsed = DateTime.now().difference(_windowStart).inSeconds;
    final rate = elapsed > 0 ? _synapseCount / elapsed : 0.0;

    // derive a human-readable action from the event kind/verb
    final action = _deriveAction(event.kind, event.verb, event.source);

    emit(state.copyWith(
      neuronCount: _activeNeurons.length,
      synapseRate: rate,
      energy: (rate / 5.0).clamp(0.0, 1.0),
      signalPulse: 1.0, // spike to max, widget handles decay animation
      activeSkillCount: _activeNeurons.length,
      currentAction: action,
    ));

    // auto-clear the action label after 3 seconds of no new events
    _actionClearTimer?.cancel();
    _actionClearTimer = Timer(const Duration(seconds: 3), () {
      add(PersonaActionCleared());
    });

    // reset the tracking window every 30 seconds
    if (elapsed > 30) {
      _activeNeurons.clear();
      _synapseCount = 0;
      _windowStart = DateTime.now();
    }
  }

  void _onActionCleared(
    PersonaActionCleared event,
    Emitter<PersonaStateModel> emit,
  ) {
    emit(state.copyWith(
      signalPulse: 0.0,
      clearCurrentAction: true,
    ));
  }

  Future<void> _onSwitchRequested(
    PersonaSwitchRequested event,
    Emitter<PersonaStateModel> emit,
  ) async {
    emit(state.copyWith(
      emotion: PersonaEmotion.thinking,
      currentAction: 'Becoming ${event.personaName}...',
    ));

    try {
      final resp = await _client.switchPersona(event.personaName);
      emit(state.copyWith(
        personaName: resp.personaName,
        personaSlug: resp.personaSlug,
        riveAssetUrl: resp.riveAssetUrl.isEmpty ? null : resp.riveAssetUrl,
        traits: Map<String, String>.from(resp.traits),
        emotion: PersonaEmotion.celebrating,
        clearCurrentAction: true,
        signalPulse: 1.0,
      ));
    } catch (_) {
      emit(state.copyWith(
        emotion: PersonaEmotion.confused,
        currentAction: 'Failed to switch persona',
      ));
    }
  }

  String? _deriveAction(String kind, String? verb, String source) {
    if (verb != null && verb.isNotEmpty) return '$source: $verb';
    return switch (kind) {
      'ToolCall' => '$source: calling tool...',
      'LlmRequest' => '$source: thinking...',
      'SynapseFire' => '$source: firing synapse...',
      'AgentResponse' => '$source: responding...',
      _ => null,
    };
  }

  PersonaEmotion _parseEmotion(String value) {
    return PersonaEmotion.values.firstWhere(
      (e) => e.name == value,
      orElse: () => PersonaEmotion.idle,
    );
  }

  @override
  Future<void> close() {
    _streamSubscription?.cancel();
    _actionClearTimer?.cancel();
    return super.close();
  }
}
