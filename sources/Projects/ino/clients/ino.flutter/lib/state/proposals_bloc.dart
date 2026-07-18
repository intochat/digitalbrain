import 'dart:async';

import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/grpc/ino_client.dart';
import 'package:ino_flutter/grpc/generated/ino.pb.dart' as pb;

sealed class ProposalsEvent {}

class ProposalsRefreshRequested extends ProposalsEvent {}

class ProposalApproved extends ProposalsEvent {
  ProposalApproved(this.proposalId);
  final String proposalId;
}

class ProposalRejected extends ProposalsEvent {
  ProposalRejected(this.proposalId);
  final String proposalId;
}

class _ProposalsLoaded extends ProposalsEvent {
  _ProposalsLoaded(this.entries);
  final List<pb.ProposalView> entries;
}

class _ProposalsFailed extends ProposalsEvent {
  _ProposalsFailed(this.error);
  final String error;
}

sealed class ProposalsState {
  const ProposalsState();
}

class ProposalsLoading extends ProposalsState {
  const ProposalsLoading();
}

class ProposalsLoaded extends ProposalsState {
  const ProposalsLoaded({
    required this.pending,
    required this.approved,
    required this.rejected,
  });
  final List<pb.ProposalView> pending;
  final List<pb.ProposalView> approved;
  final List<pb.ProposalView> rejected;
}

class ProposalsError extends ProposalsState {
  const ProposalsError(this.message);
  final String message;
}

class ProposalsBloc extends Bloc<ProposalsEvent, ProposalsState> {
  ProposalsBloc({required InoGrpcClient client})
      : _client = client,
        super(const ProposalsLoading()) {
    on<ProposalsRefreshRequested>(_onRefresh);
    on<ProposalApproved>(_onApprove);
    on<ProposalRejected>(_onReject);
    on<_ProposalsLoaded>(_onLoaded);
    on<_ProposalsFailed>(_onFailed);
    _timer = Timer.periodic(const Duration(seconds: 5), (_) {
      add(ProposalsRefreshRequested());
    });
    add(ProposalsRefreshRequested());
  }

  final InoGrpcClient _client;
  Timer? _timer;

  Future<void> _onRefresh(
    ProposalsRefreshRequested event,
    Emitter<ProposalsState> emit,
  ) async {
    try {
      final resp = await _client.listProposals(take: 100);
      add(_ProposalsLoaded(resp.entries.toList()));
    } catch (ex) {
      add(_ProposalsFailed(ex.toString()));
    }
  }

  Future<void> _onApprove(
    ProposalApproved event,
    Emitter<ProposalsState> emit,
  ) async {
    try {
      await _client.decideProposal(
        proposalId: event.proposalId,
        decision: pb.ProposalStatusProto.PROPOSAL_STATUS_APPROVED,
      );
      add(ProposalsRefreshRequested());
    } catch (ex) {
      add(_ProposalsFailed(ex.toString()));
    }
  }

  Future<void> _onReject(
    ProposalRejected event,
    Emitter<ProposalsState> emit,
  ) async {
    try {
      await _client.decideProposal(
        proposalId: event.proposalId,
        decision: pb.ProposalStatusProto.PROPOSAL_STATUS_REJECTED,
      );
      add(ProposalsRefreshRequested());
    } catch (ex) {
      add(_ProposalsFailed(ex.toString()));
    }
  }

  void _onLoaded(_ProposalsLoaded event, Emitter<ProposalsState> emit) {
    final pending = <pb.ProposalView>[];
    final approved = <pb.ProposalView>[];
    final rejected = <pb.ProposalView>[];
    for (final p in event.entries) {
      switch (p.status) {
        case pb.ProposalStatusProto.PROPOSAL_STATUS_PENDING:
          pending.add(p);
        case pb.ProposalStatusProto.PROPOSAL_STATUS_APPROVED:
          approved.add(p);
        case pb.ProposalStatusProto.PROPOSAL_STATUS_REJECTED:
          rejected.add(p);
        default:
          break;
      }
    }
    emit(ProposalsLoaded(
      pending: pending,
      approved: approved,
      rejected: rejected,
    ));
  }

  void _onFailed(_ProposalsFailed event, Emitter<ProposalsState> emit) {
    emit(ProposalsError(event.error));
  }

  @override
  Future<void> close() {
    _timer?.cancel();
    return super.close();
  }
}
