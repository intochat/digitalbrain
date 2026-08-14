import 'dart:convert';

int _integer(Object? value, String name) {
  if (value is num) return value.toInt();
  throw FormatException('$name is not an integer');
}

Map<String, Object?> _object(Object? value, String name) {
  if (value is Map) return Map<String, Object?>.from(value);
  throw FormatException('$name is not an object');
}

List<Map<String, Object?>> _objects(Object? value, String name) {
  if (value is! List) throw FormatException('$name is not a list');
  return value.map((item) => _object(item, name)).toList(growable: false);
}

final class ProductModule {
  const ProductModule({
    required this.id,
    required this.displayName,
    required this.status,
    this.setupMessage,
  });
  final String id;
  final String displayName;
  final String status;
  final String? setupMessage;
  bool get isReady => status.toLowerCase() == 'ready';
  String get statusLabel => isReady ? 'Ready' : status;
  factory ProductModule.fromJson(Map<String, Object?> json) => ProductModule(
    id: json['id'] as String,
    displayName: json['displayName'] as String,
    status: json['status'] as String,
    setupMessage: json['setupMessage'] as String?,
  );
}

final class ProductOperation {
  const ProductOperation({
    required this.id,
    required this.moduleId,
    required this.displayName,
    required this.inputSchema,
    required this.resultSchema,
  });
  final String id;
  final String moduleId;
  final String displayName;
  final String inputSchema;
  final String resultSchema;
  factory ProductOperation.fromJson(Map<String, Object?> json) =>
      ProductOperation(
        id: json['id'] as String,
        moduleId: json['moduleId'] as String,
        displayName: json['displayName'] as String,
        inputSchema: json['inputSchema'] as String,
        resultSchema: json['resultSchema'] as String,
      );
}

final class ProductActivityReceipt {
  const ProductActivityReceipt({
    required this.activityId,
    required this.operationId,
  });
  final String activityId;
  final String operationId;
  factory ProductActivityReceipt.fromJson(Map<String, Object?> json) =>
      ProductActivityReceipt(
        activityId: json['activityId'] as String,
        operationId: json['operationId'] as String,
      );
}

final class ProductActivity {
  const ProductActivity({
    required this.activityId,
    required this.operationId,
    required this.workspaceId,
    required this.status,
    required this.sequence,
    this.resultJson,
    this.problem,
  });
  final String activityId;
  final String operationId;
  final String workspaceId;
  final int status;
  final int sequence;
  final String? resultJson;
  final String? problem;
  bool get isTerminal => status >= 3;
  bool get isCompleted => status == 3;
  Object? get result => resultJson == null ? null : jsonDecode(resultJson!);
  String get statusLabel => switch (status) {
    0 => 'Accepted',
    1 => 'Running',
    2 => 'Awaiting confirmation',
    3 => 'Completed',
    4 => 'Refused',
    5 => 'Failed',
    6 => 'Cancelled',
    _ => 'Unknown',
  };
  factory ProductActivity.fromJson(Map<String, Object?> json) =>
      ProductActivity(
        activityId: json['activityId'] as String,
        operationId: json['operationId'] as String,
        workspaceId: json['workspaceId'] as String,
        status: _integer(json['status'], 'activity status'),
        sequence: _integer(json['sequence'], 'activity sequence'),
        resultJson: json['resultJson'] as String?,
        problem: json['problem'] as String?,
      );
}

final class ChatToolResult {
  const ChatToolResult({required this.operationId, required this.resultJson});
  final String operationId;
  final String resultJson;
  factory ChatToolResult.fromJson(Map<String, Object?> json) => ChatToolResult(
    operationId: json['operationId'] as String,
    resultJson: json['resultJson'] as String,
  );
}

final class ChatTurn {
  const ChatTurn({required this.response, required this.tools});
  final String response;
  final List<ChatToolResult> tools;
  factory ChatTurn.fromJson(Map<String, Object?> json) => ChatTurn(
    response: json['response'] as String,
    tools: _objects(
      json['tools'],
      'chat tools',
    ).map(ChatToolResult.fromJson).toList(growable: false),
  );
}

final class ChatTurnEnvelope {
  const ChatTurnEnvelope({required this.activityId, required this.turn});
  final String activityId;
  final ChatTurn turn;
  factory ChatTurnEnvelope.fromJson(Map<String, Object?> json) =>
      ChatTurnEnvelope(
        activityId: json['activityId'] as String,
        turn: ChatTurn.fromJson(_object(json['turn'], 'chat turn')),
      );
}

