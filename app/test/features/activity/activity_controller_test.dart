import 'package:digitalbrain_flutter/features/activity/activity_controller.dart';
import 'package:digitalbrain_flutter/features/activity/activity_models.dart';
import 'package:flutter_test/flutter_test.dart';

import 'activity_test_fixtures.dart';

void main() {
  group('ActivityController', () {
    test('loads and deterministically orders newest runs first', () async {
      final older = activityRun(
        runId: 'run-z',
        occurredAt: DateTime.utc(2026, 7, 15, 9),
      );
      final newestB = activityRun(
        runId: 'run-b',
        occurredAt: DateTime.utc(2026, 7, 15, 11),
      );
      final newestA = activityRun(
        runId: 'run-a',
        occurredAt: DateTime.utc(2026, 7, 15, 11),
      );
      final gateway = ImmediateActivityGateway([older, newestB, newestA]);
      final controller = ActivityController(gateway: gateway);
      addTearDown(controller.dispose);

      await controller.load();

      expect(controller.state, ActivityLoadState.ready);
      expect(controller.runs.map((run) => run.runId), [
        'run-a',
        'run-b',
        'run-z',
      ]);
      expect(controller.filteredRuns, controller.runs);
      expect(controller.selectedRun?.runId, 'run-a');
      expect(controller.failure, isNull);
    });

    test('keeps backend completion-recency ordering for older started runs', () async {
      final newlyOccurred = activityRun(
        runId: 'run-newly-occurred',
        occurredAt: DateTime.utc(2026, 7, 15, 11),
      );
      final recentlyCompleted = activityRun(
        runId: 'run-recently-completed',
        occurredAt: DateTime.utc(2026, 7, 15, 8),
        startedAt: DateTime.utc(2026, 7, 15, 8, 0, 2),
        completedAt: DateTime.utc(2026, 7, 15, 12),
      );
      final controller = ActivityController(
        gateway: ImmediateActivityGateway([
          newlyOccurred,
          recentlyCompleted,
        ]),
      );
      addTearDown(controller.dispose);

      await controller.load();

      expect(controller.runs.map((run) => run.runId), [
        'run-recently-completed',
        'run-newly-occurred',
      ]);
    });

    test('publishes loading state before a request completes', () async {
      final gateway = QueueActivityGateway();
      final controller = ActivityController(gateway: gateway);
      addTearDown(controller.dispose);
      final states = <ActivityLoadState>[];
      controller.addListener(() => states.add(controller.state));

      final pending = controller.load();
      expect(controller.state, ActivityLoadState.loading);
      expect(controller.isInitialLoading, isTrue);

      gateway.requests.single.complete([activityRun()]);
      await pending;

      expect(
        states,
        containsAllInOrder([
          ActivityLoadState.loading,
          ActivityLoadState.ready,
        ]),
      );
    });

    test('keeps existing data visible during refresh', () async {
      final gateway = QueueActivityGateway();
      final controller = ActivityController(gateway: gateway);
      addTearDown(controller.dispose);

      final initial = controller.load();
      gateway.requests.single.complete([activityRun()]);
      await initial;

      final refresh = controller.refresh();
      expect(controller.isRefreshing, isTrue);
      expect(controller.runs, hasLength(1));

      gateway.requests.last.complete([activityRun(runId: 'run-refreshed')]);
      await refresh;
      expect(controller.runs.single.runId, 'run-refreshed');
    });

    test(
      'newest overlapping request wins when responses complete out of order',
      () async {
        final gateway = QueueActivityGateway();
        final controller = ActivityController(gateway: gateway);
        addTearDown(controller.dispose);

        final first = controller.load();
        final second = controller.refresh();
        expect(gateway.requests, hasLength(2));

        gateway.requests[1].complete([activityRun(runId: 'run-new')]);
        await second;
        gateway.requests[0].complete([activityRun(runId: 'run-stale')]);
        await first;

        expect(controller.runs.single.runId, 'run-new');
        expect(controller.state, ActivityLoadState.ready);
      },
    );

    test(
      'maps gateway failures to a safe retryable presentation failure',
      () async {
        final gateway = ImmediateActivityGateway([])
          ..error = StateError('secret');
        final controller = ActivityController(gateway: gateway);
        addTearDown(controller.dispose);

        await controller.load();

        expect(controller.state, ActivityLoadState.failed);
        expect(
          controller.failure?.message,
          "We couldn't load Activity. Try again.",
        );
        expect(controller.failure?.retryable, isTrue);
        expect(controller.failure?.message, isNot(contains('secret')));
      },
    );

    test(
      'rejects duplicate run identities without replacing existing data',
      () async {
        final gateway = ImmediateActivityGateway([activityRun()]);
        final controller = ActivityController(gateway: gateway);
        addTearDown(controller.dispose);
        await controller.load();

        gateway.runs = [activityRun(), activityRun()];
        await controller.refresh();

        expect(controller.state, ActivityLoadState.failed);
        expect(controller.runs, hasLength(1));
        expect(
          controller.failure?.message,
          'Activity data could not be verified.',
        );
        expect(controller.failure?.retryable, isTrue);
      },
    );

    test('intersects status origin and Feature filters', () async {
      final gateway = ImmediateActivityGateway([
        activityRun(
          runId: 'run-chat-complete',
          featureId: 'feature-a',
          featureName: 'Alpha',
        ),
        activityRun(
          runId: 'run-event-failed',
          featureId: 'feature-a',
          featureName: 'Alpha',
          origin: ActivityOrigin.event,
          status: ActivityStatus.failed,
          safeFailure: 'The source was unavailable.',
          failureGuidance: 'Reconnect the source, then retry.',
        ),
        activityRun(
          runId: 'run-event-other',
          featureId: 'feature-b',
          featureName: 'Beta',
          origin: ActivityOrigin.event,
          status: ActivityStatus.failed,
        ),
      ]);
      final controller = ActivityController(gateway: gateway);
      addTearDown(controller.dispose);
      await controller.load();

      await controller.setStatusFilter(ActivityStatus.failed);
      await controller.setOriginFilter(ActivityOrigin.event);
      await controller.setFeatureFilter('feature-a');

      expect(controller.filteredRuns.single.runId, 'run-event-failed');
      expect(controller.hasActiveFilters, isTrue);
      expect(controller.availableFeatures.map((feature) => feature.name), [
        'Alpha',
        'Beta',
      ]);
      expect(gateway.queries, hasLength(4));
      expect(gateway.queries[1].status, ActivityStatus.failed);
      expect(gateway.queries[1].origin, isNull);
      expect(gateway.queries[2].status, ActivityStatus.failed);
      expect(gateway.queries[2].origin, ActivityOrigin.event);
      expect(gateway.queries[3].status, ActivityStatus.failed);
      expect(gateway.queries[3].origin, ActivityOrigin.event);
      expect(gateway.queries[3].featureId, 'feature-a');
    });

    test(
      'filters existing runs immediately while the server reloads',
      () async {
        final gateway = QueueActivityGateway();
        final controller = ActivityController(gateway: gateway);
        addTearDown(controller.dispose);
        final initial = controller.load();
        gateway.requests.single.complete([
          activityRun(runId: 'run-completed'),
          activityRun(runId: 'run-failed', status: ActivityStatus.failed),
        ]);
        await initial;

        final filtered = controller.setStatusFilter(ActivityStatus.failed);

        expect(controller.state, ActivityLoadState.loading);
        expect(controller.filteredRuns.single.runId, 'run-failed');
        expect(gateway.queries.last.status, ActivityStatus.failed);

        gateway.requests.last.complete([
          activityRun(
            runId: 'run-server-failed',
            status: ActivityStatus.failed,
          ),
        ]);
        await filtered;
        expect(controller.filteredRuns.single.runId, 'run-server-failed');
      },
    );

    test(
      'newest filtered request wins when responses complete out of order',
      () async {
        final gateway = QueueActivityGateway();
        final controller = ActivityController(gateway: gateway);
        addTearDown(controller.dispose);
        final initial = controller.load();
        gateway.requests.single.complete([activityRun()]);
        await initial;

        final statusRequest = controller.setStatusFilter(ActivityStatus.failed);
        final originRequest = controller.setOriginFilter(ActivityOrigin.event);

        expect(gateway.requests, hasLength(3));
        expect(gateway.queries[1].origin, isNull);
        expect(gateway.queries[2].status, ActivityStatus.failed);
        expect(gateway.queries[2].origin, ActivityOrigin.event);

        gateway.requests[2].complete([
          activityRun(
            runId: 'run-new',
            origin: ActivityOrigin.event,
            status: ActivityStatus.failed,
          ),
        ]);
        await originRequest;
        gateway.requests[1].complete([
          activityRun(runId: 'run-stale', status: ActivityStatus.failed),
        ]);
        await statusRequest;

        expect(controller.runs.single.runId, 'run-new');
        expect(controller.statusFilter, ActivityStatus.failed);
        expect(controller.originFilter, ActivityOrigin.event);
      },
    );

    test(
      'preserves known Feature choices across filtered empty reloads',
      () async {
        final gateway = QueueActivityGateway();
        final controller = ActivityController(gateway: gateway);
        addTearDown(controller.dispose);
        final initial = controller.load();
        gateway.requests.single.complete([
          activityRun(
            runId: 'run-alpha',
            featureId: 'feature-a',
            featureName: 'Alpha',
          ),
          activityRun(
            runId: 'run-beta',
            featureId: 'feature-b',
            featureName: 'Beta',
          ),
        ]);
        await initial;

        final statusRequest = controller.setStatusFilter(ActivityStatus.failed);
        gateway.requests.last.complete([]);
        await statusRequest;

        expect(controller.availableFeatures.map((feature) => feature.name), [
          'Alpha',
          'Beta',
        ]);

        final featureRequest = controller.setFeatureFilter('feature-b');
        expect(gateway.queries.last.featureId, 'feature-b');
        gateway.requests.last.complete([]);
        await featureRequest;

        expect(controller.featureFilter, 'feature-b');
        expect(controller.availableFeatures.map((feature) => feature.name), [
          'Alpha',
          'Beta',
        ]);
      },
    );

    test('clears filters and keeps selection within visible results', () async {
      final gateway = ImmediateActivityGateway([
        activityRun(runId: 'run-complete'),
        activityRun(runId: 'run-failed', status: ActivityStatus.failed),
      ]);
      final controller = ActivityController(gateway: gateway);
      addTearDown(controller.dispose);
      await controller.load();
      controller.selectRun('run-complete');

      await controller.setStatusFilter(ActivityStatus.failed);
      expect(controller.selectedRun?.runId, 'run-failed');

      await controller.clearFilters();
      expect(controller.hasActiveFilters, isFalse);
      expect(controller.filteredRuns, hasLength(2));
      expect(controller.selectedRun?.runId, 'run-failed');
      expect(gateway.queries.last.status, isNull);
      expect(gateway.queries.last.origin, isNull);
      expect(gateway.queries.last.featureId, isNull);
    });

    test(
      'ignores a Feature filter that is not in the loaded projection',
      () async {
        final controller = ActivityController(
          gateway: ImmediateActivityGateway([activityRun()]),
        );
        addTearDown(controller.dispose);
        await controller.load();

        await controller.setFeatureFilter('missing-feature');

        expect(controller.featureFilter, isNull);
        expect(controller.filteredRuns, hasLength(1));
      },
    );

    test('does not publish a response after disposal', () async {
      final gateway = QueueActivityGateway();
      final controller = ActivityController(gateway: gateway);
      var notifications = 0;
      controller.addListener(() => notifications++);
      final pending = controller.load();
      expect(notifications, 1);

      controller.dispose();
      gateway.requests.single.complete([activityRun()]);
      await pending;

      expect(notifications, 1);
    });
  });
}
