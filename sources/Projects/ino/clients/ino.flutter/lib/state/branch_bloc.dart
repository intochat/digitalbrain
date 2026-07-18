import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/grpc/ino_client.dart';
import 'package:ino_flutter/state/timeline_bloc.dart';

sealed class BranchBlocEvent {}

class BranchListLoaded extends BranchBlocEvent {}

class BranchForked extends BranchBlocEvent {
  BranchForked({
    required this.checkpointSequence,
    required this.modifiedEventKind,
    required this.modifiedEventSource,
    required this.modifiedEventVerb,
  });

  final int checkpointSequence;
  final String modifiedEventKind;
  final String modifiedEventSource;
  final String modifiedEventVerb;
}

class BranchReplayed extends BranchBlocEvent {
  BranchReplayed(this.universeId);
  final String universeId;
}

class BranchCompared extends BranchBlocEvent {
  BranchCompared(this.universeA, this.universeB);
  final String universeA;
  final String universeB;
}

class BranchSelected extends BranchBlocEvent {
  BranchSelected(this.universeId);
  final String universeId;
}

class BranchDiffCleared extends BranchBlocEvent {}

class BranchItem {
  const BranchItem({
    required this.id,
    required this.sourceTimeline,
    required this.forkSequence,
    required this.totalEvents,
    required this.hasReplayed,
  });

  final String id;
  final String sourceTimeline;
  final int forkSequence;
  final int totalEvents;
  final bool hasReplayed;
}

class BranchDiffResult {
  const BranchDiffResult({
    required this.sharedEvents,
    required this.divergedAfterSequence,
    required this.onlyInA,
    required this.onlyInB,
  });

  final int sharedEvents;
  final int divergedAfterSequence;
  final List<TimelineEntry> onlyInA;
  final List<TimelineEntry> onlyInB;
}

class BranchBlocState {
  const BranchBlocState({
    this.universes = const [],
    this.selectedId,
    this.selectedTimeline = const [],
    this.diff,
    this.isLoading = false,
    this.replayResult,
    this.error,
  });

  final List<BranchItem> universes;
  final String? selectedId;
  final List<TimelineEntry> selectedTimeline;
  final BranchDiffResult? diff;
  final bool isLoading;
  final String? replayResult;
  final String? error;

  BranchBlocState copyWith({
    List<BranchItem>? universes,
    String? selectedId,
    bool clearSelectedId = false,
    List<TimelineEntry>? selectedTimeline,
    BranchDiffResult? diff,
    bool clearDiff = false,
    bool? isLoading,
    String? replayResult,
    bool clearReplayResult = false,
    String? error,
    bool clearError = false,
  }) {
    return BranchBlocState(
      universes: universes ?? this.universes,
      selectedId: clearSelectedId ? null : (selectedId ?? this.selectedId),
      selectedTimeline: selectedTimeline ?? this.selectedTimeline,
      diff: clearDiff ? null : (diff ?? this.diff),
      isLoading: isLoading ?? this.isLoading,
      replayResult:
          clearReplayResult ? null : (replayResult ?? this.replayResult),
      error: clearError ? null : (error ?? this.error),
    );
  }
}

class BranchBloc extends Bloc<BranchBlocEvent, BranchBlocState> {
  BranchBloc({required InoGrpcClient client})
      : _client = client,
        super(const BranchBlocState()) {
    on<BranchListLoaded>(_onListLoaded);
    on<BranchForked>(_onForked);
    on<BranchReplayed>(_onReplayed);
    on<BranchCompared>(_onCompared);
    on<BranchSelected>(_onSelected);
    on<BranchDiffCleared>(_onDiffCleared);
  }

  final InoGrpcClient _client;

