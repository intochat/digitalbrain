import '../../core/session/digitalbrain_client.dart';
import '../../grpc/ui.pb.dart' as wire;
import '../../grpc/ui.pbenum.dart' as wire_enums;
import '../../runtime/runtime_errors.dart';
import 'activity_models.dart';

abstract interface class ActivityGateway {
  Future<List<ActivityRun>> loadRuns({
    ActivityStatus? status,
    ActivityOrigin? origin,
    String? featureId,
  });
}

abstract interface class ActivityRunGateway {
  Future<ActivityRun> loadRun(String runId);
}

class GrpcActivityGateway implements ActivityGateway, ActivityRunGateway {
  const GrpcActivityGateway({required ActivityClient client})
    : _client = client;

  final ActivityClient _client;

  @override
  Future<List<ActivityRun>> loadRuns({
    ActivityStatus? status,
    ActivityOrigin? origin,
    String? featureId,
  }) async {
    if (featureId case final value?) {
      _requireIdentity(value, 'featureId', 128);
    }
    final reply = await _client.listActivity(
      wire.ListActivityRequest(
        status: _wireStatus(status),
        origin: _wireOrigin(origin),
        featureId: featureId,
        limit: 200,
      ),
    );
    try {
      return List.unmodifiable(reply.runs.map(_mapRun));
    } on ProtocolException {
      rethrow;
    } on Object {
      throw const ProtocolException('Activity response could not be verified.');
    }
  }

  @override
  Future<ActivityRun> loadRun(String runId) async {
    _requireIdentity(runId, 'runId', 256);
    final reply = await _client.getRun(wire.GetRunRequest(runId: runId));
    try {
      if (!reply.hasRun() || reply.run.runId != runId) {
        throw const ProtocolException('Run response is incomplete.');
      }
      return _mapRun(reply.run);
    } on ProtocolException {
      rethrow;
    } on Object {
      throw const ProtocolException('Run response could not be verified.');
    }
  }
}

wire_enums.FeatureRunStatus? _wireStatus(ActivityStatus? value) =>
    switch (value) {
      null => null,
      ActivityStatus.queued =>
        wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_QUEUED,
      ActivityStatus.running =>
        wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_RUNNING,
      ActivityStatus.waitingForApproval =>
        wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_WAITING_FOR_APPROVAL,
      ActivityStatus.completed =>
        wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_COMPLETED,
      ActivityStatus.failed =>
        wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_FAILED,
      ActivityStatus.parked =>
        wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_PARKED,
    };

wire_enums.FeatureRunOrigin? _wireOrigin(
  ActivityOrigin? value,
) => switch (value) {
  null => null,
  ActivityOrigin.chat => wire_enums.FeatureRunOrigin.FEATURE_RUN_ORIGIN_CHAT,
  ActivityOrigin.direct =>
    wire_enums.FeatureRunOrigin.FEATURE_RUN_ORIGIN_DIRECT,
  ActivityOrigin.schedule =>
    wire_enums.FeatureRunOrigin.FEATURE_RUN_ORIGIN_SCHEDULE,
  ActivityOrigin.event => wire_enums.FeatureRunOrigin.FEATURE_RUN_ORIGIN_EVENT,
};

