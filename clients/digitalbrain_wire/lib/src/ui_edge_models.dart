// HTTP/SSE DTOs for hosts/DigitalBrain.Ui — host protocol, not module vocabulary.

final class OpenSceneRequest {
  const OpenSceneRequest({required this.sceneKey, required this.title});

  final String sceneKey;
  final String title;

  Map<String, Object?> toJson() => {
        'sceneKey': sceneKey,
        'title': title,
      };
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

/// Projection of a journaled [SceneOpened] fact over SSE.
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

  Map<String, Object?> toJson() => {
        'sequence': sequence,
        'sceneKey': sceneKey,
        'title': title,
        'commandId': commandId,
        'shell': shell,
      };
}
