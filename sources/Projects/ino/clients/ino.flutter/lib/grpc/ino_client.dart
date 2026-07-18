import 'package:fixnum/fixnum.dart';
import 'package:grpc/grpc.dart';
import 'package:grpc/grpc_or_grpcweb.dart';
import 'package:ino_flutter/grpc/generated/ino.pbgrpc.dart';
import 'package:ino_flutter/grpc/generated/ino.pb.dart' as pb;

export 'package:ino_flutter/grpc/generated/ino.pb.dart';

class InoGrpcClient {
  InoGrpcClient({
    required String host,
    required int port,
    bool transportSecure = false,
    List<ClientInterceptor> interceptors = const [],
  }) : _channel = GrpcOrGrpcWebClientChannel.toSingleEndpoint(
          host: host,
          port: port,
          transportSecure: transportSecure,
        ) {
    _stub = InoClient(_channel, interceptors: interceptors);
  }

  final GrpcOrGrpcWebClientChannel _channel;
  late final InoClient _stub;

  GrpcOrGrpcWebClientChannel get channel => _channel;

  Stream<pb.ChatResponse> chat(String message, {String userId = 'default'}) {
    return _stub.chat(pb.ChatRequest()
      ..message = message
      ..userId = userId);
  }

  Stream<pb.InoEvent> streamEvents({String userId = 'default'}) {
    return _stub.streamEvents(pb.EventSubscription()..userId = userId);
  }

  Stream<pb.PersonaState> streamPersonaState({String userId = 'default'}) {
    return _stub
        .streamPersonaState(pb.PersonaSubscription()..userId = userId);
  }

  Future<pb.FireResponse> fireSynapse(
    String verb,
    Map<String, String> args, {
    String targetNeuron = '',
  }) {
    return _stub.fireSynapse(pb.FireRequest()
      ..verb = verb
      ..args.addAll(args)
      ..targetNeuron = targetNeuron);
  }

  Stream<pb.TimelineEvent> getTimeline({int limit = 50, int minDecay = 30}) {
    return _stub.getTimeline(pb.TimelineQuery()
      ..limit = limit
      ..minDecay = minDecay);
  }

  Future<pb.ListSkillsResponse> listSkills({
    String domain = '',
    String query = '',
  }) {
    return _stub.listSkills(pb.ListSkillsRequest()
      ..domain = domain
      ..query = query);
  }

  Future<pb.InstallSkillResponse> installSkill(String skillId) {
    return _stub.installSkill(pb.InstallSkillRequest()..skillId = skillId);
  }

  Future<pb.SkillUIResponse> getSkillUI(String skillId) {
    return _stub.getSkillUI(pb.SkillUIRequest()..skillId = skillId);
  }

  Future<pb.ForkUniverseResponse> forkUniverse({
    String sourceTimeline = 'global',
    required int checkpointSequence,
    required String modifiedEventKind,
    required String modifiedEventSource,
    String modifiedEventVerb = '',
    Map<String, String> modifiedEventPayload = const {},
  }) {
    final req = pb.ForkUniverseRequest()
      ..sourceTimeline = sourceTimeline
      ..checkpointSequence = Int64(checkpointSequence)
      ..modifiedEventKind = modifiedEventKind
      ..modifiedEventSource = modifiedEventSource
      ..modifiedEventVerb = modifiedEventVerb;
    req.modifiedEventPayload.addAll(modifiedEventPayload);
    return _stub.forkUniverse(req);
  }

  Future<pb.ReplayUniverseResponse> replayUniverse(String universeId) {
    return _stub
        .replayUniverse(pb.ReplayUniverseRequest()..universeId = universeId);
  }

  Future<pb.CompareUniversesResponse> compareUniverses(
    String universeA,
    String universeB,
  ) {
    return _stub.compareUniverses(
      pb.CompareUniversesRequest()
        ..universeA = universeA
        ..universeB = universeB,
    );
  }

  Stream<pb.TimelineEvent> getUniverseTimeline(String universeId) {
    return _stub.getUniverseTimeline(
      pb.UniverseTimelineQuery()..universeId = universeId,
    );
  }

  Future<pb.UniverseInfoResponse> getUniverseInfo(String universeId) {
    return _stub
        .getUniverseInfo(pb.UniverseInfoRequest()..universeId = universeId);
  }

  Future<pb.ListUniversesResponse> listUniverses() {
    return _stub.listUniverses(pb.ListUniversesRequest());
  }

  Future<pb.StateAtResponse> getStateAt(int sequence) {
    return _stub
        .getStateAt(pb.StateAtRequest()..sequence = Int64(sequence));
  }

  Future<pb.SwitchPersonaResponse> switchPersona(String personaName) {
    return _stub
        .switchPersona(pb.SwitchPersonaRequest()..personaName = personaName);
  }

  Future<pb.TelemetryResponse> getTelemetry({
    String query = 'most_used_skills',
    int limit = 10,
  }) {
    return _stub.getTelemetry(pb.TelemetryRequest()
      ..query = query
      ..limit = limit);
  }

  // Inspector E.3 — Slice 3B: proposal lifecycle + routing decisions
  Future<pb.ListProposalsResponse> listProposals({
    String userId = 'default',
    pb.ProposalStatusProto? filter,
    int skip = 0,
    int take = 100,
  }) {
    final req = pb.ListProposalsRequest()
      ..userId = userId
      ..skip = skip
      ..take = take;
    if (filter != null) req.filter = filter;
    return _stub.listProposals(req);
  }

  Future<pb.DecideProposalResponse> decideProposal({
    required String proposalId,
    required pb.ProposalStatusProto decision,
    String userId = 'default',
    String? overrideScriptBody,
  }) {
    final req = pb.DecideProposalRequest()
      ..userId = userId
      ..proposalId = proposalId
      ..decision = decision;
    if (overrideScriptBody != null) req.overrideScriptBody = overrideScriptBody;
    return _stub.decideProposal(req);
  }

  Future<pb.ListRoutingDecisionsResponse> listRoutingDecisions({
    String userId = 'default',
    int count = 20,
  }) {
    return _stub.listRoutingDecisions(pb.ListRoutingDecisionsRequest()
      ..userId = userId
      ..count = count);
  }

  // Slice 4 — RFW event callback. Called when a RemoteWidget tree dispatches
  // an event (e.g. flight.selected) — the gateway resolves correlation_id
  // back to the originating plan grain and round-trips the next plan step's
  // RFW payload inline in the response.
  Future<pb.RfwEventResponse> rfwEvent({
    required String correlationId,
    required String eventName,
    Map<String, String> args = const {},
  }) {
    final req = pb.RfwEventRequest()
      ..correlationId = correlationId
      ..eventName = eventName;
    req.args.addAll(args);
    return _stub.rfwEvent(req);
  }

  Future<void> shutdown() => _channel.shutdown();
}
