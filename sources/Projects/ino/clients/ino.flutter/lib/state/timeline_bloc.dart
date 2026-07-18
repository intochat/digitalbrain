import 'dart:async';
import 'dart:convert';

import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/grpc/ino_client.dart';

enum TimelineMode { live, scrub }

class TimelineMark {
  const TimelineMark({
    required this.x,
    required this.kind,
    required this.label,
  });

  /// Position along the timeline width: 0 = origin (left edge), 1 = now (right edge).
  final double x;

  /// One of: 'origin', 'green' (L1 birth), 'gold' (pinned), 'red' (incident), 'now'.
  final String kind;

  final String label;
}

sealed class TimelineBlocEvent {}

class TimelineStarted extends TimelineBlocEvent {}

class TimelinePaused extends TimelineBlocEvent {}

class TimelineResumed extends TimelineBlocEvent {}

class TimelineFilterChanged extends TimelineBlocEvent {
  TimelineFilterChanged({this.minDecay, this.kinds});
  final int? minDecay;
  final Set<String>? kinds;
}

class TimelineScrubbed extends TimelineBlocEvent {
  TimelineScrubbed(this.sequence);
  final int sequence;
}

class TimelineModeToggled extends TimelineBlocEvent {}

class TimelineDataLoaded extends TimelineBlocEvent {
  TimelineDataLoaded({required this.density, required this.lifeMarks});
  final List<double> density;
  final List<TimelineMark> lifeMarks;
}

class _EventReceived extends TimelineBlocEvent {
  _EventReceived(this.entry);
  final TimelineEntry entry;
}

class TimelineEntry {
  const TimelineEntry({
    required this.sequence,
    required this.kind,
    required this.source,
    required this.target,
    required this.timestamp,
    required this.decay,
    this.verb,
    this.correlationId,
    this.scenario,
    this.feature,
    this.reasoningSource,
    this.neuronId,
    this.payload = const {},
  });

  final int sequence;
  final String kind;
  final String source;
  final String target;
  final int timestamp;
  final int decay;
  final String? verb;
  final String? correlationId;
  // Slice 15 — BDD-mock LLM annotations forwarded on SynapseFired envelopes.
  // Feature+scenario identify the .feature scenario that matched the user's
  // prompt; reasoningSource is the provider (bdd-mock | azure-openai | ...).
  final String? scenario;
  final String? feature;
  final String? reasoningSource;
  // Slice 8 — neuron the Cortex matched for this fire (null until routing uses catalog).
  final String? neuronId;
  final Map<String, dynamic> payload;
}

class StateSnapshot {
  const StateSnapshot({
    required this.asOfSequence,
    required this.asOfTimestamp,
    required this.activeNeurons,
    required this.openCorrelations,
    required this.countsByKind,
  });

  final int asOfSequence;
  final int asOfTimestamp;
  final List<String> activeNeurons;
  final List<String> openCorrelations;
  final Map<String, int> countsByKind;
}

class TimelineBlocState {
  const TimelineBlocState({
    this.events = const [],
    this.isLive = false,
    this.isLoading = false,
    this.minDecay = 30,
    this.activeKinds = const {},
    this.mode = TimelineMode.live,
    this.currentSequence = 0,
    this.maxSequence = 0,
    this.snapshot,
    this.density = const [],
    this.lifeMarks = const [],
  });

  final List<TimelineEntry> events;
  final bool isLive;
  final bool isLoading;
  final int minDecay;
  final Set<String> activeKinds;
  final TimelineMode mode;
  final int currentSequence;
  final int maxSequence;
  final StateSnapshot? snapshot;
  final List<double> density;
  final List<TimelineMark> lifeMarks;

  TimelineBlocState copyWith({
    List<TimelineEntry>? events,
    bool? isLive,
    bool? isLoading,
    int? minDecay,
    Set<String>? activeKinds,
    TimelineMode? mode,
    int? currentSequence,
    int? maxSequence,
    StateSnapshot? snapshot,
    bool clearSnapshot = false,
    List<double>? density,
    List<TimelineMark>? lifeMarks,
  }) {
    return TimelineBlocState(
      events: events ?? this.events,
      isLive: isLive ?? this.isLive,
      isLoading: isLoading ?? this.isLoading,
      minDecay: minDecay ?? this.minDecay,
      activeKinds: activeKinds ?? this.activeKinds,
      mode: mode ?? this.mode,
      currentSequence: currentSequence ?? this.currentSequence,
      maxSequence: maxSequence ?? this.maxSequence,
      snapshot: clearSnapshot ? null : (snapshot ?? this.snapshot),
      density: density ?? this.density,
      lifeMarks: lifeMarks ?? this.lifeMarks,
    );
  }
}

class TimelineBloc extends Bloc<TimelineBlocEvent, TimelineBlocState> {
  TimelineBloc({required InoGrpcClient client})
    : _client = client,
      super(const TimelineBlocState()) {
    on<TimelineStarted>(_onStarted);
    on<TimelinePaused>(_onPaused);
    on<TimelineResumed>(_onResumed);
    on<TimelineFilterChanged>(_onFilterChanged);
    on<TimelineDataLoaded>(_onDataLoaded);
    on<_EventReceived>(_onEventReceived);
    on<TimelineScrubbed>(_onScrubbed);
    on<TimelineModeToggled>(_onModeToggled);
  }

  final InoGrpcClient _client;
  StreamSubscription<InoEvent>? _liveSubscription;
  final Map<int, StateSnapshot> _snapshotCache = {};

