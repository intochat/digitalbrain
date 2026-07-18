import 'dart:async';

import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/grpc/ino_client.dart';
import 'package:ino_flutter/grpc/generated/ino.pb.dart' as pb;

sealed class RoutingEvent {}

class RoutingRefreshRequested extends RoutingEvent {}

class _RoutingLoaded extends RoutingEvent {
  _RoutingLoaded(this.entries);
  final List<pb.RoutingDecisionView> entries;
}

class _RoutingFailed extends RoutingEvent {
  _RoutingFailed(this.error);
  final String error;
}

sealed class RoutingState {
  const RoutingState();
}

class RoutingLoading extends RoutingState {
  const RoutingLoading();
}

class RoutingLoaded extends RoutingState {
  const RoutingLoaded(this.entries);
  final List<pb.RoutingDecisionView> entries;
}

class RoutingError extends RoutingState {
  const RoutingError(this.message);
  final String message;
}

class RoutingBloc extends Bloc<RoutingEvent, RoutingState> {
  RoutingBloc({required InoGrpcClient client})
      : _client = client,
        super(const RoutingLoading()) {
    on<RoutingRefreshRequested>(_onRefresh);
    on<_RoutingLoaded>(_onLoaded);
    on<_RoutingFailed>(_onFailed);
    _timer = Timer.periodic(const Duration(seconds: 2), (_) {
      add(RoutingRefreshRequested());
    });
    add(RoutingRefreshRequested());
  }

  final InoGrpcClient _client;
  Timer? _timer;

  Future<void> _onRefresh(
    RoutingRefreshRequested event,
    Emitter<RoutingState> emit,
  ) async {
    try {
      final resp = await _client.listRoutingDecisions(count: 20);
      add(_RoutingLoaded(resp.entries.toList()));
    } catch (ex) {
      add(_RoutingFailed(ex.toString()));
    }
  }

  void _onLoaded(_RoutingLoaded event, Emitter<RoutingState> emit) {
    emit(RoutingLoaded(event.entries));
  }

  void _onFailed(_RoutingFailed event, Emitter<RoutingState> emit) {
    emit(RoutingError(event.error));
  }

  @override
  Future<void> close() {
    _timer?.cancel();
    return super.close();
  }
}
