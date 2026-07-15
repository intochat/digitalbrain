import 'package:digitalbrain_flutter/features/activity/activity_models.dart';
import 'package:flutter_test/flutter_test.dart';

import 'activity_test_fixtures.dart';

void main() {
  group('ActivityRun', () {
    test(
      'keeps the immutable Feature release identity and safe references',
      () {
        final run = activityRun(
          runId: 'run-historical',
          releaseDigest: 'b' * 64,
          resultSurfaceReference: 'surface://result/historical',
          conversationId: 'conversation-historical',
          automationId: 'automation-historical',
        );

        expect(run.runId, 'run-historical');
        expect(run.releaseDigest, 'b' * 64);
        expect(run.resultSurfaceReference, 'surface://result/historical');
        expect(run.conversationId, 'conversation-historical');
        expect(run.automationId, 'automation-historical');
        expect(run.elapsed, const Duration(seconds: 18));
      },
    );

    test('exposes stable user-facing enum labels', () {
      expect(ActivityOrigin.values.map((value) => value.label), [
        'Chat',
        'Direct',
        'Schedule',
        'Event',
      ]);
      expect(ActivityStatus.values.map((value) => value.label), [
        'Queued',
        'Running',
        'Waiting for approval',
        'Completed',
        'Failed',
        'Parked',
      ]);
      expect(ActivityAuthority.values.map((value) => value.label), [
        'Authorized',
        'Waiting for approval',
        'Paused',
      ]);
    });

    test('rejects malformed required identities and release digests', () {
      expect(() => activityRun(runId: ' run-001'), throwsArgumentError);
      expect(
        () => activityRun(featureName: 'Research\u0000brief'),
        throwsArgumentError,
      );
      expect(() => activityRun(releaseDigest: 'A' * 64), throwsArgumentError);
      expect(() => activityRun(traceReference: ''), throwsArgumentError);
    });

    test('accepts the full Feature goal bound from the wire contract', () {
      expect(() => activityRun(featureName: 'a' * 4096), returnsNormally);
      expect(() => activityRun(featureName: 'a' * 4097), throwsArgumentError);
    });

    test('rejects malformed optional references and safe text', () {
      expect(
        () => activityRun(resultSurfaceReference: ' surface://result'),
        throwsArgumentError,
      );
      expect(
        () => activityRun(safeFailure: 'Unsafe\u0001failure'),
        throwsArgumentError,
      );
      expect(() => activityRun(failureGuidance: ' '), throwsArgumentError);
    });

    test('requires UTC timestamps', () {
      expect(
        () => activityRun(occurredAt: DateTime(2026, 7, 15, 10)),
        throwsArgumentError,
      );
      expect(
        () => activityRun(retryAt: DateTime(2026, 7, 15, 11)),
        throwsArgumentError,
      );
    });

    test('rejects reversed temporal order and impossible status timing', () {
      final occurred = DateTime.utc(2026, 7, 15, 10);
      expect(
        () => activityRun(
          occurredAt: occurred,
          startedAt: occurred.subtract(const Duration(seconds: 1)),
        ),
        throwsArgumentError,
      );
      expect(
        () => activityRun(
          occurredAt: occurred,
          completedAt: occurred.subtract(const Duration(seconds: 1)),
        ),
        throwsArgumentError,
      );
      expect(
        () => activityRun(
          status: ActivityStatus.running,
          completedAt: occurred.add(const Duration(seconds: 4)),
        ),
        throwsArgumentError,
      );
      expect(
        () => activityRun(status: ActivityStatus.completed, completedAt: null),
        returnsNormally,
      );
      expect(
        () => activityRun(
          status: ActivityStatus.waitingForApproval,
          authority: ActivityAuthority.waitingForApproval,
          completedAt: occurred.add(const Duration(seconds: 4)),
        ),
        returnsNormally,
      );
      expect(
        () => activityRun(
          status: ActivityStatus.failed,
          completedAt: occurred.add(const Duration(seconds: 4)),
        ),
        returnsNormally,
      );
      expect(
        () => activityRun(
          status: ActivityStatus.parked,
          completedAt: occurred.add(const Duration(seconds: 4)),
        ),
        throwsArgumentError,
      );
    });

    test('rejects negative attempts and a retry before occurrence', () {
      final occurred = DateTime.utc(2026, 7, 15, 10);
      expect(() => activityRun(attempts: -1), throwsArgumentError);
      expect(
        () => activityRun(
          occurredAt: occurred,
          retryAt: occurred.subtract(const Duration(seconds: 1)),
        ),
        throwsArgumentError,
      );
    });
  });
}
