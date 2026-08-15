final class BrainModule {
  const BrainModule({required this.id});

  final String id;

  factory BrainModule.fromJson(Map<String, Object?> json) {
    return BrainModule(id: json['id'] as String);
  }
}

final class BrainNeuron {
  const BrainNeuron({
    required this.id,
    required this.grainType,
    required this.identity,
    required this.placement,
  });

  final String id;
  final String grainType;
  final String identity;
  final String placement;

  factory BrainNeuron.fromJson(Map<String, Object?> json) {
    return BrainNeuron(
      id: json['id'] as String,
      grainType: json['grainType'] as String,
      identity: json['identity'] as String,
      placement: json['placement'] as String,
    );
  }
}

final class BrainTopologySnapshot {
  const BrainTopologySnapshot({
    required this.modules,
    required this.neurons,
    required this.observedAt,
    this.connections = const [],
    this.broadcastRoutes = const [],
  });

  final List<BrainModule> modules;
  final List<BrainNeuron> neurons;
  final DateTime observedAt;
  final List<BrainConnection> connections;
  final List<BrainBroadcastRoute> broadcastRoutes;

  factory BrainTopologySnapshot.fromJson(Map<String, Object?> json) {
    return BrainTopologySnapshot(
      modules: (json['modules'] as List<Object?>)
          .map(
            (module) =>
                BrainModule.fromJson(Map<String, Object?>.from(module! as Map)),
          )
          .toList(growable: false),
      neurons: (json['neurons'] as List<Object?>)
          .map(
            (neuron) =>
                BrainNeuron.fromJson(Map<String, Object?>.from(neuron! as Map)),
          )
          .toList(growable: false),
      observedAt: DateTime.parse(json['observedAt'] as String).toUtc(),
      connections: (json['connections'] as List<Object?>? ?? const [])
          .map(
            (connection) => BrainConnection.fromJson(
              Map<String, Object?>.from(connection! as Map),
            ),
          )
          .toList(growable: false),
      broadcastRoutes: (json['broadcastRoutes'] as List<Object?>? ?? const [])
          .map(
            (route) => BrainBroadcastRoute.fromJson(
              Map<String, Object?>.from(route! as Map),
            ),
          )
          .toList(growable: false),
    );
  }
}

final class BrainBroadcastRoute {
  const BrainBroadcastRoute({
    required this.synapseAlias,
    required this.handlerGrainType,
  });

  final String synapseAlias;
  final String handlerGrainType;

  factory BrainBroadcastRoute.fromJson(Map<String, Object?> json) {
    return BrainBroadcastRoute(
      synapseAlias: json['synapseAlias'] as String? ?? '',
      handlerGrainType: json['handlerGrainType'] as String? ?? '',
    );
  }
}

final class BrainConnection {
  const BrainConnection({
    required this.connectionId,
    required this.source,
    required this.synapseAlias,
    required this.target,
    this.transform,
    this.expiresAt,
  });

  final String connectionId;
  final String source;
  final String synapseAlias;
  final String target;
  final String? transform;
  final DateTime? expiresAt;

  factory BrainConnection.fromJson(Map<String, Object?> json) {
    final expiresAt = json['expiresAt'] as String?;
    return BrainConnection(
      connectionId: json['connectionId'] as String,
      source: json['source'] as String,
      synapseAlias: json['synapseAlias'] as String,
      target: json['target'] as String,
      transform: json['transform'] as String?,
      expiresAt: expiresAt == null ? null : DateTime.parse(expiresAt).toUtc(),
    );
  }
}

final class GraphChangeEvent {
  const GraphChangeEvent({
    required this.sequence,
    required this.kind,
    required this.connectionId,
    required this.timestamp,
    this.source,
    this.synapseAlias,
    this.target,
  });

  final int sequence;
  final String kind;
  final String connectionId;
  final String? source;
  final String? synapseAlias;
  final String? target;
  final DateTime timestamp;

  factory GraphChangeEvent.fromJson(Map<String, Object?> json) {
    return GraphChangeEvent(
      sequence: (json['sequence'] as num).toInt(),
      kind: json['kind'] as String,
      connectionId: json['connectionId'] as String,
      source: json['source'] as String?,
      synapseAlias: json['synapseAlias'] as String?,
      target: json['target'] as String?,
      timestamp: DateTime.parse(json['timestamp'] as String).toUtc(),
    );
  }
}
