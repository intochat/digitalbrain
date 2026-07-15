import 'package:digitalbrain_flutter/features/activity/activity_controller.dart';
import 'package:digitalbrain_flutter/features/activity/activity_models.dart';
import 'package:digitalbrain_flutter/features/activity/activity_page.dart';
import 'package:digitalbrain_flutter/theme/digitalbrain_theme.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'activity_test_fixtures.dart';

const _goldenBoundaryKey = ValueKey('activity-golden-boundary');

void main() {
  testWidgets('wide Activity master detail', (tester) async {
    await _pumpGolden(tester, const Size(1280, 820));

    await expectLater(
      find.byKey(_goldenBoundaryKey),
      matchesGoldenFile('goldens/activity_wide.png'),
    );
  });

  testWidgets('compact Activity list', (tester) async {
    await _pumpGolden(tester, const Size(390, 844));

    await expectLater(
      find.byKey(_goldenBoundaryKey),
      matchesGoldenFile('goldens/activity_compact.png'),
    );
  });
}

Future<void> _pumpGolden(WidgetTester tester, Size size) async {
  tester.view.devicePixelRatio = 1;
  tester.view.physicalSize = size;
  addTearDown(tester.view.resetDevicePixelRatio);
  addTearDown(tester.view.resetPhysicalSize);
  final controller = ActivityController(
    gateway: ImmediateActivityGateway(_goldenRuns()),
  );
  addTearDown(controller.dispose);
  await controller.load();
  await tester.pumpWidget(
    MaterialApp(
      debugShowCheckedModeBanner: false,
      theme: buildDigitalBrainTheme(useGoogleFonts: false),
      darkTheme: buildDigitalBrainTheme(useGoogleFonts: false),
      themeMode: ThemeMode.dark,
      home: RepaintBoundary(
        key: _goldenBoundaryKey,
        child: ActivityPage(
          controller: controller,
          onOpenFeature: (_) {},
          onOpenConversation: (_) {},
          onOpenRequest: (_) {},
          onOpenAutomation: (_, _) {},
          onOpenResultSurface: (_) {},
        ),
      ),
    ),
  );
  await tester.pumpAndSettle();
}

List<ActivityRun> _goldenRuns() => [
  activityRun(
    runId: 'run-source-review',
    featureId: 'feature-source-review',
    featureName: 'Source review',
    origin: ActivityOrigin.event,
    status: ActivityStatus.failed,
    occurredAt: DateTime.utc(2026, 7, 15, 12, 4),
    safeFailure: 'The source could not be reached.',
    failureGuidance: 'Reconnect the source, then retry this Run.',
    automationId: 'automation-source-review',
    inputKind: 'source.changed',
    traceReference: 'trace-source-review',
  ),
  activityRun(
    runId: 'run-company-brief',
    featureId: 'feature-company-brief',
    featureName: 'Company brief',
    origin: ActivityOrigin.chat,
    status: ActivityStatus.completed,
    occurredAt: DateTime.utc(2026, 7, 15, 11, 42),
    traceReference: 'trace-company-brief',
  ),
  activityRun(
    runId: 'run-inbox-triage',
    featureId: 'feature-inbox-triage',
    featureName: 'Inbox triage',
    origin: ActivityOrigin.schedule,
    status: ActivityStatus.waitingForApproval,
    authority: ActivityAuthority.waitingForApproval,
    occurredAt: DateTime.utc(2026, 7, 15, 10, 30),
    completedAt: null,
    resultSurfaceReference: null,
    automationId: 'automation-inbox-triage',
    inputKind: 'schedule.tick',
    traceReference: 'trace-inbox-triage',
  ),
  activityRun(
    runId: 'run-contact-sync',
    featureId: 'feature-contact-sync',
    featureName: 'Contact sync',
    origin: ActivityOrigin.direct,
    status: ActivityStatus.running,
    occurredAt: DateTime.utc(2026, 7, 15, 9, 18),
    completedAt: null,
    resultSurfaceReference: null,
    inputKind: 'direct.request',
    traceReference: 'trace-contact-sync',
  ),
];
