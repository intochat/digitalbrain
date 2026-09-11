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

final class ChatSpreadsheetOffer {
  const ChatSpreadsheetOffer({
    required this.title,
    required this.columns,
    required this.rows,
    this.sheetName = 'Sheet1',
  });

  final String title;
  final String sheetName;
  final List<String> columns;
  final List<List<String>> rows;

  factory ChatSpreadsheetOffer.fromJson(Map<String, Object?> json) {
    final rawColumns = json['columns'];
    final columns = rawColumns is List
        ? rawColumns.map((e) => e.toString()).toList(growable: false)
        : const <String>[];
    final rawRows = json['rows'];
    final rows = rawRows is List
        ? rawRows
              .map((row) {
                if (row is List) {
                  return row.map((cell) => cell.toString()).toList();
                }
                if (row is Map) {
                  final cells = row['cells'];
                  if (cells is List) {
                    return cells.map((cell) => cell.toString()).toList();
                  }
                }
                return const <String>[];
              })
              .toList(growable: false)
        : const <List<String>>[];
    return ChatSpreadsheetOffer(
      title: json['title'] as String? ?? 'Sheet',
      sheetName: json['sheetName'] as String? ?? 'Sheet1',
      columns: columns,
      rows: rows,
    );
  }
}

final class ChatGraphNode {
  const ChatGraphNode({
    required this.id,
    required this.label,
    this.kind = 'leaf',
    this.cluster,
  });

  final String id;
  final String label;
  final String kind;
  final String? cluster;

  factory ChatGraphNode.fromJson(Map<String, Object?> json) => ChatGraphNode(
    id: json['id'] as String? ?? '',
    label: json['label'] as String? ?? '',
    kind: json['kind'] as String? ?? 'leaf',
    cluster: json['cluster'] as String?,
  );
}

final class ChatGraphEdge {
  const ChatGraphEdge({
    required this.id,
    required this.sourceId,
    required this.targetId,
    this.dotted = false,
  });

  final String id;
  final String sourceId;
  final String targetId;
  final bool dotted;

  factory ChatGraphEdge.fromJson(Map<String, Object?> json) => ChatGraphEdge(
    id: json['id'] as String? ?? '',
    sourceId: json['sourceId'] as String? ?? '',
    targetId: json['targetId'] as String? ?? '',
    dotted: json['dotted'] as bool? ?? false,
  );
}

/// State of a named graph kit entity, read from /kit/graphs/{name}.
final class ChatGraphOffer {
  const ChatGraphOffer({
    required this.title,
    required this.nodes,
    required this.edges,
  });

  final String title;
  final List<ChatGraphNode> nodes;
  final List<ChatGraphEdge> edges;