ActivityRun _mapRun(wire.FeatureRunSnapshot value) {
  try {
    final origin = switch (value.origin) {
      wire_enums.FeatureRunOrigin.FEATURE_RUN_ORIGIN_CHAT =>
        ActivityOrigin.chat,
      wire_enums.FeatureRunOrigin.FEATURE_RUN_ORIGIN_DIRECT =>
        ActivityOrigin.direct,
      wire_enums.FeatureRunOrigin.FEATURE_RUN_ORIGIN_SCHEDULE =>
        ActivityOrigin.schedule,
      wire_enums.FeatureRunOrigin.FEATURE_RUN_ORIGIN_EVENT =>
        ActivityOrigin.event,
      _ => throw const ProtocolException('Run origin is invalid.'),
    };
    final status = switch (value.status) {
      wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_QUEUED =>
        ActivityStatus.queued,
      wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_RUNNING =>
        ActivityStatus.running,
      wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_WAITING_FOR_APPROVAL =>
        ActivityStatus.waitingForApproval,
      wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_COMPLETED =>
        ActivityStatus.completed,
      wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_FAILED =>
        ActivityStatus.failed,
      wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_PARKED =>
        ActivityStatus.parked,
      _ => throw const ProtocolException('Run status is invalid.'),
    };
    final authority = switch (value.authorityState) {
      wire_enums
          .FeatureRunAuthorityState
          .FEATURE_RUN_AUTHORITY_STATE_AUTHORIZED =>
        ActivityAuthority.authorized,
      wire_enums
          .FeatureRunAuthorityState
          .FEATURE_RUN_AUTHORITY_STATE_WAITING_FOR_APPROVAL =>
        ActivityAuthority.waitingForApproval,
      wire_enums.FeatureRunAuthorityState.FEATURE_RUN_AUTHORITY_STATE_PAUSED =>
        ActivityAuthority.paused,
      _ => throw const ProtocolException('Run authority state is invalid.'),
    };
    final reference = value.hasOriginReference() ? value.originReference : null;
    final conversationId = reference?.hasConversationId() == true
        ? reference!.conversationId
        : null;
    final requestId = reference?.hasRequestId() == true
        ? reference!.requestId
        : null;
    final automationId = reference?.hasAutomationId() == true
        ? reference!.automationId
        : null;
    _validateOriginReference(
      origin,
      conversationId: conversationId,
      requestId: requestId,
      automationId: automationId,
    );
    final occurredAt = _timestamp(value.occurredAtUnixMs, 'occurredAt');
    final startedAt = value.hasStartedAtUnixMs()
        ? _timestamp(value.startedAtUnixMs, 'startedAt')
        : null;
    final completedAt = value.hasCompletedAtUnixMs()
        ? _timestamp(value.completedAtUnixMs, 'completedAt')
        : null;
    final retryAt = value.hasRetryAtUnixMs()
        ? _timestamp(value.retryAtUnixMs, 'retryAt')
        : null;
    final resultReference = value.hasResultSurfaceReference()
        ? value.resultSurfaceReference
        : null;
    final safeFailure = value.hasSafeFailure() ? value.safeFailure : null;
    final guidance = value.hasFailureGuidance() ? value.failureGuidance : null;
    _validateStatus(
      status,
      authority: authority,
      startedAt: startedAt,
      completedAt: completedAt,
      retryAt: retryAt,
      attempts: value.attempts,
      resultReference: resultReference,
      safeFailure: safeFailure,
      guidance: guidance,
    );
    return ActivityRun(
      runId: value.runId,
      featureId: value.featureId,
      featureName: value.featureName,
      installationId: value.installationId,
      releaseDigest: value.releaseDigest,
      origin: origin,
      status: status,
      authority: authority,
      occurredAt: occurredAt,
      startedAt: startedAt,
      completedAt: completedAt,
      retryAt: retryAt,
      attempts: value.attempts,
      resultSurfaceReference: resultReference,
      safeFailure: safeFailure,
      failureGuidance: guidance,
      conversationId: conversationId,
      requestId: requestId,
      automationId: automationId,
      inputKind: value.inputKind,
      traceReference: value.traceReference,
    );
  } on ProtocolException {
    rethrow;
  } on Object {
    throw const ProtocolException('Run response could not be verified.');
  }
}

void _validateOriginReference(
  ActivityOrigin origin, {
  required String? conversationId,
  required String? requestId,
  required String? automationId,
}) {
  final valid = switch (origin) {
    ActivityOrigin.chat =>
      conversationId != null && requestId != null && automationId == null,
    ActivityOrigin.direct =>
      conversationId == null && requestId == null && automationId == null,
    ActivityOrigin.schedule || ActivityOrigin.event =>
      conversationId == null && requestId == null && automationId != null,
  };
  if (!valid) throw const ProtocolException('Run origin link is invalid.');
}

void _validateStatus(
  ActivityStatus status, {
  required ActivityAuthority authority,
  required DateTime? startedAt,
  required DateTime? completedAt,
  required DateTime? retryAt,
  required int attempts,
  required String? resultReference,
  required String? safeFailure,
  required String? guidance,
}) {
  final hasFailure = safeFailure != null && guidance != null;
  final noFailure = safeFailure == null && guidance == null;
  final valid = switch (status) {
    ActivityStatus.queued =>
      completedAt == null && retryAt == null && noFailure,
    ActivityStatus.running =>
      startedAt != null &&
          completedAt == null &&
          retryAt == null &&
          attempts > 0 &&
          noFailure,
    ActivityStatus.waitingForApproval =>
      startedAt != null &&
          completedAt != null &&
          retryAt == null &&
          attempts > 0 &&
          noFailure &&
          authority == ActivityAuthority.waitingForApproval,
    ActivityStatus.completed =>
      startedAt != null &&
          completedAt != null &&
          retryAt == null &&
          attempts > 0 &&
          noFailure &&
          authority == ActivityAuthority.authorized,
    ActivityStatus.failed =>
      startedAt != null &&
          (completedAt == null && retryAt != null ||
              completedAt != null && retryAt == null) &&
          attempts > 0 &&
          resultReference == null &&
          hasFailure,
    ActivityStatus.parked =>
      startedAt != null &&
          completedAt == null &&
          retryAt == null &&
          attempts > 0 &&
          resultReference == null &&
          hasFailure &&
          authority == ActivityAuthority.paused,
  };
  if (!valid) throw const ProtocolException('Run status detail is invalid.');
}

DateTime _timestamp(Object value, String name) {
  final milliseconds = switch (value) {
    int integer => integer,
    _ => int.parse(value.toString()),
  };
  if (milliseconds <= 0) {
    throw ProtocolException('Run $name timestamp is invalid.');
  }
  return DateTime.fromMillisecondsSinceEpoch(milliseconds, isUtc: true);
}

void _requireIdentity(String value, String name, int maximumLength) {
  if (value.isEmpty ||
      value.length > maximumLength ||
      value.trim() != value ||
      value.runes.any(
        (character) => character < 32 || character >= 127 && character <= 159,
      )) {
    throw ArgumentError.value(value, name, 'Invalid identity.');
  }
}
