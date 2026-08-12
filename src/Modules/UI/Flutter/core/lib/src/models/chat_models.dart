final class SendMessageRequest {
  const SendMessageRequest({required this.text});

  final String text;

  Map<String, Object?> toJson() => {'text': text};
}

/// One [ChatResponseUpdate] frame from conversation send SSE (owner commands /messages/stream).
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

final class ChatButtonOffer {
  const ChatButtonOffer({
    required this.buttonId,
    required this.label,
    required this.action,
  });

  final String buttonId;
  final String label;
  final String action;

  factory ChatButtonOffer.fromJson(Map<String, Object?> json) {
    return ChatButtonOffer(
      buttonId: json['buttonId'] as String? ?? '',
      label: json['label'] as String? ?? '',
      action: json['action'] as String? ?? '',
    );
  }
}

final class ChatChartPoint {
  const ChatChartPoint({required this.label, required this.value});

  final String label;
  final num value;

  factory ChatChartPoint.fromJson(Map<String, Object?> json) {
    return ChatChartPoint(
      label: json['label'] as String? ?? '',
      value: json['value'] as num? ?? 0,
    );
  }
}

final class ChatChartOffer {
  const ChatChartOffer({
    required this.title,
    required this.points,
    this.chartKind = 'bar',
  });

  final String title;
  final List<ChatChartPoint> points;
  final String chartKind;

  factory ChatChartOffer.fromJson(Map<String, Object?> json) {
    final raw = json['points'];
    final points = raw is List
        ? raw
              .whereType<Map>()
              .map((e) => ChatChartPoint.fromJson(Map<String, Object?>.from(e)))
              .toList(growable: false)
        : const <ChatChartPoint>[];
    return ChatChartOffer(
      title: json['title'] as String? ?? 'Chart',
      points: points,
      chartKind: json['chartKind'] as String? ?? 'bar',
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
    this.buttons = const [],
    this.charts = const [],
    this.timers = const [],
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
  final List<ChatButtonOffer> buttons;
  final List<ChatChartOffer> charts;
  final List<ChatTimerOffer> timers;

  factory ChatTurnEvent.fromJson(Map<String, Object?> json) {
    final rawButtons = json['buttons'];
    final buttons = rawButtons is List
        ? rawButtons
              .whereType<Map>()
              .map(
                (e) => ChatButtonOffer.fromJson(Map<String, Object?>.from(e)),
              )
              .toList(growable: false)
        : const <ChatButtonOffer>[];
    final rawCharts = json['charts'];
    final charts = rawCharts is List
        ? rawCharts
              .whereType<Map>()
              .map(
                (e) => ChatChartOffer.fromJson(Map<String, Object?>.from(e)),
              )
              .toList(growable: false)
        : const <ChatChartOffer>[];
    final rawTimers = json['timers'];
    final timers = rawTimers is List
        ? rawTimers
              .whereType<Map>()
              .map((e) => ChatTimerOffer.fromJson(Map<String, Object?>.from(e)))
              .toList(growable: false)
        : const <ChatTimerOffer>[];

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
      buttons: buttons,
      charts: charts,
      timers: timers,
    );
  }
}

final class ChatTimerOffer {
  const ChatTimerOffer({required this.label, required this.dueAt});

  final String label;
  final DateTime dueAt;

  factory ChatTimerOffer.fromJson(Map<String, Object?> json) {
    final rawDueAt = json['dueAt'] as String?;
    return ChatTimerOffer(
      label: json['label'] as String? ?? 'Timer',
      dueAt: rawDueAt == null
          ? DateTime.now().toUtc()
          : DateTime.parse(rawDueAt).toUtc(),
    );
  }
}

/// Journal projection of MCP authorization facts from UI-HTTP SSE.