  Future<void> _onListLoaded(
    BranchListLoaded event,
    Emitter<BranchBlocState> emit,
  ) async {
    emit(state.copyWith(isLoading: true, clearError: true));

    try {
      final response = await _client.listUniverses();
      final items = response.universes
          .map(
            (u) => BranchItem(
              id: u.universeId,
              sourceTimeline: u.sourceTimeline,
              forkSequence: u.forkSequence.toInt(),
              totalEvents: u.totalEvents,
              hasReplayed: u.hasReplayed,
            ),
          )
          .toList();
      emit(state.copyWith(universes: items, isLoading: false));
    } catch (e) {
      emit(state.copyWith(isLoading: false, error: e.toString()));
    }
  }

  Future<void> _onForked(
    BranchForked event,
    Emitter<BranchBlocState> emit,
  ) async {
    emit(state.copyWith(isLoading: true, clearError: true));

    try {
      await _client.forkUniverse(
        checkpointSequence: event.checkpointSequence,
        modifiedEventKind: event.modifiedEventKind,
        modifiedEventSource: event.modifiedEventSource,
        modifiedEventVerb: event.modifiedEventVerb,
      );
      add(BranchListLoaded());
    } catch (e) {
      emit(state.copyWith(isLoading: false, error: e.toString()));
    }
  }

  Future<void> _onReplayed(
    BranchReplayed event,
    Emitter<BranchBlocState> emit,
  ) async {
    emit(state.copyWith(isLoading: true, clearError: true));

    try {
      final response = await _client.replayUniverse(event.universeId);
      emit(state.copyWith(
        isLoading: false,
        replayResult:
            'Replayed ${response.eventsReplayed} events: ${response.summary}',
      ));
      add(BranchListLoaded());
    } catch (e) {
      emit(state.copyWith(isLoading: false, error: e.toString()));
    }
  }

  Future<void> _onCompared(
    BranchCompared event,
    Emitter<BranchBlocState> emit,
  ) async {
    emit(state.copyWith(isLoading: true, clearError: true));

    try {
      final response =
          await _client.compareUniverses(event.universeA, event.universeB);
      final diff = BranchDiffResult(
        sharedEvents: response.sharedEvents,
        divergedAfterSequence: response.divergedAfterSequence.toInt(),
        onlyInA: response.onlyInA
            .map(
              (evt) => TimelineEntry(
                sequence: evt.sequence.toInt(),
                kind: evt.kind,
                source: evt.source,
                target: evt.target,
                timestamp: evt.timestamp.toInt(),
                decay: evt.decay,
              ),
            )
            .toList(),
        onlyInB: response.onlyInB
            .map(
              (evt) => TimelineEntry(
                sequence: evt.sequence.toInt(),
                kind: evt.kind,
                source: evt.source,
                target: evt.target,
                timestamp: evt.timestamp.toInt(),
                decay: evt.decay,
              ),
            )
            .toList(),
      );
      emit(state.copyWith(diff: diff, isLoading: false));
    } catch (e) {
      emit(state.copyWith(isLoading: false, error: e.toString()));
    }
  }

  Future<void> _onSelected(
    BranchSelected event,
    Emitter<BranchBlocState> emit,
  ) async {
    emit(state.copyWith(
      selectedId: event.universeId,
      isLoading: true,
      clearError: true,
    ));

    try {
      final entries = <TimelineEntry>[];
      await for (final evt in _client.getUniverseTimeline(event.universeId)) {
        entries.add(TimelineEntry(
          sequence: evt.sequence.toInt(),
          kind: evt.kind,
          source: evt.source,
          target: evt.target,
          timestamp: evt.timestamp.toInt(),
          decay: evt.decay,
        ));
      }
      emit(state.copyWith(selectedTimeline: entries, isLoading: false));
    } catch (e) {
      emit(state.copyWith(isLoading: false, error: e.toString()));
    }
  }

  void _onDiffCleared(
    BranchDiffCleared event,
    Emitter<BranchBlocState> emit,
  ) {
    emit(state.copyWith(clearDiff: true));
  }
}
