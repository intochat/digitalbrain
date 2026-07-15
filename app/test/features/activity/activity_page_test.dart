import 'package:digitalbrain_flutter/features/activity/activity_controller.dart';
import 'package:digitalbrain_flutter/features/activity/activity_models.dart';
import 'package:digitalbrain_flutter/features/activity/activity_page.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

import 'activity_test_fixtures.dart';

void main() {
  testWidgets(
    'shows an accessible loading state then the first-run empty state',
    (tester) async {
      final gateway = QueueActivityGateway();
      final semantics = tester.ensureSemantics();

      await _pumpPage(tester, ActivityPage(gateway: gateway));

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
      expect(find.bySemanticsLabel('Loading Activity'), findsOneWidget);

      gateway.requests.single.complete([]);
      await tester.pumpAndSettle();

      expect(find.text('No activity yet'), findsOneWidget);
      expect(
        find.text('Runs will appear here when a Feature starts.'),
        findsOneWidget,
      );
      semantics.dispose();
    },
  );

  testWidgets('renders selected Run detail beside the list on wide windows', (
    tester,
  ) async {
    final controller = await _controllerWith([
      activityRun(
        runId: 'run-complete',
        featureName: 'Research brief',
        releaseDigest: 'b' * 64,
      ),
      activityRun(
        runId: 'run-waiting',
        featureId: 'feature-inbox',
        featureName: 'Inbox triage',
        status: ActivityStatus.waitingForApproval,
        authority: ActivityAuthority.waitingForApproval,
        resultSurfaceReference: null,
        completedAt: null,
      ),
    ]);
    addTearDown(controller.dispose);

    await _pumpPage(
      tester,
      ActivityPage(controller: controller),
      size: const Size(1200, 820),
    );

    expect(find.byKey(activityListKey), findsOneWidget);
    expect(find.byType(ActivityRunDetailView), findsOneWidget);
    expect(find.text('Research brief'), findsWidgets);
    expect(find.text('Run overview'), findsOneWidget);
    expect(find.text('b' * 64), findsNothing);

    for (
      var attempt = 0;
      attempt < 5 && find.byKey(activityTechnicalDetailsKey).evaluate().isEmpty;
      attempt++
    ) {
      await tester.drag(find.byType(ListView).last, const Offset(0, -260));
      await tester.pumpAndSettle();
    }
    await tester.tap(find.byKey(activityTechnicalDetailsKey));
    await tester.pumpAndSettle();

    expect(find.text('b' * 64), findsOneWidget);
    expect(find.text('trace-001'), findsOneWidget);
  });

  testWidgets('selecting a compact Run delegates navigation to the caller', (
    tester,
  ) async {
    final run = activityRun(runId: 'run-compact');
    final controller = await _controllerWith([run]);
    addTearDown(controller.dispose);
    ActivityRun? selected;

    await _pumpPage(
      tester,
      ActivityPage(
        controller: controller,
        onRunSelected: (value) => selected = value,
      ),
      size: const Size(600, 760),
    );

    expect(find.byType(ActivityRunDetailView), findsNothing);
    await tester.tap(find.byKey(activityRunCardKey('run-compact')));

    expect(selected?.runId, 'run-compact');
  });

  testWidgets('the first compact Run is keyboard activatable', (tester) async {
    final run = activityRun(runId: 'run-keyboard');
    final controller = await _controllerWith([run]);
    addTearDown(controller.dispose);
    ActivityRun? selected;

    await _pumpPage(
      tester,
      ActivityPage(
        controller: controller,
        onRunSelected: (value) => selected = value,
      ),
      size: const Size(600, 760),
    );

    await tester.sendKeyEvent(LogicalKeyboardKey.enter);
    await tester.pump();

    expect(selected?.runId, 'run-keyboard');
  });

  testWidgets(
    'status origin and Feature menus intersect through the controller',
    (tester) async {
      final controller = await _controllerWith([
        activityRun(
          runId: 'run-alpha-chat',
          featureId: 'feature-alpha',
          featureName: 'Alpha',
        ),
        activityRun(
          runId: 'run-alpha-event',
          featureId: 'feature-alpha',
          featureName: 'Alpha',
          origin: ActivityOrigin.event,
          status: ActivityStatus.failed,
        ),
        activityRun(
          runId: 'run-beta-event',
          featureId: 'feature-beta',
          featureName: 'Beta',
          origin: ActivityOrigin.event,
          status: ActivityStatus.failed,
        ),
      ]);
      addTearDown(controller.dispose);

      await _pumpPage(
        tester,
        ActivityPage(controller: controller),
        size: const Size(800, 820),
      );

      await tester.tap(find.byKey(activityStatusFilterKey));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Failed').last);
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(activityOriginFilterKey));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Event').last);
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(activityFeatureFilterKey));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Alpha').last);
      await tester.pumpAndSettle();

      expect(controller.statusFilter, ActivityStatus.failed);
      expect(controller.originFilter, ActivityOrigin.event);
      expect(controller.featureFilter, 'feature-alpha');
      expect(find.byKey(activityRunCardKey('run-alpha-event')), findsOneWidget);
      expect(find.byKey(activityRunCardKey('run-beta-event')), findsNothing);

      await tester.tap(find.byKey(activityClearFiltersButtonKey));
      await tester.pump();
      expect(controller.hasActiveFilters, isFalse);
    },
  );

  testWidgets('shows a distinct empty state when filters have no matches', (
    tester,
  ) async {
    final controller = await _controllerWith([activityRun()]);
    addTearDown(controller.dispose);
    controller.setStatusFilter(ActivityStatus.failed);

    await _pumpPage(tester, ActivityPage(controller: controller));

    expect(find.text('No runs match these filters'), findsOneWidget);
    expect(find.byKey(activityClearFiltersButtonKey), findsOneWidget);
  });

  testWidgets('keeps the filtered empty state after an empty server reload', (
    tester,
  ) async {
    final gateway = QueueActivityGateway();
    await _pumpPage(tester, ActivityPage(gateway: gateway));
    gateway.requests.single.complete([activityRun()]);
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(activityStatusFilterKey));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Failed').last);
    await tester.pump(const Duration(milliseconds: 300));

    expect(gateway.requests, hasLength(2));
    gateway.requests.last.complete([]);
    await tester.pumpAndSettle();

    expect(find.text('No runs match these filters'), findsOneWidget);
    expect(find.text('No activity yet'), findsNothing);
    expect(find.byKey(activityClearFiltersButtonKey), findsOneWidget);
  });

  testWidgets('bounds full-length Feature goals in cards and filter menus', (
    tester,
  ) async {
    final longName = 'a' * 4096;
    final controller = await _controllerWith([
      activityRun(featureName: longName),
    ]);
    addTearDown(controller.dispose);

    await _pumpPage(
      tester,
      ActivityPage(controller: controller),
      size: const Size(320, 700),
    );

    expect(
      tester.getSize(find.byKey(activityRunCardKey('run-001'))).height,
      lessThan(180),
    );
    await tester.ensureVisible(find.byKey(activityFeatureFilterKey));
    await tester.pump();
    await tester.tap(find.byKey(activityFeatureFilterKey));
    await tester.pumpAndSettle();

    final labels = tester.widgetList<Text>(find.text(longName)).toList();
    expect(labels, hasLength(2));
    expect(labels.every((label) => label.maxLines == 2), isTrue);
    expect(
      labels.every((label) => label.overflow == TextOverflow.ellipsis),
      isTrue,
    );
    expect(tester.takeException(), isNull);
  });

  testWidgets('keeps gateway details private and retries a failed load', (
    tester,
  ) async {
    final gateway = ImmediateActivityGateway([])
      ..error = StateError('provider secret payload');

    await _pumpPage(tester, ActivityPage(gateway: gateway));
    await tester.pumpAndSettle();

    expect(find.text('Activity is unavailable'), findsOneWidget);
    expect(find.text("We couldn't load Activity. Try again."), findsOneWidget);
    expect(find.textContaining('provider secret payload'), findsNothing);

    gateway
      ..error = null
      ..runs = [activityRun(runId: 'run-recovered')];
    await tester.tap(find.byKey(activityRetryButtonKey));
    await tester.pumpAndSettle();

    expect(gateway.calls, 2);
    expect(find.byKey(activityRunCardKey('run-recovered')), findsOneWidget);
  });

  testWidgets('Run detail exposes only typed navigation callbacks', (
    tester,
  ) async {
    final run = activityRun(
      automationId: 'automation-001',
      safeFailure: 'The source could not be reached.',
      failureGuidance: 'Reconnect the source, then retry this Run.',
    );
    String? feature;
    String? conversation;
    String? request;
    String? automation;
    String? result;

    await tester.pumpWidget(
      MaterialApp(
        home: ActivityRunDetailPage(
          run: run,
          onOpenFeature: (value) => feature = value,
          onOpenConversation: (value) => conversation = value,
          onOpenRequest: (value) => request = value,
          onOpenAutomation: (featureId, value) {
            expect(featureId, 'feature-research');
            automation = value;
          },
          onOpenResultSurface: (value) => result = value,
        ),
      ),
    );
    await tester.pumpAndSettle();

    for (final key in [
      activityOpenFeatureButtonKey,
      activityOpenChatButtonKey,
      activityOpenRequestButtonKey,
      activityOpenAutomationButtonKey,
      activityOpenResultButtonKey,
    ]) {
      await tester.ensureVisible(find.byKey(key));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(key));
    }

    expect(feature, 'feature-research');
    expect(conversation, 'conversation-001');
    expect(request, 'request-001');
    expect(automation, 'automation-001');
    expect(result, 'surface://result/run-001');
    expect(find.text('The source could not be reached.'), findsOneWidget);
    expect(
      find.text('Reconnect the source, then retry this Run.'),
      findsOneWidget,
    );
  });

  testWidgets('Run cards expose concise selected semantics', (tester) async {
    final controller = await _controllerWith([activityRun()]);
    addTearDown(controller.dispose);
    final semantics = tester.ensureSemantics();

    await _pumpPage(
      tester,
      ActivityPage(controller: controller),
      size: const Size(1200, 820),
    );

    expect(
      find.bySemanticsLabel(
        RegExp('Research brief, Completed, Chat, selected'),
      ),
      findsOneWidget,
    );
    semantics.dispose();
  });

  testWidgets(
    'compact layout remains usable at two-hundred-percent text scale',
    (tester) async {
      final controller = await _controllerWith([
        for (var index = 0; index < 4; index++)
          activityRun(
            runId: 'run-$index',
            featureId: 'feature-$index',
            featureName: 'Feature $index',
          ),
      ]);
      addTearDown(controller.dispose);

      await _pumpPage(
        tester,
        ActivityPage(controller: controller),
        size: const Size(320, 700),
        textScaler: const TextScaler.linear(2),
      );

      expect(tester.takeException(), isNull);
      expect(find.byKey(activityListKey), findsOneWidget);
      expect(find.byKey(activityStatusFilterKey), findsOneWidget);
    },
  );
}

Future<ActivityController> _controllerWith(List<ActivityRun> runs) async {
  final controller = ActivityController(
    gateway: ImmediateActivityGateway(runs),
  );
  await controller.load();
  return controller;
}

Future<void> _pumpPage(
  WidgetTester tester,
  Widget page, {
  Size size = const Size(800, 720),
  TextScaler textScaler = TextScaler.noScaling,
}) async {
  tester.view.devicePixelRatio = 1;
  tester.view.physicalSize = size;
  addTearDown(tester.view.resetDevicePixelRatio);
  addTearDown(tester.view.resetPhysicalSize);
  await tester.pumpWidget(
    MaterialApp(
      theme: ThemeData(colorSchemeSeed: const Color(0xFF4058D7)),
      builder: (context, child) => MediaQuery(
        data: MediaQuery.of(context).copyWith(textScaler: textScaler),
        child: child ?? const SizedBox.shrink(),
      ),
      home: page,
    ),
  );
  await tester.pump();
}
