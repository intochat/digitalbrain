import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/grpc/ino_client.dart';
import 'package:ino_flutter/grpc/generated/ino.pb.dart' as pb;

sealed class SkillsBlocEvent {}

class LoadSkills extends SkillsBlocEvent {
  LoadSkills({this.domain = '', this.query = ''});
  final String domain;
  final String query;
}

class InstallSkillRequested extends SkillsBlocEvent {
  InstallSkillRequested(this.skillId);
  final String skillId;
}

class _SkillsLoaded extends SkillsBlocEvent {
  _SkillsLoaded(this.skills);
  final List<SkillItem> skills;
}

class _SkillsFailed extends SkillsBlocEvent {
  _SkillsFailed(this.error);
  final String error;
}

class SkillItem {
  const SkillItem({
    required this.id,
    required this.name,
    required this.domain,
    required this.description,
    required this.installed,
    this.capabilities = const [],
    this.invocationCount = 0,
  });

  factory SkillItem.fromProto(pb.SkillInfo proto) {
    return SkillItem(
      id: proto.id,
      name: proto.name,
      domain: proto.domain,
      description: proto.description,
      installed: proto.installed,
      capabilities: List<String>.unmodifiable(proto.capabilities),
    );
  }

  final String id;
  final String name;
  final String domain;
  final String description;
  final bool installed;
  final List<String> capabilities;
  final int invocationCount;

  SkillItem copyWith({bool? installed, int? invocationCount}) {
    return SkillItem(
      id: id,
      name: name,
      domain: domain,
      description: description,
      installed: installed ?? this.installed,
      capabilities: capabilities,
      invocationCount: invocationCount ?? this.invocationCount,
    );
  }
}

class SkillsBlocState {
  const SkillsBlocState({
    this.skills = const [],
    this.isLoading = false,
    this.installingId,
    this.error,
  });

  final List<SkillItem> skills;
  final bool isLoading;
  final String? installingId;
  final String? error;

  SkillsBlocState copyWith({
    List<SkillItem>? skills,
    bool? isLoading,
    String? installingId,
    bool clearInstallingId = false,
    String? error,
    bool clearError = false,
  }) {
    return SkillsBlocState(
      skills: skills ?? this.skills,
      isLoading: isLoading ?? this.isLoading,
      installingId: clearInstallingId ? null : (installingId ?? this.installingId),
      error: clearError ? null : (error ?? this.error),
    );
  }
}

class SkillsBloc extends Bloc<SkillsBlocEvent, SkillsBlocState> {
  SkillsBloc({required InoGrpcClient client})
      : _client = client,
        super(const SkillsBlocState()) {
    on<LoadSkills>(_onLoadSkills);
    on<InstallSkillRequested>(_onInstallSkill);
    on<_SkillsLoaded>(_onSkillsLoaded);
    on<_SkillsFailed>(_onSkillsFailed);
  }

  final InoGrpcClient _client;
  String _lastDomain = '';
  String _lastQuery = '';

  Future<void> _onLoadSkills(
    LoadSkills event,
    Emitter<SkillsBlocState> emit,
  ) async {
    _lastDomain = event.domain;
    _lastQuery = event.query;
    emit(state.copyWith(isLoading: true, clearError: true));

    try {
      final response = await _client.listSkills(
        domain: event.domain,
        query: event.query,
      );
      final items = response.skills.map(SkillItem.fromProto).toList();
      add(_SkillsLoaded(items));
    } catch (e) {
      add(_SkillsFailed(e.toString()));
    }
  }

  Future<void> _onInstallSkill(
    InstallSkillRequested event,
    Emitter<SkillsBlocState> emit,
  ) async {
    emit(state.copyWith(installingId: event.skillId));

    try {
      await _client.installSkill(event.skillId);
      add(LoadSkills(domain: _lastDomain, query: _lastQuery));
    } catch (e) {
      add(_SkillsFailed(e.toString()));
    }
  }

  void _onSkillsLoaded(
    _SkillsLoaded event,
    Emitter<SkillsBlocState> emit,
  ) {
    emit(state.copyWith(
      skills: event.skills,
      isLoading: false,
      clearInstallingId: true,
    ));
  }

  void _onSkillsFailed(
    _SkillsFailed event,
    Emitter<SkillsBlocState> emit,
  ) {
    emit(state.copyWith(
      isLoading: false,
      clearInstallingId: true,
      error: event.error,
    ));
  }
}
