import 'package:digitalbrain_flutter/core/session/digitalbrain_client.dart';
import 'package:digitalbrain_flutter/features/activity/activity_gateway.dart';
import 'package:digitalbrain_flutter/features/activity/activity_models.dart';
import 'package:digitalbrain_flutter/grpc/ui.pb.dart' as wire;
import 'package:digitalbrain_flutter/grpc/ui.pbenum.dart' as wire_enums;
import 'package:digitalbrain_flutter/runtime/runtime_errors.dart';
import 'package:fixnum/fixnum.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('maps the safe list and exact detail projections', () async {
    final client = _ActivityClient()
      ..listReply = wire.ListActivityReply(runs: [_run()])
      ..runReply = wire.RunReply(run: _run());
    final gateway = GrpcActivityGateway(client: client);

    final runs = await gateway.loadRuns();
    final detail = await gateway.loadRun('run-a');

    expect(client.listRequest?.limit, 200);
    expect(client.listRequest?.hasStatus(), isFalse);
    expect(client.listRequest?.hasOrigin(), isFalse);
    expect(client.listRequest?.hasFeatureId(), isFalse);
    expect(client.runRequest?.runId, 'run-a');
    expect(runs, hasLength(1));
    expect(runs.single.runId, 'run-a');
    expect(runs.single.featureId, 'feature-a');
    expect(runs.single.origin, ActivityOrigin.chat);
    expect(runs.single.status, ActivityStatus.completed);
    expect(runs.single.authority, ActivityAuthority.authorized);
    expect(runs.single.conversationId, 'conversation-a');
    expect(runs.single.requestId, 'request-a');
    expect(runs.single.completedAt, DateTime.utc(2026, 7, 15, 10, 0, 2));
    expect(detail.runId, runs.single.runId);
    expect(detail.releaseDigest, 'a' * 64);
  });

  test('maps every selected list filter into the wire request', () async {
    final client = _ActivityClient();
    final gateway = GrpcActivityGateway(client: client);

    await gateway.loadRuns(
      status: ActivityStatus.waitingForApproval,
      origin: ActivityOrigin.schedule,
      featureId: 'feature-a',
    );

    final request = client.listRequest!;
    expect(request.hasStatus(), isTrue);
    expect(
      request.status,
      wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_WAITING_FOR_APPROVAL,
    );
    expect(request.hasOrigin(), isTrue);
    expect(
      request.origin,
      wire_enums.FeatureRunOrigin.FEATURE_RUN_ORIGIN_SCHEDULE,
    );
    expect(request.hasFeatureId(), isTrue);
    expect(request.featureId, 'feature-a');
    expect(request.limit, 200);
  });

  test(
    'rejects malformed origin coordinates and incomplete status timing',
    () async {
      final malformedOrigin = _run()
        ..originReference = wire.FeatureRunOriginReference(
          conversationId: 'conversation-a',
          automationId: 'schedule-a',
        );
      final malformedTiming = _run()
        ..status = wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_RUNNING
        ..clearStartedAtUnixMs()
        ..clearCompletedAtUnixMs();
      final client = _ActivityClient();
      final gateway = GrpcActivityGateway(client: client);

      client.listReply = wire.ListActivityReply(runs: [malformedOrigin]);
      await expectLater(gateway.loadRuns(), throwsA(isA<ProtocolException>()));
      client.listReply = wire.ListActivityReply(runs: [malformedTiming]);
      await expectLater(gateway.loadRuns(), throwsA(isA<ProtocolException>()));
    },
  );

  test(
    'rejects missing detail and invalid Run identities before sending',
    () async {
      final client = _ActivityClient()..runReply = wire.RunReply();
      final gateway = GrpcActivityGateway(client: client);

      await expectLater(
        gateway.loadRun('run-a'),
        throwsA(isA<ProtocolException>()),
      );
      await expectLater(
        gateway.loadRun(' run-a'),
        throwsA(isA<ArgumentError>()),
      );
    },
  );

  test('accepts a completed Run without a result surface', () async {
    final completed = _run()..clearResultSurfaceReference();
    final client = _ActivityClient()
      ..listReply = wire.ListActivityReply(runs: [completed]);

    final runs = await GrpcActivityGateway(client: client).loadRuns();

    expect(runs.single.status, ActivityStatus.completed);
    expect(runs.single.resultSurfaceReference, isNull);
  });

  test('maps a Run waiting for approval after execution completes', () async {
    final waiting = _run()
      ..status =
          wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_WAITING_FOR_APPROVAL
      ..authorityState = wire_enums
          .FeatureRunAuthorityState
          .FEATURE_RUN_AUTHORITY_STATE_WAITING_FOR_APPROVAL;
    final client = _ActivityClient()
      ..listReply = wire.ListActivityReply(runs: [waiting]);

    final runs = await GrpcActivityGateway(client: client).loadRuns();

    expect(runs.single.status, ActivityStatus.waitingForApproval);
    expect(runs.single.completedAt, isNotNull);
  });

  test('maps a terminal failed Run after an approval is declined', () async {
    final declined = _run()
      ..status = wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_FAILED
      ..clearResultSurfaceReference()
      ..safeFailure = 'The proposed external action was declined.'
      ..failureGuidance = 'Run the Feature again to propose another action.';
    final client = _ActivityClient()
      ..listReply = wire.ListActivityReply(runs: [declined]);

    final runs = await GrpcActivityGateway(client: client).loadRuns();

    expect(runs.single.status, ActivityStatus.failed);
    expect(runs.single.completedAt, isNotNull);
    expect(runs.single.retryAt, isNull);
  });

  test('maps every product status and origin from the wire contract', () async {
    final statusCases = <(
      wire_enums.FeatureRunStatus,
      ActivityStatus,
    )>[
      (
        wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_QUEUED,
        ActivityStatus.queued,
      ),
      (
        wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_RUNNING,
        ActivityStatus.running,
      ),
      (
        wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_WAITING_FOR_APPROVAL,
        ActivityStatus.waitingForApproval,
      ),
      (
        wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_COMPLETED,
        ActivityStatus.completed,
      ),
      (
        wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_FAILED,
        ActivityStatus.failed,
      ),
      (
        wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_PARKED,
        ActivityStatus.parked,
      ),
    ];
    final originCases = <(
      wire_enums.FeatureRunOrigin,
      ActivityOrigin,
    )>[
      (
        wire_enums.FeatureRunOrigin.FEATURE_RUN_ORIGIN_CHAT,
        ActivityOrigin.chat,
      ),
      (
        wire_enums.FeatureRunOrigin.FEATURE_RUN_ORIGIN_DIRECT,
        ActivityOrigin.direct,
      ),
      (
        wire_enums.FeatureRunOrigin.FEATURE_RUN_ORIGIN_SCHEDULE,
        ActivityOrigin.schedule,
      ),
      (
        wire_enums.FeatureRunOrigin.FEATURE_RUN_ORIGIN_EVENT,
        ActivityOrigin.event,
      ),
    ];
    final wireRuns = <wire.FeatureRunSnapshot>[];
    final expected = <(ActivityStatus, ActivityOrigin)>[];
    var sequence = 0;
    for (final (wireStatus, status) in statusCases) {
      for (final (wireOrigin, origin) in originCases) {
        wireRuns.add(
          _runFor(
            runId: 'run-${sequence++}',
            status: wireStatus,
            origin: wireOrigin,
          ),
        );
        expected.add((status, origin));
      }
    }
    final client = _ActivityClient()
      ..listReply = wire.ListActivityReply(runs: wireRuns);

    final runs = await GrpcActivityGateway(client: client).loadRuns();

    expect(runs, hasLength(expected.length));
    for (var index = 0; index < expected.length; index++) {
      expect((runs[index].status, runs[index].origin), expected[index]);
    }
  });
}

