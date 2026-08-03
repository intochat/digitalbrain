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

/// One [ChatResponseUpdate] frame from POST /chats/{name}/messages/stream.
///
/// Unknown content `$type` values are retained as [ChatDeltaPart] with raw
/// fields so older clients do not crash when the edge starts emitting data/uri.
final class ChatDelta {
  const ChatDelta({required this.role, required this.contents});

  final String? role;
  final List<ChatDeltaPart> contents;

  String get text => contents
      .map((part) => part.text ?? '')
      .where((value) => value.isNotEmpty)
      .join();

  factory ChatDelta.fromJson(Map<String, Object?> json) {
    final rawContents = json['contents'];
    final contents = rawContents is List
        ? rawContents
              .whereType<Map>()
              .map(
                (part) =>
                    ChatDeltaPart.fromJson(Map<String, Object?>.from(part)),
              )
              .toList(growable: false)
        : const <ChatDeltaPart>[];

    return ChatDelta(
      role: json['role'] as String?,
      contents: contents,
    );
  }
}

final class ChatDeltaPart {
  const ChatDeltaPart({
    required this.type,
    this.text,
    required this.raw,
  });

  final String type;
  final String? text;
  final Map<String, Object?> raw;

  bool get isText => type == 'text';

  factory ChatDeltaPart.fromJson(Map<String, Object?> json) {
    final type = json[r'$type'] as String? ?? 'unknown';
    return ChatDeltaPart(
      type: type,
      text: json['text'] as String?,
      raw: Map<String, Object?>.from(json),
    );
  }
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

/// Journal projection of MCP authorization facts from UI-HTTP SSE.
final class AuthorizationEvent {
  const AuthorizationEvent({
    required this.sequence,
    required this.kind,
    required this.commandId,
    required this.serverKey,
    this.serverDisplayName,
    this.signInUrl,
    required this.state,
    required this.timestamp,
  });

  final int sequence;
  final String kind;
  final String commandId;
  final String serverKey;
  final String? serverDisplayName;
  final String? signInUrl;
  final String state;
  final DateTime timestamp;

  bool get isRequired => kind == 'AuthorizationRequired';
  bool get isCompleted => kind == 'AuthorizationCompleted';
  bool get isDenied => kind == 'AuthorizationDenied';
  bool get isResolved => isCompleted || isDenied;

  factory AuthorizationEvent.fromJson(Map<String, Object?> json) {
    return AuthorizationEvent(
      sequence: (json['sequence'] as num).toInt(),
      kind: json['kind'] as String,
      commandId: json['commandId'] as String,
      serverKey: json['serverKey'] as String,
      serverDisplayName: json['serverDisplayName'] as String?,
      signInUrl: json['signInUrl'] as String?,
      state: json['state'] as String,
      timestamp: DateTime.parse(json['timestamp'] as String).toUtc(),
    );
  }
}

/// Pending sign-in cards rebuilt from authorization journal facts.
final class SignInCardProjection {
  const SignInCardProjection({
    required this.state,
    required this.commandId,
    required this.serverKey,
    required this.serverDisplayName,
    required this.signInUrl,
  });

  final String state;
  final String commandId;
  final String serverKey;
  final String serverDisplayName;
  final Uri signInUrl;

  static List<SignInCardProjection> project(
    Iterable<AuthorizationEvent> events,
  ) {
    final open = <String, SignInCardProjection>{};
    final ordered = events.toList()
      ..sort((a, b) => a.sequence.compareTo(b.sequence));

    for (final event in ordered) {
      if (event.isRequired) {
        final url = event.signInUrl;
        final name = event.serverDisplayName;
        if (url == null || name == null || name.isEmpty) {
          continue;
        }
        open[event.state] = SignInCardProjection(
          state: event.state,
          commandId: event.commandId,
          serverKey: event.serverKey,
          serverDisplayName: name,
          signInUrl: Uri.parse(url),
        );
        continue;
      }

      if (event.isResolved) {
        open.remove(event.state);
      }
    }

    return List<SignInCardProjection>.unmodifiable(open.values);
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