final class BrainNeuron {
  const BrainNeuron({
    required this.id,
    required this.moduleId,
    required this.roleId,
    required this.scope,
    required this.firingCount,
  });
  final String id;
  final String moduleId;
  final String roleId;
  final String scope;
  final int firingCount;
  factory BrainNeuron.fromJson(Map<String, Object?> json) => BrainNeuron(
    id: json['id'] as String,
    moduleId: json['moduleId'] as String,
    roleId: json['roleId'] as String,
    scope: json['scope'] as String,
    firingCount: _integer(json['firingCount'], 'neuron firing count'),
  );
}

final class BrainSynapse {
  const BrainSynapse({
    required this.id,
    required this.revision,
    required this.sourceNeuronId,
    required this.targetNeuronId,
    required this.inputContractId,
    required this.outputContractId,
    required this.status,
    required this.usageCount,
    required this.provenanceActivityId,
  });
  final String id;
  final int revision;
  final String sourceNeuronId;
  final String targetNeuronId;
  final String inputContractId;
  final String outputContractId;
  final String status;
  final int usageCount;
  final String provenanceActivityId;
  factory BrainSynapse.fromJson(Map<String, Object?> json) => BrainSynapse(
    id: json['id'] as String,
    revision: _integer(json['revision'], 'synapse revision'),
    sourceNeuronId: json['sourceNeuronId'] as String,
    targetNeuronId: json['targetNeuronId'] as String,
    inputContractId: json['inputContractId'] as String,
    outputContractId: json['outputContractId'] as String,
    status: json['status'] as String,
    usageCount: _integer(json['usageCount'], 'synapse usage count'),
    provenanceActivityId: json['provenanceActivityId'] as String,
  );
}

final class BrainSnapshot {
  const BrainSnapshot({
    required this.workspaceId,
    required this.sequence,
    required this.observedAt,
    required this.neurons,
    required this.synapses,
  });
  final String workspaceId;
  final int sequence;
  final DateTime observedAt;
  final List<BrainNeuron> neurons;
  final List<BrainSynapse> synapses;
  factory BrainSnapshot.fromJson(Map<String, Object?> json) => BrainSnapshot(
    workspaceId: json['workspaceId'] as String,
    sequence: _integer(json['sequence'], 'brain sequence'),
    observedAt: DateTime.parse(json['observedAt'] as String),
    neurons: _objects(
      json['neurons'],
      'brain neurons',
    ).map(BrainNeuron.fromJson).toList(growable: false),
    synapses: _objects(
      json['synapses'],
      'brain synapses',
    ).map(BrainSynapse.fromJson).toList(growable: false),
  );
}

final class BrainJournalRecord {
  const BrainJournalRecord({
    required this.sequence,
    required this.activityId,
    required this.neuronId,
    required this.direction,
    required this.contractId,
    required this.occurredAt,
    required this.routeCount,
    required this.outcome,
    required this.summary,
  });
  final int sequence;
  final String activityId;
  final String neuronId;
  final int direction;
  final String contractId;
  final DateTime occurredAt;
  final int routeCount;
  final String outcome;
  final String summary;
  String get directionLabel => switch (direction) {
    0 => 'Inbound',
    1 => 'Outbound',
    2 => 'Delivery',
    3 => 'Operation',
    4 => 'Assistant',
    5 => 'System',
    _ => 'Unknown',
  };
  factory BrainJournalRecord.fromJson(Map<String, Object?> json) =>
      BrainJournalRecord(
        sequence: _integer(json['sequence'], 'journal sequence'),
        activityId: json['activityId'] as String,
        neuronId: json['neuronId'] as String,
        direction: _integer(json['direction'], 'journal direction'),
        contractId: json['contractId'] as String,
        occurredAt: DateTime.parse(json['occurredAt'] as String),
        routeCount: _integer(json['routeCount'], 'journal route count'),
        outcome: json['outcome'] as String,
        summary: json['summary'] as String,
      );
}

final class BrainJournalPage {
  const BrainJournalPage({
    required this.workspaceId,
    required this.activityId,
    required this.lastSequence,
    required this.records,
    required this.hasMore,
  });
  final String workspaceId;
  final String activityId;
  final int lastSequence;
  final List<BrainJournalRecord> records;
  final bool hasMore;
  factory BrainJournalPage.fromJson(Map<String, Object?> json) =>
      BrainJournalPage(
        workspaceId: json['workspaceId'] as String,
        activityId: json['activityId'] as String,
        lastSequence: _integer(json['lastSequence'], 'journal last sequence'),
        records: _objects(
          json['records'],
          'journal records',
        ).map(BrainJournalRecord.fromJson).toList(growable: false),
        hasMore: json['hasMore'] as bool,
      );
}