  Future<void> _onStarted(
    TimelineStarted event,
    Emitter<TimelineBlocState> emit,
  ) async {
    emit(state.copyWith(isLoading: true));
    await _liveSubscription?.cancel();

    final entries = <TimelineEntry>[];
    try {
      await for (final te in _client.getTimeline(
        limit: 50,
        minDecay: state.minDecay,
      )) {
        entries.add(_fromTimelineEvent(te));
      }
    } catch (_) {
      // partial results are fine -- server may close the stream early
    }

    final maxSeq = entries.isEmpty
        ? 0
        : entries.map((e) => e.sequence).reduce((a, b) => a > b ? a : b);

    emit(
      state.copyWith(
        events: entries,
        isLoading: false,
        isLive: true,
        maxSequence: maxSeq,
        currentSequence: maxSeq,
      ),
    );

    _startLiveTail();
  }

  void _onPaused(TimelinePaused event, Emitter<TimelineBlocState> emit) {
    _liveSubscription?.cancel();
    _liveSubscription = null;
    emit(state.copyWith(isLive: false));
  }

  void _onResumed(TimelineResumed event, Emitter<TimelineBlocState> emit) {
    emit(state.copyWith(isLive: true));
    _startLiveTail();
  }

  void _onFilterChanged(
    TimelineFilterChanged event,
    Emitter<TimelineBlocState> emit,
  ) {
    emit(
      state.copyWith(
        minDecay: event.minDecay ?? state.minDecay,
        activeKinds: event.kinds ?? state.activeKinds,
      ),
    );
    add(TimelineStarted());
  }

  void _onDataLoaded(
    TimelineDataLoaded event,
    Emitter<TimelineBlocState> emit,
  ) {
    emit(state.copyWith(
      density: event.density,
      lifeMarks: event.lifeMarks,
    ));
  }

  void _onEventReceived(_EventReceived event, Emitter<TimelineBlocState> emit) {
    if (!state.isLive) return;

    final entry = event.entry;
    if (state.activeKinds.isNotEmpty &&
        !state.activeKinds.contains(entry.kind)) {
      return;
    }

    final newMax = entry.sequence > state.maxSequence
        ? entry.sequence
        : state.maxSequence;
    emit(
      state.copyWith(
        events: [...state.events, entry],
        maxSequence: newMax,
        currentSequence: newMax,
      ),
    );
  }

  Future<void> _onModeToggled(
    TimelineModeToggled event,
    Emitter<TimelineBlocState> emit,
  ) async {
    final newMode = state.mode == TimelineMode.live
        ? TimelineMode.scrub
        : TimelineMode.live;
    emit(state.copyWith(mode: newMode));
  }

  Future<void> _onScrubbed(
    TimelineScrubbed event,
    Emitter<TimelineBlocState> emit,
  ) async {
    final seq = event.sequence;
    emit(state.copyWith(currentSequence: seq, isLoading: true));

    final cached = _snapshotCache[seq];
    if (cached != null) {
      emit(state.copyWith(snapshot: cached, isLoading: false));
      return;
    }

    try {
      final response = await _client.getStateAt(seq);
      final snap = StateSnapshot(
        asOfSequence: response.asOfSequence.toInt(),
        asOfTimestamp: response.asOfTimestamp.toInt(),
        activeNeurons: List<String>.from(response.activeNeurons),
        openCorrelations: List<String>.from(response.openCorrelations),
        countsByKind: Map<String, int>.from(response.countsByKind),
      );
      _snapshotCache[seq] = snap;
      emit(state.copyWith(snapshot: snap, isLoading: false));
    } catch (_) {
      emit(state.copyWith(isLoading: false, clearSnapshot: true));
    }
  }

  void _startLiveTail() {
    _liveSubscription?.cancel();
    _liveSubscription = _client.streamEvents().listen(
      (evt) {
        final entry = _fromInoEvent(evt);
        if (entry != null) add(_EventReceived(entry));
      },
      onError: (_) {}, // stream errors don't crash the bloc
    );
  }

  TimelineEntry _fromTimelineEvent(TimelineEvent te) {
    return TimelineEntry(
      sequence: te.sequence.toInt(),
      kind: te.kind,
      source: te.source,
      target: te.target,
      timestamp: te.timestamp.toInt(),
      decay: te.decay,
    );
  }

  TimelineEntry? _fromInoEvent(InoEvent evt) {
    try {
      final json =
          jsonDecode(String.fromCharCodes(evt.payload)) as Map<String, dynamic>;
      return TimelineEntry(
        sequence: (json['SequenceNumber'] as num?)?.toInt() ?? 0,
        kind: evt.type,
        source: evt.sourceNeuron,
        target: (json['TargetId'] as String?) ?? '',
        timestamp: evt.timestamp.toInt(),
        decay: (json['Decay'] as num?)?.toInt() ?? 100,
        verb: json['SynapseVerb'] as String?,
        correlationId: json['CorrelationId'] as String?,
        scenario: json['Scenario'] as String?,
        feature: json['Feature'] as String?,
        reasoningSource: json['ReasoningSource'] as String?,
        neuronId: json['Neuron'] as String?,
        payload: (json['Payload'] as Map<String, dynamic>?) ?? const {},
      );
    } catch (_) {
      return null;
    }
  }

  @override
  Future<void> close() {
    _liveSubscription?.cancel();
    return super.close();
  }
}