wire.FeatureRunSnapshot _runFor({
  required String runId,
  required wire_enums.FeatureRunStatus status,
  required wire_enums.FeatureRunOrigin origin,
}) {
  final value = _run()
    ..runId = runId
    ..status = status
    ..origin = origin;
  if (origin == wire_enums.FeatureRunOrigin.FEATURE_RUN_ORIGIN_CHAT) {
    value.originReference = wire.FeatureRunOriginReference(
      conversationId: 'conversation-a',
      requestId: 'request-a',
    );
  } else if (origin ==
      wire_enums.FeatureRunOrigin.FEATURE_RUN_ORIGIN_DIRECT) {
    value.clearOriginReference();
  } else if (origin ==
          wire_enums.FeatureRunOrigin.FEATURE_RUN_ORIGIN_SCHEDULE ||
      origin == wire_enums.FeatureRunOrigin.FEATURE_RUN_ORIGIN_EVENT) {
    value.originReference = wire.FeatureRunOriginReference(
      automationId: 'automation-a',
    );
  } else {
    throw StateError('Unexpected test origin.');
  }
  value
    ..clearRetryAtUnixMs()
    ..clearSafeFailure()
    ..clearFailureGuidance();
  if (status == wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_QUEUED) {
    value
      ..clearStartedAtUnixMs()
      ..clearCompletedAtUnixMs()
      ..clearResultSurfaceReference()
      ..attempts = 0;
  } else if (status ==
      wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_RUNNING) {
    value
      ..clearCompletedAtUnixMs()
      ..clearResultSurfaceReference();
  } else if (status ==
      wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_WAITING_FOR_APPROVAL) {
    value.authorityState = wire_enums
        .FeatureRunAuthorityState
        .FEATURE_RUN_AUTHORITY_STATE_WAITING_FOR_APPROVAL;
  } else if (status ==
      wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_COMPLETED) {
    return value;
  } else if (status ==
      wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_FAILED) {
    value
      ..clearCompletedAtUnixMs()
      ..clearResultSurfaceReference()
      ..retryAtUnixMs = Int64(1784109660000)
      ..safeFailure = 'The provider was unavailable.'
      ..failureGuidance = 'DigitalBrain will retry automatically.';
  } else if (status ==
      wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_PARKED) {
    value
      ..clearCompletedAtUnixMs()
      ..clearResultSurfaceReference()
      ..safeFailure = 'This Run needs attention.'
      ..failureGuidance = 'Review the Feature before retrying.'
      ..authorityState = wire_enums
          .FeatureRunAuthorityState
          .FEATURE_RUN_AUTHORITY_STATE_PAUSED;
  } else {
    throw StateError('Unexpected test status.');
  }
  return value;
}