  factory ChatGraphOffer.fromJson(Map<String, Object?> json) {
    final rawNodes = json['nodes'];
    final rawEdges = json['edges'];
    return ChatGraphOffer(
      title: json['title'] as String? ?? '',
      nodes: rawNodes is List
          ? rawNodes
                .whereType<Map>()
                .map(
                  (e) => ChatGraphNode.fromJson(Map<String, Object?>.from(e)),
                )
                .toList(growable: false)
          : const <ChatGraphNode>[],
      edges: rawEdges is List
          ? rawEdges
                .whereType<Map>()
                .map(
                  (e) => ChatGraphEdge.fromJson(Map<String, Object?>.from(e)),
                )
                .toList(growable: false)
          : const <ChatGraphEdge>[],
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

final class KitCardRef {
  const KitCardRef({
    required this.kind,
    required this.name,
    required this.caption,
  });

  final String kind;
  final String name;
  final String caption;

  factory KitCardRef.fromJson(Map<String, Object?> json) => KitCardRef(
    kind: json['kind'] as String? ?? '',
    name: json['name'] as String? ?? '',
    caption: json['caption'] as String? ?? '',
  );
}

final class ChatSendReceipt {
  const ChatSendReceipt({required this.turnId, this.commandId});

  final String turnId;
  final String? commandId;
}

final class ChatTurnEvent {
  const ChatTurnEvent({
    required this.sequence,
    required this.fromUser,
    required this.text,
    required this.commandId,
    required this.signal,
    required this.neuronId,
    required this.correlationId,
    required this.timestamp,
    this.cards = const [],
    this.turnId,
    this.status,
    this.eventId,
  });

  final int sequence;
  final bool fromUser;
  final String text;
  final String commandId;
  final String signal;
  final String neuronId;
  final String correlationId;
  final DateTime timestamp;
  final List<KitCardRef> cards;
  final String? turnId;
  final String? status;
  final String? eventId;

  // One ChatTurnSnapshot is an exchange: the user's turn, plus the answer or
  // the failure once it settles. Routed through fromJson so cards and
  // timestamps are parsed in exactly one place.
  static List<ChatTurnEvent> replayTurns(List<Object?> snapshots) {
    final events = <Map<String, Object?>>[];
    for (final snapshot in snapshots) {
      final turn = snapshot as Map;
      final turnId = (turn['turn'] as Map)['value'] as String;
      final status = turn['status'] as String;
      final shared = <String, Object?>{
        'turnId': turnId,
        'commandId': (turn['commandId'] as Map)['value'] as String,
        'status': status,
        'neuronId': '',
        'correlationId': '',
      };
      events.add({
        ...shared,
        'fromUser': true,
        'signal': 'TurnAccepted',
        'text': turn['text'],
        'timestamp': turn['startedAt'],
        'eventId': 'turn:$turnId:sent',
      });
      final answer = turn['answer'];
      if (answer != null) {
        events.add({
          ...shared,
          'fromUser': false,
          'signal': 'Responded',
          'text': answer,
          'timestamp': turn['settledAt'] ?? turn['startedAt'],
          'cards': turn['cards'],
          'eventId': 'turn:$turnId:answer',
        });
      } else if (status == 'Failed' || status == 'Cancelled') {
        events.add({
          ...shared,
          'fromUser': false,
          'signal': 'TurnFailed',
          'text': turn['detail'] ?? '',
          'timestamp': turn['settledAt'] ?? turn['startedAt'],
          'eventId': 'turn:$turnId:settled',
        });
      }
    }
    return List.generate(
      events.length,
      (index) => ChatTurnEvent.fromJson({
        ...events[index],
        'sequence': index - events.length,
      }),
      growable: false,
    );
  }

  factory ChatTurnEvent.fromJson(Map<String, Object?> json) {
    final rawCards = json['cards'];
    final cards = rawCards is List
        ? rawCards
              .whereType<Map>()
              .map((e) => KitCardRef.fromJson(Map<String, Object?>.from(e)))
              .toList(growable: false)
        : const <KitCardRef>[];

    return ChatTurnEvent(
      sequence: (json['sequence'] as num).toInt(),
      fromUser: json['fromUser'] as bool,
      text: json['text'] as String,
      commandId: json['commandId'] as String,
      signal: json['signal'] as String,
      neuronId: json['neuronId'] as String,
      correlationId: json['correlationId'] as String,
      timestamp: DateTime.parse(json['timestamp'] as String).toUtc(),
      cards: cards,
      turnId: json['turnId'] as String?,
      status: json['status'] as String?,
      eventId: json['eventId'] as String?,
    );
  }
}

sealed class ChatStreamEvent {
  const ChatStreamEvent();
}

final class ChatTurnObserved extends ChatStreamEvent {
  const ChatTurnObserved({required this.turn});

  final ChatTurnEvent turn;
}

final class ChatJournalReset extends ChatStreamEvent {
  const ChatJournalReset({required this.cursor, required this.turns});

  final int cursor;
  final List<ChatTurnEvent> turns;
}

/// State of a named surface kit entity, read from /kit/surfaces/{name}.
final class KitSurfaceState {
  const KitSurfaceState({required this.scenes});

  final List<KitSurfaceScene> scenes;

  factory KitSurfaceState.fromJson(Map<String, Object?> json) {
    final raw = json['scenes'];
    return KitSurfaceState(
      scenes: raw is List
          ? raw
                .whereType<Map>()
                .map(
                  (e) => KitSurfaceScene.fromJson(Map<String, Object?>.from(e)),
                )
                .toList(growable: false)
          : const <KitSurfaceScene>[],
    );
  }
}

final class KitSurfaceScene {
  const KitSurfaceScene({
    required this.surfaceKey,
    required this.title,
    this.root,
  });

  final String surfaceKey;
  final String title;
  final SurfaceComponent? root;

  factory KitSurfaceScene.fromJson(Map<String, Object?> json) =>
      KitSurfaceScene(
        surfaceKey: json['surfaceKey'] as String? ?? '',
        title: json['title'] as String? ?? '',
        root: json['root'] is Map
            ? SurfaceComponent.fromJson(
                Map<String, Object?>.from(json['root'] as Map),
              )
            : null,
      );
}

/// A persistent component tree authored by a UI script, never a client route.
final class SurfaceComponent {
  const SurfaceComponent({
    required this.kind,
    this.key,
    this.properties = const {},
    this.children = const [],
  });
  final String kind;
  final String? key;
  final Map<String, String> properties;
  final List<SurfaceComponent> children;

  factory SurfaceComponent.fromJson(Map<String, Object?> json) =>
      SurfaceComponent(
        kind: json['kind'] as String? ?? '',
        key: json['key'] as String?,
        properties: json['properties'] is Map
            ? {
                for (final entry in (json['properties'] as Map).entries)
                  if (entry.value != null)
                    entry.key as String: entry.value.toString(),
              }
            : const {},
        children: (json['children'] as List? ?? const [])
            .whereType<Map>()
            .map(
              (child) =>
                  SurfaceComponent.fromJson(Map<String, Object?>.from(child)),
            )
            .toList(growable: false),
      );
}
