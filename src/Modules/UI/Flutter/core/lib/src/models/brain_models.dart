/// An owner-scoped observation of actual neurons and source-owned synapses.
final class BrainSnapshot {
  const BrainSnapshot({
    required this.rootId,
    required this.observedAt,
    this.scope = '',
    this.truncated = false,
    this.nodes = const [],
    this.synapses = const [],
    this.activity = const [],
  });
  final String rootId, scope;
  final DateTime observedAt;
  final bool truncated;
  final List<BrainNeuron> nodes;
  final List<BrainSynapse> synapses;
  final List<BrainActivity> activity;
  factory BrainSnapshot.fromJson(Map<String, dynamic> json) => BrainSnapshot(
    rootId: json['rootId'] as String,
    observedAt: DateTime.parse(json['observedAt'] as String),
    scope: json['scope'] as String? ?? '',
    truncated: json['truncated'] == true,
    nodes: _objects(
      json['nodes'],
    ).map(BrainNeuron.fromJson).toList(growable: false),
    synapses: _objects(
      json['synapses'],
    ).map(BrainSynapse.fromJson).toList(growable: false),
    activity: _objects(
      json['activity'],
    ).map(BrainActivity.fromJson).toList(growable: false),
  );
}

final class BrainNeuron {
  const BrainNeuron({
    required this.id,
    required this.type,
    required this.name,
    required this.label,
    required this.module,
    this.role = 'observed',
    this.status = 'Idle',
    this.handledSignals = const [],
    this.incomingSequence = 0,
    this.outgoingSequence = 0,
    this.lastActivityAt,
  });
  final String id, type, name, label, module, role, status;
  final List<String> handledSignals;
  final int incomingSequence, outgoingSequence;
  final DateTime? lastActivityAt;
  factory BrainNeuron.fromJson(Map<String, dynamic> j) => BrainNeuron(
    id: j['id'] as String,
    type: j['type'] as String,
    name: j['name'] as String,
    label: j['label'] as String,
    module: j['module'] as String,
    role: j['role'] as String? ?? 'observed',
    status: j['status'] as String? ?? 'Idle',
    handledSignals: (j['handledSignals'] as List? ?? []).cast<String>(),
    incomingSequence: (j['incomingSequence'] as num?)?.toInt() ?? 0,
    outgoingSequence: (j['outgoingSequence'] as num?)?.toInt() ?? 0,
    lastActivityAt: _date(j['lastActivityAt']),
  );
}

final class BrainSynapse {
  const BrainSynapse({
    required this.id,
    required this.sourceId,
    required this.targetId,
    required this.signalType,
    required this.kind,
    this.weight = 0,
    this.fireCount = 0,
    this.lastFiredAt,
    this.isBlocking = false,
    this.canUnsubscribe = false,
  });
  final String id, sourceId, targetId, signalType, kind;
  final double weight;
  final int fireCount;
  final DateTime? lastFiredAt;
  final bool isBlocking, canUnsubscribe;
  factory BrainSynapse.fromJson(Map<String, dynamic> j) => BrainSynapse(
    id: j['id'] as String,
    sourceId: j['sourceId'] as String,
    targetId: j['targetId'] as String,
    signalType: j['signalType'] as String,
    kind: j['kind'] as String,
    weight: (j['weight'] as num?)?.toDouble() ?? 0,
    fireCount: (j['fireCount'] as num?)?.toInt() ?? 0,
    lastFiredAt: _date(j['lastFiredAt']),
    isBlocking: j['isBlocking'] == true,
    canUnsubscribe: j['canUnsubscribe'] == true,
  );
}

final class BrainActivity {
  const BrainActivity({
    required this.id,
    required this.neuronId,
    required this.direction,
    required this.sequence,
    required this.signalType,
    required this.timestamp,
    this.callerId,
    this.correlationId,
    this.summary = '',
    this.payloadPreview,
  });
  final String id, neuronId, direction, signalType, summary;
  final String? callerId, correlationId;
  final Object? payloadPreview;
  final int sequence;
  final DateTime timestamp;
  factory BrainActivity.fromJson(Map<String, dynamic> j) => BrainActivity(
    id: j['id'] as String,
    neuronId: j['neuronId'] as String,
    direction: j['direction'] as String,
    sequence: (j['sequence'] as num).toInt(),
    signalType: j['signalType'] as String,
    timestamp: DateTime.parse(j['timestamp'] as String),
    callerId: j['callerId'] as String?,
    correlationId: j['correlationId'] as String?,
    summary: j['summary'] as String? ?? '',
    payloadPreview: j['payloadPreview'],
  );
}

Iterable<Map<String, dynamic>> _objects(Object? value) =>
    (value as List? ?? []).map((item) => (item as Map).cast<String, dynamic>());
DateTime? _date(Object? value) =>
    value is String ? DateTime.tryParse(value) : null;

typedef ReadBrain = Future<BrainSnapshot> Function();
typedef SetBrainSubscription =
    Future<void> Function({
      required String sourceId,
      required String targetId,
      required String signalType,
      required bool subscribed,
    });