wire.FeatureRunSnapshot _run() => wire.FeatureRunSnapshot(
  runId: 'run-a',
  featureId: 'feature-a',
  featureName: 'Research brief',
  installationId: 'installation-a',
  releaseDigest: 'a' * 64,
  inputKind: 'manual',
  origin: wire_enums.FeatureRunOrigin.FEATURE_RUN_ORIGIN_CHAT,
  originReference: wire.FeatureRunOriginReference(
    conversationId: 'conversation-a',
    requestId: 'request-a',
  ),
  status: wire_enums.FeatureRunStatus.FEATURE_RUN_STATUS_COMPLETED,
  authorityState: wire_enums
      .FeatureRunAuthorityState
      .FEATURE_RUN_AUTHORITY_STATE_AUTHORIZED,
  occurredAtUnixMs: Int64(1784109600000),
  startedAtUnixMs: Int64(1784109601000),
  completedAtUnixMs: Int64(1784109602000),
  attempts: 1,
  resultSurfaceReference: 'result-${'b' * 64}',
  traceReference: 'trace-${'c' * 64}',
);

class _ActivityClient implements ActivityClient {
  wire.ListActivityReply listReply = wire.ListActivityReply();
  wire.RunReply runReply = wire.RunReply();
  wire.ListActivityRequest? listRequest;
  wire.GetRunRequest? runRequest;

  @override
  Future<wire.ListActivityReply> listActivity(
    wire.ListActivityRequest request,
  ) async {
    listRequest = request;
    return listReply;
  }

  @override
  Future<wire.RunReply> getRun(wire.GetRunRequest request) async {
    runRequest = request;
    return runReply;
  }
}
