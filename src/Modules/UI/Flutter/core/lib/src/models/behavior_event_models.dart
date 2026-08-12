final class BehaviorEvent {
  const BehaviorEvent({
    required this.sequence,
    required this.kind,
    required this.behaviorId,
    required this.commandId,
    this.artifactHash,
    this.detail,
    required this.timestamp,
  });

  final int sequence;
  final String kind;
  final String behaviorId;
  final String commandId;
  final String? artifactHash;
  final String? detail;
  final DateTime timestamp;

  factory BehaviorEvent.fromJson(Map<String, Object?> json) {
    return BehaviorEvent(
      sequence: (json['sequence'] as num).toInt(),
      kind: json['kind'] as String,
      behaviorId: json['behaviorId'] as String,
      commandId: json['commandId'] as String,
      artifactHash: json['artifactHash'] as String?,
      detail: json['detail'] as String?,
      timestamp: DateTime.parse(json['timestamp'] as String).toUtc(),
    );
  }
}

/// Module-owned user action projected for Flutter cards — never secrets.

final class UserActionRequiredEvent {
  const UserActionRequiredEvent({
    required this.sequence,
    required this.taskId,
    required this.attemptId,
    required this.moduleId,
    required this.displayText,
    required this.actionUrl,
    required this.expiresAt,
    required this.timestamp,
  });

  final int sequence;
  final String taskId;
  final String attemptId;
  final String moduleId;
  final String displayText;
  final Uri actionUrl;
  final DateTime expiresAt;
  final DateTime timestamp;

  factory UserActionRequiredEvent.fromJson(Map<String, Object?> json) {
    return UserActionRequiredEvent(
      sequence: (json['sequence'] as num).toInt(),
      taskId: json['taskId'] as String,
      attemptId: json['attemptId'] as String,
      moduleId: json['moduleId'] as String,
      displayText: json['displayText'] as String,
      actionUrl: Uri.parse(json['actionUrl'] as String),
      expiresAt: DateTime.parse(json['expiresAt'] as String).toUtc(),
      timestamp: DateTime.parse(json['timestamp'] as String).toUtc(),
    );
  }
}
