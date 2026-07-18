import 'dart:async';

import 'package:digitalbrain_flutter/features/activity/activity_gateway.dart';
import 'package:digitalbrain_flutter/features/activity/activity_models.dart';

ActivityRun activityRun({
  String runId = 'run-001',
  String featureId = 'feature-research',
  String featureName = 'Research brief',
  String installationId = 'installation-research',
  String? releaseDigest,
  ActivityOrigin origin = ActivityOrigin.chat,
  ActivityStatus status = ActivityStatus.completed,
  ActivityAuthority authority = ActivityAuthority.authorized,
  DateTime? occurredAt,
  DateTime? startedAt,
  DateTime? completedAt,
  DateTime? retryAt,
  int attempts = 1,
  String? resultSurfaceReference = 'surface://result/run-001',
  String? safeFailure,
  String? failureGuidance,
  String? conversationId = 'conversation-001',
  String? requestId = 'request-001',
  String? automationId,
  String inputKind = 'chat.request',
  String traceReference = 'trace-001',
}) {
  final occurred = occurredAt ?? DateTime.utc(2026, 7, 15, 10);
  final started = startedAt ?? occurred.add(const Duration(seconds: 2));
  final terminal = switch (status) {
    ActivityStatus.completed || ActivityStatus.waitingForApproval =>
      completedAt ?? started.add(const Duration(seconds: 18)),
    _ => completedAt,
  };
  return ActivityRun(
    runId: runId,
    featureId: featureId,
    featureName: featureName,
    installationId: installationId,
    releaseDigest: releaseDigest ?? 'a' * 64,
    origin: origin,
    status: status,
    authority: authority,
    occurredAt: occurred,
    startedAt: status == ActivityStatus.queued ? null : started,
    completedAt: terminal,
    retryAt: retryAt,
    attempts: attempts,
    resultSurfaceReference: resultSurfaceReference,
    safeFailure: safeFailure,
    failureGuidance: failureGuidance,
    conversationId: conversationId,
    requestId: requestId,
    automationId: automationId,
    inputKind: inputKind,
    traceReference: traceReference,
  );
}

class QueueActivityGateway implements ActivityGateway {
  final List<Completer<List<ActivityRun>>> requests = [];
  final List<ActivityGatewayCall> queries = [];

  @override
  Future<List<ActivityRun>> loadRuns({
    ActivityStatus? status,
    ActivityOrigin? origin,
    String? featureId,
  }) {
    queries.add(
      ActivityGatewayCall(status: status, origin: origin, featureId: featureId),
    );
    final request = Completer<List<ActivityRun>>();
    requests.add(request);
    return request.future;
  }
}

class ImmediateActivityGateway implements ActivityGateway {
  ImmediateActivityGateway(this.runs);

  List<ActivityRun> runs;
  Object? error;
  int calls = 0;
  final List<ActivityGatewayCall> queries = [];

  @override
  Future<List<ActivityRun>> loadRuns({
    ActivityStatus? status,
    ActivityOrigin? origin,
    String? featureId,
  }) async {
    calls++;
    queries.add(
      ActivityGatewayCall(status: status, origin: origin, featureId: featureId),
    );
    if (error case final failure?) throw failure;
    return runs;
  }
}

class ActivityGatewayCall {
  const ActivityGatewayCall({
    required this.status,
    required this.origin,
    required this.featureId,
  });

  final ActivityStatus? status;
  final ActivityOrigin? origin;
  final String? featureId;
}
