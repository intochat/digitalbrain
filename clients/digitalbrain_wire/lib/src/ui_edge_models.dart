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
  });

  final int sequence;
  final bool fromUser;
  final String text;
  final String commandId;

  factory ChatTurnEvent.fromJson(Map<String, Object?> json) {
    return ChatTurnEvent(
      sequence: (json['sequence'] as num).toInt(),
      fromUser: json['fromUser'] as bool,
      text: json['text'] as String,
      commandId: json['commandId'] as String,
    );
  }
}
