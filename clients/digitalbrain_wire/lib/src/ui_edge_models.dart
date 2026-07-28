final class OpenSceneRequest {
  const OpenSceneRequest({required this.sceneKey, required this.title});

  final String sceneKey;
  final String title;

  Map<String, Object?> toJson() => {'sceneKey': sceneKey, 'title': title};
}

final class ActivateControlRequest {
  const ActivateControlRequest({required this.intent, this.sceneKey});

  final String intent;
  final String? sceneKey;

  Map<String, Object?> toJson() => {
    'intent': intent,
    if (sceneKey != null) 'sceneKey': sceneKey,
  };
}

final class SceneOpenedEvent {
  const SceneOpenedEvent({
    required this.sequence,
    required this.sceneKey,
    required this.title,
    required this.commandId,
    required this.shell,
  });

  final int sequence;
  final String sceneKey;
  final String title;
  final String commandId;
  final String shell;

  factory SceneOpenedEvent.fromJson(Map<String, Object?> json) {
    return SceneOpenedEvent(
      sequence: (json['sequence'] as num).toInt(),
      sceneKey: json['sceneKey'] as String,
      title: json['title'] as String,
      commandId: json['commandId'] as String,
      shell: json['shell'] as String,
    );
  }
}

final class SendMessageRequest {
  const SendMessageRequest({required this.text});

  final String text;

  Map<String, Object?> toJson() => {'text': text};
}

final class ChatTurnEvent {
  const ChatTurnEvent({
    required this.sequence,
    required this.fromUser,
    required this.text,
    required this.commandId,
    required this.synapse,
    required this.neuronId,
    required this.caller,
    required this.correlationId,
    required this.timestamp,
  });

  final int sequence;
  final bool fromUser;
  final String text;
  final String commandId;
  final String synapse;
  final String neuronId;
  final String caller;
  final String correlationId;
  final DateTime timestamp;

  factory ChatTurnEvent.fromJson(Map<String, Object?> json) {
    return ChatTurnEvent(
      sequence: (json['sequence'] as num).toInt(),
      fromUser: json['fromUser'] as bool,
      text: json['text'] as String,
      commandId: json['commandId'] as String,
      synapse: json['synapse'] as String,
      neuronId: json['neuronId'] as String,
      caller: json['caller'] as String,
      correlationId: json['correlationId'] as String,
      timestamp: DateTime.parse(json['timestamp'] as String).toUtc(),
    );
  }
}

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
    required this.silo,
  });

  final String id;
  final String grainType;
  final String identity;
  final String silo;

  factory BrainNeuron.fromJson(Map<String, Object?> json) {
    return BrainNeuron(
      id: json['id'] as String,
      grainType: json['grainType'] as String,
      identity: json['identity'] as String,
      silo: json['silo'] as String,
    );
  }
}

final class BrainTopologySnapshot {
  const BrainTopologySnapshot({
    required this.modules,
    required this.neurons,
    required this.observedAt,
  });

  final List<BrainModule> modules;
  final List<BrainNeuron> neurons;
  final DateTime observedAt;

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
    );
  }
}
