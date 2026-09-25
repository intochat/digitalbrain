/// Metadata-only observations from the product's bounded activity feed.
final class ActivityRecord {
  const ActivityRecord({
    required this.id,
    required this.operationId,
    required this.sequence,
    required this.at,
    required this.kind,
    required this.type,
    required this.status,
    this.correlationId,
    this.sourceId,
    this.targetId,
    this.durationMs,
    this.failureCode,
  });

  final String id, operationId, kind, type, status;
  final String? correlationId, sourceId, targetId, failureCode;
  final int sequence;
  final DateTime at;
  final double? durationMs;

  static const _kinds = [
    'CallStarted',
    'CallArrived',
    'CallCompleted',
    'CallFailed',
    'SignalPublished',
  ];

  factory ActivityRecord.fromJson(Map<String, dynamic> json) {
    final rawKind = json['kind'];
    return ActivityRecord(
      id: json['id'] as String,
      operationId: json['operationId'] as String,
      sequence: (json['sequence'] as num).toInt(),
      at: DateTime.parse(json['at'] as String),
      kind: rawKind is int && rawKind >= 0 && rawKind < _kinds.length
          ? _kinds[rawKind]
          : '$rawKind',
      type: json['type'] as String,
      status: json['status'] as String,
      correlationId: json['correlationId'] as String?,
      sourceId: json['sourceId'] as String?,
      targetId: json['targetId'] as String?,
      durationMs: (json['durationMs'] as num?)?.toDouble(),
      failureCode: json['failureCode'] as String?,
    );
  }
}

final class ActivitySnapshot {
  const ActivitySnapshot({
    required this.events,
    required this.nextSequence,
    required this.gap,
    required this.observedAt,
  });
  final List<ActivityRecord> events;
  final int nextSequence;
  final bool gap;
  final DateTime observedAt;

  factory ActivitySnapshot.fromJson(Map<String, dynamic> json) =>
      ActivitySnapshot(
        events: [
          for (final item in json['events'] as List? ?? const [])
            ActivityRecord.fromJson(Map<String, dynamic>.from(item as Map)),
        ],
        nextSequence: (json['nextSequence'] as num).toInt(),
        gap: json['gap'] == true,
        observedAt: DateTime.parse(json['observedAt'] as String),
      );
}

sealed class ActivityUpdate {
  const ActivityUpdate();
}

final class ActivityItem extends ActivityUpdate {
  const ActivityItem(this.event);
  final ActivityRecord event;
}

final class ActivityGap extends ActivityUpdate {
  const ActivityGap(this.snapshot);
  final ActivitySnapshot snapshot;
}

final class ActivityDisconnected extends ActivityUpdate {
  const ActivityDisconnected();
}
