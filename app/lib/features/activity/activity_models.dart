enum ActivityOrigin {
  chat('Chat'),
  direct('Direct'),
  schedule('Schedule'),
  event('Event');

  const ActivityOrigin(this.label);

  final String label;
}

enum ActivityStatus {
  queued('Queued'),
  running('Running'),
  waitingForApproval('Waiting for approval'),
  completed('Completed'),
  failed('Failed'),
  parked('Parked');

  const ActivityStatus(this.label);

  final String label;
}

enum ActivityAuthority {
  authorized('Authorized'),
  waitingForApproval('Waiting for approval'),
  paused('Paused');

  const ActivityAuthority(this.label);

  final String label;
}

class ActivityRun {
  ActivityRun({
    required this.runId,
    required this.featureId,
    required this.featureName,
    required this.installationId,
    required this.releaseDigest,
    required this.origin,
    required this.status,
    required this.authority,
    required this.occurredAt,
    required this.startedAt,
    required this.completedAt,
    required this.retryAt,
    required this.attempts,
    required this.resultSurfaceReference,
    required this.safeFailure,
    required this.failureGuidance,
    required this.conversationId,
    required this.requestId,
    required this.automationId,
    required this.inputKind,
    required this.traceReference,
  }) {
    _requireIdentity(runId, 'runId', 256);
    _requireIdentity(featureId, 'featureId', 128);
    _requireText(featureName, 'featureName', 4096);
    _requireIdentity(installationId, 'installationId', 256);
    if (!_digestPattern.hasMatch(releaseDigest)) {
      throw ArgumentError.value(
        releaseDigest,
        'releaseDigest',
        'Invalid Feature release digest.',
      );
    }
    _requireUtc(occurredAt, 'occurredAt');
    _requireOptionalUtc(startedAt, 'startedAt');
    _requireOptionalUtc(completedAt, 'completedAt');
    _requireOptionalUtc(retryAt, 'retryAt');
    if (attempts < 0) {
      throw ArgumentError.value(attempts, 'attempts', 'Invalid attempt count.');
    }
    _requireOptionalIdentity(
      resultSurfaceReference,
      'resultSurfaceReference',
      512,
    );
    _requireOptionalText(safeFailure, 'safeFailure', 1000);
    _requireOptionalText(failureGuidance, 'failureGuidance', 2000);
    _requireOptionalIdentity(conversationId, 'conversationId', 256);
    _requireOptionalIdentity(requestId, 'requestId', 256);
    _requireOptionalIdentity(automationId, 'automationId', 256);
    _requireIdentity(inputKind, 'inputKind', 128);
    _requireIdentity(traceReference, 'traceReference', 512);
    if (startedAt != null && startedAt!.isBefore(occurredAt) ||
        completedAt != null && completedAt!.isBefore(startedAt ?? occurredAt) ||
        retryAt != null && retryAt!.isBefore(occurredAt) ||
        completedAt != null &&
            status != ActivityStatus.completed &&
            status != ActivityStatus.waitingForApproval &&
            status != ActivityStatus.failed) {
      throw ArgumentError('Invalid Activity Run timing.');
    }
  }

  final String runId;
  final String featureId;
  final String featureName;
  final String installationId;
  final String releaseDigest;
  final ActivityOrigin origin;
  final ActivityStatus status;
  final ActivityAuthority authority;
  final DateTime occurredAt;
  final DateTime? startedAt;
  final DateTime? completedAt;
  final DateTime? retryAt;
  final int attempts;
  final String? resultSurfaceReference;
  final String? safeFailure;
  final String? failureGuidance;
  final String? conversationId;
  final String? requestId;
  final String? automationId;
  final String inputKind;
  final String traceReference;

  Duration? get elapsed {
    final completed = completedAt;
    if (completed == null) return null;
    return completed.difference(startedAt ?? occurredAt);
  }
}

class ActivityFeature {
  const ActivityFeature({required this.id, required this.name});

  final String id;
  final String name;
}

final RegExp _digestPattern = RegExp(r'^[0-9a-f]{64}$');

void _requireIdentity(String value, String name, int maximumLength) {
  if (!_isBoundedSafeText(value, maximumLength)) {
    throw ArgumentError.value(value, name, 'Invalid identity.');
  }
}

void _requireText(String value, String name, int maximumLength) {
  if (!_isBoundedSafeText(value, maximumLength)) {
    throw ArgumentError.value(value, name, 'Invalid text.');
  }
}

void _requireOptionalIdentity(String? value, String name, int maximumLength) {
  if (value != null) _requireIdentity(value, name, maximumLength);
}

void _requireOptionalText(String? value, String name, int maximumLength) {
  if (value != null) _requireText(value, name, maximumLength);
}

bool _isBoundedSafeText(String value, int maximumLength) =>
    value.isNotEmpty &&
    value.length <= maximumLength &&
    value.trim() == value &&
    !value.runes.any(
      (character) => character < 32 || character >= 127 && character <= 159,
    );

void _requireUtc(DateTime value, String name) {
  if (!value.isUtc) {
    throw ArgumentError.value(value, name, 'UTC timestamp required.');
  }
}

void _requireOptionalUtc(DateTime? value, String name) {
  if (value != null) _requireUtc(value, name);
}
