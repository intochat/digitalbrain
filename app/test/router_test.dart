import 'dart:async';

import 'package:digitalbrain_flutter/app.dart';
import 'package:digitalbrain_flutter/core/session/digitalbrain_client.dart';
import 'package:digitalbrain_flutter/features/activity/activity_page.dart';
import 'package:digitalbrain_flutter/features/activity/activity_run_page.dart';
import 'package:digitalbrain_flutter/features/releases/feature_release_page.dart';
import 'package:digitalbrain_flutter/features/studio/feature_studio_models.dart';
import 'package:digitalbrain_flutter/features/studio/feature_studio_page.dart';
import 'package:digitalbrain_flutter/grpc/ui.pb.dart' as wire;
import 'package:digitalbrain_flutter/grpc/ui.pbenum.dart' as wire_enums;
import 'package:digitalbrain_flutter/router.dart';
import 'package:digitalbrain_flutter/runtime/protocol/surface_protocol.dart';
import 'package:digitalbrain_flutter/runtime/runtime.dart';
import 'package:digitalbrain_flutter/runtime/runtime_configuration.dart';
import 'package:digitalbrain_flutter/runtime/runtime_session_owner.dart';
import 'package:digitalbrain_flutter/runtime/widgets/chat_page.dart';
import 'package:digitalbrain_flutter/runtime/widgets/ino_composer.dart';
import 'package:digitalbrain_flutter/runtime/widgets/ino_conversation_view.dart';
import 'package:digitalbrain_flutter/runtime/widgets/runtime_shell.dart';
import 'package:digitalbrain_flutter/shell/digitalbrain_shell.dart';
import 'package:fixnum/fixnum.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

import 'features/releases/feature_release_test_fixtures.dart';
import 'runtime/test_fixtures.dart';

void main() {
  testWidgets(
    'canonical exact Feature Release route loads authority and generates canonical rollback location',
    (tester) async {
      final location = '/features/feature-a/releases/${releaseDigest('a')}';
      final transport = _RouterTransport(
        _RouterFeedCall(),
        featureReply: wireReleaseDetails(),
        rollbackReply: wireReleaseDetails(
          activeCharacter: 'b',
          withPrevious: false,
          revision: Int64(13),
        ),
      );
      final owner = RuntimeSessionOwner(
        configuration: _configuration(),
        transportFactory: (_) => transport,
      );
      final router = createDigitalBrainRouter(initialLocation: location);

      await tester.pumpWidget(
        DigitalBrainApp(
          sessionOwnerFactory: () => owner,
          routerFactory: () => router,
        ),
      );
      await _pumpUntil(
        tester,
        () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
      );
      await tester.tap(find.byKey(runtimeSignInButtonKey));
      await _pumpUntil(tester, () => transport.getFeatureRequests.length == 1);
      await _pumpUntil(
        tester,
        () => find.text('Active Version').evaluate().isNotEmpty,
      );

      final page = tester.widget<FeatureReleasePage>(
        find.byType(FeatureReleasePage),
      );
      expect(page.expectedReleaseDigest, releaseDigest('a'));
      expect(
        page.key,
        ValueKey('feature-version-feature-a-${releaseDigest('a')}'),
      );
      expect(transport.getFeatureRequests.single.featureId, 'feature-a');
      expect(transport.releaseSourceRequests, hasLength(2));
      expect(find.text(releaseDigest('a')), findsOneWidget);
      expect(find.text(sourceReference('a')), findsOneWidget);

      final rollback = find.byKey(featureReleaseRollbackButtonKey);
      await tester.ensureVisible(rollback);
      await tester.tap(rollback);
      await tester.pumpAndSettle();
      expect(transport.rollbackRequests, isEmpty);
      expect(find.text(releaseDigest('b')), findsOneWidget);
      expect(find.text(sourceReference('b')), findsOneWidget);

      await tester.tap(find.byKey(featureReleaseConfirmRollbackButtonKey));
      await _pumpUntil(tester, () => transport.rollbackRequests.length == 1);
      await tester.pumpAndSettle();

      final request = transport.rollbackRequests.single;
      expect(request.featureId, 'feature-a');
      expect(request.expectedActiveDigest, releaseDigest('a'));
      expect(request.targetDigest, releaseDigest('b'));
      expect(request.idempotencyId, isNotEmpty);
      expect(request.expectedRevision, Int64(12));
      expect(transport.releaseSourceRequests, hasLength(4));
      expect(find.text('Previous Version restored exactly'), findsOneWidget);
      expect(find.text(releaseDigest('b')), findsOneWidget);
      expect(find.text(sourceReference('b')), findsOneWidget);
      expect(
        router.routeInformationProvider.value.uri.path,
        '/features/feature-a/releases/${releaseDigest('b')}',
      );

      await tester.pumpWidget(const SizedBox.shrink());
      await _pumpUntil(tester, () => transport.closeCalls > 0);
    },
  );

  testWidgets('failed exact rollback retains the requested Version route', (
    tester,
  ) async {
    final requestedLocation =
        '/features/feature-a/versions/${releaseDigest('a')}';
    final transport = _RouterTransport(
      _RouterFeedCall(),
      featureReply: wireReleaseDetails(),
      rollbackReply: wire.FeatureReply(),
    );
    final owner = RuntimeSessionOwner(
      configuration: _configuration(),
      transportFactory: (_) => transport,
    );
    final router = createDigitalBrainRouter(initialLocation: requestedLocation);

    await tester.pumpWidget(
      DigitalBrainApp(
        sessionOwnerFactory: () => owner,
        routerFactory: () => router,
      ),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );
    await tester.tap(find.byKey(runtimeSignInButtonKey));
    await _pumpUntil(
      tester,
      () => find.byKey(featureReleaseRollbackButtonKey).evaluate().isNotEmpty,
    );

    final rollback = find.byKey(featureReleaseRollbackButtonKey);
    await tester.ensureVisible(rollback);
    await tester.tap(rollback);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(featureReleaseConfirmRollbackButtonKey));
    await _pumpUntil(tester, () => transport.rollbackRequests.length == 1);
    await tester.pumpAndSettle();

    expect(router.routeInformationProvider.value.uri.path, requestedLocation);
    expect(
      find.text(
        'The rollback response could not be verified. Reload the Feature.',
      ),
      findsOneWidget,
    );

    await tester.pumpWidget(const SizedBox.shrink());
    await _pumpUntil(tester, () => transport.closeCalls > 0);
  });

  testWidgets(
    'legacy Version route remains an exact release compatibility alias',
    (tester) async {
      final legacyLocation =
          '/features/feature-a/versions/${releaseDigest('b')}';
      final transport = _RouterTransport(
        _RouterFeedCall(),
        featureReply: wireReleaseDetails(
          activeCharacter: 'b',
          withPrevious: false,
        ),
      );
      final owner = RuntimeSessionOwner(
        configuration: _configuration(),
        transportFactory: (_) => transport,
      );
      final router = createDigitalBrainRouter(initialLocation: legacyLocation);

      await tester.pumpWidget(
        DigitalBrainApp(
          sessionOwnerFactory: () => owner,
          routerFactory: () => router,
        ),
      );
      await _pumpUntil(
        tester,
        () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
      );
      await tester.tap(find.byKey(runtimeSignInButtonKey));
      await _pumpUntil(
        tester,
        () => find.text('Active Version').evaluate().isNotEmpty,
      );

      final page = tester.widget<FeatureReleasePage>(
        find.byType(FeatureReleasePage),
      );
      expect(page.expectedReleaseDigest, releaseDigest('b'));
      expect(router.routeInformationProvider.value.uri.path, legacyLocation);
      expect(find.text(releaseDigest('b')), findsOneWidget);
      expect(find.text('Previous Version restored exactly'), findsNothing);

      await tester.pumpWidget(const SizedBox.shrink());
      await _pumpUntil(tester, () => transport.closeCalls > 0);
    },
  );

  testWidgets('exact Feature Version route rejects an authority mismatch', (
    tester,
  ) async {
    final location = '/features/feature-a/versions/${releaseDigest('b')}';
    final transport = _RouterTransport(
      _RouterFeedCall(),
      featureReply: wireReleaseDetails(),
    );
    final owner = RuntimeSessionOwner(
      configuration: _configuration(),
      transportFactory: (_) => transport,
    );
    final router = createDigitalBrainRouter(initialLocation: location);

    await tester.pumpWidget(
      DigitalBrainApp(
        sessionOwnerFactory: () => owner,
        routerFactory: () => router,
      ),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );
    await tester.tap(find.byKey(runtimeSignInButtonKey));
    await _pumpUntil(tester, () => transport.getFeatureRequests.length == 1);
    await _pumpUntil(
      tester,
      () => find
          .text('The Feature response could not be verified.')
          .evaluate()
          .isNotEmpty,
    );

    final page = tester.widget<FeatureReleasePage>(
      find.byType(FeatureReleasePage),
    );
    expect(page.expectedReleaseDigest, releaseDigest('b'));
    expect(transport.releaseSourceRequests, isEmpty);
    expect(find.text('Active Version'), findsNothing);
    expect(find.byKey(featureReleaseRollbackButtonKey), findsNothing);

    await tester.pumpWidget(const SizedBox.shrink());
    await _pumpUntil(tester, () => transport.closeCalls > 0);
  });

  testWidgets('Feature route remains compatible without a Version digest', (
    tester,
  ) async {
    const location = '/features/feature-a';
    final transport = _RouterTransport(
      _RouterFeedCall(),
      featureReply: wireReleaseDetails(withPrevious: false),
    );
    final owner = RuntimeSessionOwner(
      configuration: _configuration(),
      transportFactory: (_) => transport,
    );
    final router = createDigitalBrainRouter(initialLocation: location);

    await tester.pumpWidget(
      DigitalBrainApp(
        sessionOwnerFactory: () => owner,
        routerFactory: () => router,
      ),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );
    await tester.tap(find.byKey(runtimeSignInButtonKey));
    await _pumpUntil(
      tester,
      () => find.text('Active Version').evaluate().isNotEmpty,
    );

    final page = tester.widget<FeatureReleasePage>(
      find.byType(FeatureReleasePage),
    );
    expect(page.expectedReleaseDigest, isNull);
    expect(find.text(releaseDigest('a')), findsOneWidget);

    await tester.pumpWidget(const SizedBox.shrink());
    await _pumpUntil(tester, () => transport.closeCalls > 0);
  });

  testWidgets('Studio Run now sends one server-checkable resume intent', (
    tester,
  ) async {
    final location = Uri(
      path: '/features/proposals/draft-a',
      queryParameters: const {
        'conversationId': 'tampered-conversation',
        'operationId': 'tampered-operation',
        'prompt': 'tampered prompt',
      },
    ).toString();
    final transport = _RouterTransport(_RouterFeedCall());
    final owner = RuntimeSessionOwner(
      configuration: _configuration(),
      transportFactory: (_) => transport,
    );
    final router = createDigitalBrainRouter(
      initialLocation: location,
      runNowIdFactory: () => 'resume-router',
    );

    await tester.pumpWidget(
      DigitalBrainApp(
        sessionOwnerFactory: () => owner,
        routerFactory: () => router,
      ),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );
    await tester.tap(find.byKey(runtimeSignInButtonKey));
    await _pumpUntil(
      tester,
      () => find.byType(FeatureStudioPage).evaluate().isNotEmpty,
    );

    final studio = tester.widget<FeatureStudioPage>(
      find.byType(FeatureStudioPage),
    );
    expect(studio.onRunNow, isNotNull);
    studio.onRunNow?.call('draft-a', Int64(7));
    await _pumpUntil(
      tester,
      () =>
          router.routeInformationProvider.value.uri.path == '/chat' &&
          transport.resumeRequests.length == 1,
    );

    expect(router.routeInformationProvider.value.uri.queryParameters, {
      'intent': 'resume-originating-request',
      'featureDraftId': 'draft-a',
      'expectedRevision': '7',
      'idempotencyId': 'resume-router',
    });
    expect(transport.resumeRequests.single.draftId, 'draft-a');
    expect(transport.resumeRequests.single.expectedRevision, Int64(7));
    expect(transport.resumeRequests.single.idempotencyId, 'resume-router');
    await tester.pump(const Duration(milliseconds: 20));
    expect(transport.resumeRequests, hasLength(1));

    await tester.pumpWidget(const SizedBox.shrink());
    await _pumpUntil(tester, () => transport.closeCalls > 0);
  });

  testWidgets('Studio Return to Chat performs no implicit resume', (
    tester,
  ) async {
    const location = '/features/proposals/draft-a';
    final transport = _RouterTransport(_RouterFeedCall());
    final owner = RuntimeSessionOwner(
      configuration: _configuration(),
      transportFactory: (_) => transport,
    );
    final router = createDigitalBrainRouter(initialLocation: location);

    await tester.pumpWidget(
      DigitalBrainApp(
        sessionOwnerFactory: () => owner,
        routerFactory: () => router,
      ),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );
    await tester.tap(find.byKey(runtimeSignInButtonKey));
    await _pumpUntil(
      tester,
      () => find.byType(FeatureStudioPage).evaluate().isNotEmpty,
    );

    final studio = tester.widget<FeatureStudioPage>(
      find.byType(FeatureStudioPage),
    );
    studio.onBackToChat(
      const FeatureStudioOriginatingRequest(
        operationId: 'operation-router',
        conversationId: 'conversation-router',
        text: 'Research Acme',
      ),
      'draft-a',
    );
    await _pumpUntil(
      tester,
      () => router.routeInformationProvider.value.uri.path == '/chat',
    );

    expect(router.routeInformationProvider.value.uri.queryParameters, isEmpty);
    expect(transport.resumeRequests, isEmpty);

    await tester.pumpWidget(const SizedBox.shrink());
    await _pumpUntil(tester, () => transport.closeCalls > 0);
  });

  testWidgets('malformed resume intent fails safely without execution', (
    tester,
  ) async {
    final location = Uri(
      path: '/chat',
      queryParameters: const {
        'intent': 'resume-originating-request',
        'featureDraftId': 'draft-a',
        'expectedRevision': 'not-a-revision',
        'idempotencyId': 'resume-router',
        'prompt': 'must never execute',
      },
    ).toString();
    final transport = _RouterTransport(_RouterFeedCall());
    final owner = RuntimeSessionOwner(
      configuration: _configuration(),
      transportFactory: (_) => transport,
    );
    final router = createDigitalBrainRouter(initialLocation: location);

    await tester.pumpWidget(
      DigitalBrainApp(
        sessionOwnerFactory: () => owner,
        routerFactory: () => router,
      ),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );
    await tester.tap(find.byKey(runtimeSignInButtonKey));
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeResumeErrorKey).evaluate().isNotEmpty,
    );

    expect(transport.resumeRequests, isEmpty);

    await tester.pumpWidget(const SizedBox.shrink());
    await _pumpUntil(tester, () => transport.closeCalls > 0);
  });

  testWidgets('resume failure retries once with the same stable identity', (
    tester,
  ) async {
    final location = Uri(
      path: '/chat',
      queryParameters: const {
        'intent': 'resume-originating-request',
        'featureDraftId': 'draft-a',
        'expectedRevision': '7',
        'idempotencyId': 'resume-router',
        'prompt': 'ignored prompt',
      },
    ).toString();
    final transport = _RouterTransport(
      _RouterFeedCall(),
      failFirstResume: true,
    );
    final owner = RuntimeSessionOwner(
      configuration: _configuration(),
      transportFactory: (_) => transport,
    );
    final router = createDigitalBrainRouter(initialLocation: location);

    await tester.pumpWidget(
      DigitalBrainApp(
        sessionOwnerFactory: () => owner,
        routerFactory: () => router,
      ),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );
    await tester.tap(find.byKey(runtimeSignInButtonKey));
    await _pumpUntil(
      tester,
      () =>
          transport.resumeRequests.length == 1 &&
          find.byKey(runtimeResumeErrorKey).evaluate().isNotEmpty,
    );
    await tester.tap(find.byKey(runtimeResumeRetryKey));
    await _pumpUntil(tester, () => transport.resumeRequests.length == 2);

    expect(
      transport.resumeRequests.map((request) => request.idempotencyId),
      everyElement('resume-router'),
    );
    expect(transport.resumeRequests.last.draftId, 'draft-a');
    expect(transport.resumeRequests.last.expectedRevision, Int64(7));

    await tester.pumpWidget(const SizedBox.shrink());
    await _pumpUntil(tester, () => transport.closeCalls > 0);
  });

  for (final (failure, reply) in <(String, wire.ResumeOriginatingRequestReply)>[
    (
      'mismatched command',
      wire.ResumeOriginatingRequestReply(
        commandId: 'different-intent',
        operationId: _resumeOperationId,
        phase: 'Accepted',
        version: Int64.ONE,
      ),
    ),
    (
      'empty operation',
      wire.ResumeOriginatingRequestReply(
        commandId: 'resume-router',
        operationId: '',
        phase: 'Accepted',
        version: Int64.ONE,
      ),
    ),
    (
      'noncanonical operation',
      wire.ResumeOriginatingRequestReply(
        commandId: 'resume-router',
        operationId: 'operation-resumed',
        phase: 'Accepted',
        version: Int64.ONE,
      ),
    ),
    (
      'unexpected phase',
      wire.ResumeOriginatingRequestReply(
        commandId: 'resume-router',
        operationId: _resumeOperationId,
        phase: 'Running',
        version: Int64.ONE,
      ),
    ),
    (
      'nonpositive version',
      wire.ResumeOriginatingRequestReply(
        commandId: 'resume-router',
        operationId: _resumeOperationId,
        phase: 'Accepted',
        version: Int64.ZERO,
      ),
    ),
  ]) {
    testWidgets('Chat rejects $failure reply and retries the stable intent', (
      tester,
    ) async {
      final location = Uri(
        path: '/chat',
        queryParameters: const {
          'intent': 'resume-originating-request',
          'featureDraftId': 'draft-a',
          'expectedRevision': '7',
          'idempotencyId': 'resume-router',
        },
      ).toString();
      final transport = _RouterTransport(_RouterFeedCall(), resumeReply: reply);
      final owner = RuntimeSessionOwner(
        configuration: _configuration(),
        transportFactory: (_) => transport,
      );
      final router = createDigitalBrainRouter(initialLocation: location);

      await tester.pumpWidget(
        DigitalBrainApp(
          sessionOwnerFactory: () => owner,
          routerFactory: () => router,
        ),
      );
      await _pumpUntil(
        tester,
        () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
      );
      await tester.tap(find.byKey(runtimeSignInButtonKey));
      await _pumpUntil(
        tester,
        () =>
            transport.resumeRequests.length == 1 &&
            find.byKey(runtimeResumeErrorKey).evaluate().isNotEmpty,
      );

      transport.resumeReply = null;
      await tester.tap(find.byKey(runtimeResumeRetryKey));
      await _pumpUntil(tester, () => transport.resumeRequests.length == 2);
      expect(
        transport.resumeRequests.map((request) => request.idempotencyId),
        everyElement('resume-router'),
      );

      await tester.pumpWidget(const SizedBox.shrink());
      await _pumpUntil(tester, () => transport.closeCalls > 0);
    });
  }

  testWidgets(
    'Studio update route preserves the requested installation target',
    (tester) async {
      final location = Uri(
        path: '/features/proposals/draft-a',
        queryParameters: const {'installationId': 'installation-existing'},
      ).toString();
      final transport = _RouterTransport(_RouterFeedCall());
      final owner = RuntimeSessionOwner(
        configuration: _configuration(),
        transportFactory: (_) => transport,
      );
      final router = createDigitalBrainRouter(initialLocation: location);

      await tester.pumpWidget(
        DigitalBrainApp(
          sessionOwnerFactory: () => owner,
          routerFactory: () => router,
        ),
      );
      await _pumpUntil(
        tester,
        () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
      );
      await tester.tap(find.byKey(runtimeSignInButtonKey));
      await _pumpUntil(
        tester,
        () => find.byType(FeatureStudioPage).evaluate().isNotEmpty,
      );

      final studio = tester.widget<FeatureStudioPage>(
        find.byType(FeatureStudioPage),
      );
      expect(studio.requestedInstallationId, 'installation-existing');
      final firstStudioState = tester.state(find.byType(FeatureStudioPage));

      router.go(
        '/features/proposals/draft-a?installationId=installation-replacement',
      );
      await _pumpUntil(
        tester,
        () =>
            tester
                .widget<FeatureStudioPage>(find.byType(FeatureStudioPage))
                .requestedInstallationId ==
            'installation-replacement',
      );
      expect(
        tester.state(find.byType(FeatureStudioPage)),
        isNot(same(firstStudioState)),
      );

      await tester.pumpWidget(const SizedBox.shrink());
      await _pumpUntil(tester, () => transport.closeCalls > 0);
    },
  );

  testWidgets('unauthenticated Studio deep link stays matched behind sign-in', (
    tester,
  ) async {
    const location =
        '/features/proposals/proposal-0123456789abcdef0123456789abcdef';
    final feed = _RouterFeedCall();
    final transport = _RouterTransport(feed);
    final owner = RuntimeSessionOwner(
      configuration: _configuration(),
      transportFactory: (_) => transport,
    );
    final router = createDigitalBrainRouter(initialLocation: location);

    await tester.pumpWidget(
      DigitalBrainApp(
        sessionOwnerFactory: () => owner,
        routerFactory: () => router,
      ),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );

    expect(router.routeInformationProvider.value.uri.path, location);
    expect(find.byKey(runtimeSignInKey), findsOneWidget);
    expect(find.byKey(featureStudioDraftIdKey), findsNothing);

    await tester.tap(find.byKey(runtimeSignInButtonKey));
    await _pumpUntil(
      tester,
      () => find.byKey(featureStudioDraftIdKey).evaluate().isNotEmpty,
    );

    expect(router.routeInformationProvider.value.uri.path, location);
    expect(
      find.text('proposal-0123456789abcdef0123456789abcdef'),
      findsNothing,
    );
    expect(
      find.descendant(
        of: find.byKey(digitalBrainCurrentContextKey),
        matching: find.text('Feature Studio'),
      ),
      findsOneWidget,
    );
    expect(find.byKey(digitalBrainSignOutButtonKey), findsOneWidget);

    expect(
      find.textContaining(RegExp('proposal', caseSensitive: false)),
      findsNothing,
    );
    await tester.tap(find.byKey(featureStudioBackToChatButtonKey));
    await _pumpUntil(
      tester,
      () => router.routeInformationProvider.value.uri.path == '/chat',
    );
    await tester.pump(const Duration(milliseconds: 300));
    expect(router.routeInformationProvider.value.uri.path, '/chat');
    expect(
      find.descendant(
        of: find.byKey(digitalBrainCurrentContextKey),
        matching: find.text('Chat'),
      ),
      findsOneWidget,
    );

    await tester.pumpWidget(const SizedBox.shrink());
    await _pumpUntil(tester, () => transport.closeCalls > 0);
    expect(transport.closeCalls, 1);
  });

  testWidgets(
    'shell Chat navigation guards invalid Studio edits with Stay or Discard',
    (tester) async {
      tester.view.devicePixelRatio = 1;
      tester.view.physicalSize = const Size(1200, 900);
      addTearDown(tester.view.resetDevicePixelRatio);
      addTearDown(tester.view.resetPhysicalSize);
      const location =
          '/features/proposals/proposal-0123456789abcdef0123456789abcdef';
      final transport = _RouterTransport(_RouterFeedCall());
      final owner = RuntimeSessionOwner(
        configuration: _configuration(),
        transportFactory: (_) => transport,
      );
      final router = createDigitalBrainRouter(initialLocation: location);

      await tester.pumpWidget(
        DigitalBrainApp(
          sessionOwnerFactory: () => owner,
          routerFactory: () => router,
        ),
      );
      await _pumpUntil(
        tester,
        () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
      );
      await tester.tap(find.byKey(runtimeSignInButtonKey));
      await _pumpUntil(
        tester,
        () => find
            .byKey(const ValueKey('scenario-brief-name'))
            .evaluate()
            .isNotEmpty,
      );
      await tester.enterText(
        find.byKey(const ValueKey('scenario-brief-name')),
        '',
      );
      await tester.pump();

      await tester.tap(find.byTooltip('Chat'));
      await _pumpUntil(
        tester,
        () => find.byKey(featureStudioLeaveDialogKey).evaluate().isNotEmpty,
      );
      expect(router.routeInformationProvider.value.uri.path, location);
      await tester.tap(find.byKey(featureStudioStayButtonKey));
      await tester.pump(const Duration(milliseconds: 300));
      expect(router.routeInformationProvider.value.uri.path, location);
      expect(
        tester
            .widget<TextFormField>(
              find.byKey(const ValueKey('scenario-brief-name')),
            )
            .controller
            ?.text,
        isEmpty,
      );

      await tester.tap(find.byTooltip('Chat'));
      await _pumpUntil(
        tester,
        () => find.byKey(featureStudioLeaveDialogKey).evaluate().isNotEmpty,
      );
      await tester.tap(find.byKey(featureStudioDiscardButtonKey));
      await _pumpUntil(
        tester,
        () => router.routeInformationProvider.value.uri.path == '/chat',
      );
      await tester.pump(const Duration(milliseconds: 301));
      expect(find.byKey(featureStudioLeaveDialogKey), findsNothing);
      expect(
        find.descendant(
          of: find.byKey(digitalBrainCurrentContextKey),
          matching: find.text('Chat'),
        ),
        findsOneWidget,
      );

      await tester.pumpWidget(const SizedBox.shrink());
      await _pumpUntil(tester, () => transport.closeCalls > 0);
      expect(transport.closeCalls, 1);
    },
  );

  testWidgets(
    'parameter-only Studio navigation replaces identity and retains exit guard',
    (tester) async {
      tester.view.devicePixelRatio = 1;
      tester.view.physicalSize = const Size(1200, 900);
      addTearDown(tester.view.resetDevicePixelRatio);
      addTearDown(tester.view.resetPhysicalSize);
      const draftA = 'draft-a';
      const draftB = 'draft-b';
      const locationA = '/features/proposals/$draftA';
      const locationB = '/features/proposals/$draftB';
      final transport = _RouterTransport(_RouterFeedCall());
      final owner = RuntimeSessionOwner(
        configuration: _configuration(),
        transportFactory: (_) => transport,
      );
      final router = createDigitalBrainRouter(initialLocation: locationA);

      await tester.pumpWidget(
        DigitalBrainApp(
          sessionOwnerFactory: () => owner,
          routerFactory: () => router,
        ),
      );
      await _pumpUntil(
        tester,
        () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
      );
      await tester.tap(find.byKey(runtimeSignInButtonKey));
      await _pumpUntil(tester, () => transport.getDraftIds.length == 1);
      await _pumpUntil(
        tester,
        () => find
            .byKey(const ValueKey('scenario-brief-name'))
            .evaluate()
            .isNotEmpty,
      );
      expect(
        tester
            .widget<TextFormField>(
              find.byKey(const ValueKey('scenario-brief-name')),
            )
            .controller
            ?.text,
        'Draft A Behavior',
      );

      router.go(locationB);
      await _pumpUntil(tester, () => transport.getDraftIds.length == 2);
      await _pumpUntil(tester, () {
        final field = find.byKey(const ValueKey('scenario-brief-name'));
        return field.evaluate().length == 1 &&
            tester.widget<TextFormField>(field).controller?.text ==
                'Draft B Behavior';
      });
      expect(transport.getDraftIds, [draftA, draftB]);
      expect(
        tester
            .widget<TextFormField>(
              find.byKey(const ValueKey('scenario-brief-name')),
            )
            .controller
            ?.text,
        'Draft B Behavior',
      );

      await tester.enterText(
        find.byKey(const ValueKey('scenario-brief-name')),
        'Only Draft B changed',
      );
      await tester.pump(const Duration(milliseconds: 501));
      await _pumpUntil(tester, () => transport.reviseRequests.isNotEmpty);
      expect(transport.reviseRequests, hasLength(1));
      expect(transport.reviseRequests.single.draftId, draftB);

      await tester.enterText(
        find.byKey(const ValueKey('scenario-brief-name')),
        '',
      );
      await tester.pump();
      await tester.tap(find.byTooltip('Chat'));
      await _pumpUntil(
        tester,
        () => find.byKey(featureStudioLeaveDialogKey).evaluate().isNotEmpty,
      );
      expect(router.routeInformationProvider.value.uri.path, locationB);
      await tester.tap(find.byKey(featureStudioDiscardButtonKey));
      await _pumpUntil(
        tester,
        () => router.routeInformationProvider.value.uri.path == '/chat',
      );

      await tester.pumpWidget(const SizedBox.shrink());
      await _pumpUntil(tester, () => transport.closeCalls > 0);
      expect(transport.closeCalls, 1);
    },
  );

  testWidgets('dirty Studio identity change requires Stay or Discard', (
    tester,
  ) async {
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(1200, 900);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPhysicalSize);
    const locationA = '/features/proposals/draft-a';
    const locationB = '/features/proposals/draft-b';
    final transport = _RouterTransport(_RouterFeedCall());
    final owner = RuntimeSessionOwner(
      configuration: _configuration(),
      transportFactory: (_) => transport,
    );
    final router = createDigitalBrainRouter(initialLocation: locationA);

    await tester.pumpWidget(
      DigitalBrainApp(
        sessionOwnerFactory: () => owner,
        routerFactory: () => router,
      ),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );
    await tester.tap(find.byKey(runtimeSignInButtonKey));
    await _pumpUntil(
      tester,
      () => find
          .byKey(const ValueKey('scenario-brief-name'))
          .evaluate()
          .isNotEmpty,
    );
    await tester.enterText(
      find.byKey(const ValueKey('scenario-brief-name')),
      '',
    );
    await tester.pump();

    router.go(locationB);
    await _pumpUntil(
      tester,
      () => find.byKey(featureStudioLeaveDialogKey).evaluate().isNotEmpty,
    );
    await tester.tap(find.byKey(featureStudioStayButtonKey));
    await _pumpUntil(
      tester,
      () => router.routeInformationProvider.value.uri.path == locationA,
    );
    expect(router.routeInformationProvider.value.uri.path, locationA);
    expect(
      tester
          .widget<TextFormField>(
            find.byKey(const ValueKey('scenario-brief-name')),
          )
          .controller
          ?.text,
      isEmpty,
    );

    router.go(locationB);
    await _pumpUntil(
      tester,
      () => find.byKey(featureStudioLeaveDialogKey).evaluate().isNotEmpty,
    );
    await tester.tap(find.byKey(featureStudioDiscardButtonKey));
    await _pumpUntil(
      tester,
      () => router.routeInformationProvider.value.uri.path == locationB,
    );
    await _pumpUntil(tester, () => transport.getDraftIds.length == 2);
    expect(transport.getDraftIds, ['draft-a', 'draft-b']);

    await tester.pumpWidget(const SizedBox.shrink());
    await _pumpUntil(tester, () => transport.closeCalls > 0);
    expect(transport.closeCalls, 1);
  });

  testWidgets(
    'Login to Chat to Studio to Chat keeps one controller and surface',
    (tester) async {
      final feed = _RouterFeedCall();
      final transport = _RouterTransport(feed);
      var transportCreations = 0;
      final owner = RuntimeSessionOwner(
        configuration: _configuration(),
        transportFactory: (_) {
          transportCreations++;
          return transport;
        },
      );
      final router = createDigitalBrainRouter();

      await tester.pumpWidget(
        DigitalBrainApp(
          sessionOwnerFactory: () => owner,
          routerFactory: () => router,
        ),
      );
      await _pumpUntil(
        tester,
        () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
      );
      final controller = owner.controller!;
      await tester.tap(find.byKey(runtimeSignInButtonKey));
      await _pumpUntil(tester, () => transport.watchCalls == 1);
      feed.add(
        FeedSurfaceJson(
          surfaceJsonString(
            sequence: 1,
            payload: inoConversationPayload(
              operation: inoOperation(
                state: 'succeeded',
                proposal: inoFeatureProposal(),
              ),
            ),
            actions: [testInoActionJson()],
          ),
        ),
      );
      await _pumpUntil(
        tester,
        () => find.byKey(chatOpenStudioButtonKey).evaluate().isNotEmpty,
      );
      final surface = controller.latestSurface;

      expect(
        find.descendant(
          of: find.byKey(digitalBrainCurrentContextKey),
          matching: find.text('Chat'),
        ),
        findsOneWidget,
      );
      expect(find.byKey(digitalBrainSignOutButtonKey), findsOneWidget);

      await tester.enterText(
        find.byKey(inoComposerFieldKey),
        'Keep this draft through Studio',
      );
      await tester.pump();
      expect(controller.session.identity?.ownerId, 'owner-a');
      expect(controller.session.identity?.actorId, 'actor-a');
      expect(find.textContaining(RegExp(r'owner-a|actor-a')), findsNothing);
      expect(
        tester
            .widget<TextField>(find.byKey(inoComposerFieldKey))
            .focusNode
            ?.hasFocus,
        isTrue,
      );

      await tester.tap(find.byKey(chatOpenStudioButtonKey));
      await tester.pumpAndSettle();
      expect(identical(owner.controller, controller), isTrue);
      expect(identical(controller.latestSurface, surface), isTrue);

      await tester.binding.handlePopRoute();
      await _pumpUntil(
        tester,
        () => router.routeInformationProvider.value.uri.path == '/chat',
      );
      await tester.pumpAndSettle();
      _expectRestoredChat(
        tester,
        owner: owner,
        controller: controller,
        surface: surface,
        draft: 'Keep this draft through Studio',
      );

      await tester.tap(find.byKey(chatOpenStudioButtonKey));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(featureStudioBackToChatButtonKey));
      await _pumpUntil(
        tester,
        () => router.routeInformationProvider.value.uri.path == '/chat',
      );
      await tester.pump(const Duration(milliseconds: 500));

      expect(find.byKey(runtimeSignInKey), findsNothing);
      expect(find.byKey(runtimeSurfaceKey), findsOneWidget);
      _expectRestoredChat(
        tester,
        owner: owner,
        controller: controller,
        surface: surface,
        draft: 'Keep this draft through Studio',
      );
      expect(transportCreations, 1);
      expect(transport.loginCalls, 1);
      expect(transport.watchCalls, 1);
      expect(transport.closeCalls, 0);

      await tester.tap(find.byKey(digitalBrainSignOutButtonKey));
      await _pumpUntil(
        tester,
        () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
      );

      await tester.pumpWidget(const SizedBox.shrink());
      await _pumpUntil(tester, () => transport.closeCalls > 0);
      expect(transport.closeCalls, 1);
    },
  );

  testWidgets(
    'Studio preserves dirty retry identity through forced reauthentication',
    (tester) async {
      const location =
          '/features/proposals/proposal-0123456789abcdef0123456789abcdef';
      final transport = _RouterTransport(
        _RouterFeedCall(),
        unauthenticateFirstRevise: true,
        failFirstRefresh: true,
      );
      final owner = RuntimeSessionOwner(
        configuration: _configuration(),
        transportFactory: (_) => transport,
      );
      final router = createDigitalBrainRouter(initialLocation: location);
      final app = DigitalBrainApp(
        sessionOwnerFactory: () => owner,
        routerFactory: () => router,
      );
      await tester.pumpWidget(app);
      await _pumpUntil(
        tester,
        () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
      );
      await tester.tap(find.byKey(runtimeSignInButtonKey));
      await _pumpUntil(
        tester,
        () => find
            .byKey(const ValueKey('scenario-brief-name'))
            .evaluate()
            .isNotEmpty,
      );
      final studioElement = tester.element(find.byType(FeatureStudioPage));

      await tester.enterText(
        find.byKey(const ValueKey('scenario-brief-name')),
        'Retained local brief',
      );
      await tester.pump(const Duration(milliseconds: 500));
      await _pumpUntil(
        tester,
        () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
      );

      expect(router.routeInformationProvider.value.uri.path, location);
      expect(transport.reviseRequests, hasLength(1));
      final rejectedRequest = transport.reviseRequests.single;
      final retainedField = find.byKey(
        const ValueKey('scenario-brief-name'),
        skipOffstage: false,
      );
      expect(find.byKey(const ValueKey('scenario-brief-name')), findsNothing);
      expect(retainedField, findsOneWidget);
      expect(
        tester.widget<TextFormField>(retainedField).controller?.text,
        'Retained local brief',
      );

      await tester.tap(find.byKey(runtimeSignInButtonKey));
      await _pumpUntil(
        tester,
        () => find
            .byKey(const ValueKey('scenario-brief-name'))
            .evaluate()
            .isNotEmpty,
      );

      expect(
        tester.element(find.byType(FeatureStudioPage)),
        same(studioElement),
      );
      expect(
        tester
            .widget<TextFormField>(
              find.byKey(const ValueKey('scenario-brief-name')),
            )
            .controller
            ?.text,
        'Retained local brief',
      );
      expect(find.text('Try again'), findsOneWidget);
      await tester.ensureVisible(find.text('Try again'));
      await tester.tap(find.text('Try again'));
      await _pumpUntil(tester, () => transport.reviseRequests.length == 2);

      final retriedRequest = transport.reviseRequests.last;
      expect(retriedRequest.idempotencyId, rejectedRequest.idempotencyId);
      expect(retriedRequest.writeToBuffer(), rejectedRequest.writeToBuffer());
      expect(transport.refreshCalls, 1);
      expect(transport.cancelProductCallCount, greaterThanOrEqualTo(1));
      expect(router.routeInformationProvider.value.uri.path, location);

      await tester.pumpWidget(const SizedBox.shrink());
      await _pumpUntil(tester, () => transport.closeCalls > 0);
      await tester.pump(const Duration(milliseconds: 300));
    },
  );

  testWidgets('forced reauthentication hides the open Studio Code disclosure', (
    tester,
  ) async {
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(320, 900);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPhysicalSize);
    const location =
        '/features/proposals/proposal-0123456789abcdef0123456789abcdef';
    final transport = _RouterTransport(_RouterFeedCall());
    final owner = RuntimeSessionOwner(
      configuration: _configuration(),
      transportFactory: (_) => transport,
    );
    final router = createDigitalBrainRouter(initialLocation: location);
    await tester.pumpWidget(
      DigitalBrainApp(
        sessionOwnerFactory: () => owner,
        routerFactory: () => router,
      ),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );
    await tester.tap(find.byKey(runtimeSignInButtonKey));
    await _pumpUntil(
      tester,
      () => find.byKey(featureStudioDraftIdKey).evaluate().isNotEmpty,
    );
    await _openCompactStudioCode(tester);
    const sourceKey = ValueKey('source-Feature/Feature.cs');
    final studioElement = tester.element(find.byType(FeatureStudioPage));
    final sourceElement = tester.element(find.byKey(sourceKey));
    expect(find.byKey(sourceKey), findsOneWidget);
    await tester.enterText(find.byKey(sourceKey), 'Retained source draft');
    expect(transport.reviseRequests, isEmpty);

    await owner.controller!.requireAuthentication();
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );

    expect(find.byKey(sourceKey), findsNothing);
    expect(
      find.byKey(sourceKey, skipOffstage: false).hitTestable(),
      findsNothing,
    );
    expect(find.byKey(runtimeSignInKey).hitTestable(), findsOneWidget);
    tester.testTextInput.enterText('Injected while signed out');
    await tester.sendKeyDownEvent(LogicalKeyboardKey.controlLeft);
    await tester.sendKeyEvent(LogicalKeyboardKey.keyS);
    await tester.sendKeyUpEvent(LogicalKeyboardKey.controlLeft);
    await tester.pump();
    expect(
      tester
          .widget<TextFormField>(find.byKey(sourceKey, skipOffstage: false))
          .controller
          ?.text,
      'Retained source draft',
    );
    expect(transport.reviseRequests, isEmpty);

    await tester.tap(find.byKey(runtimeSignInButtonKey));
    await _pumpUntil(tester, () => find.byKey(sourceKey).evaluate().isNotEmpty);
    expect(tester.element(find.byType(FeatureStudioPage)), same(studioElement));
    expect(tester.element(find.byKey(sourceKey)), same(sourceElement));
    expect(
      tester.widget<TextFormField>(find.byKey(sourceKey)).controller?.text,
      'Retained source draft',
    );
    await tester.tap(find.byKey(sourceKey));
    await tester.pump();
    expect(Focus.of(tester.element(find.byKey(sourceKey))).hasFocus, isTrue);

    await tester.pumpWidget(const SizedBox.shrink());
    await tester.pump(const Duration(milliseconds: 500));
  });

  testWidgets('sign out removes the open Studio Code disclosure', (
    tester,
  ) async {
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(320, 900);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPhysicalSize);
    const location =
        '/features/proposals/proposal-0123456789abcdef0123456789abcdef';
    final transport = _RouterTransport(_RouterFeedCall());
    final owner = RuntimeSessionOwner(
      configuration: _configuration(),
      transportFactory: (_) => transport,
    );
    final router = createDigitalBrainRouter(initialLocation: location);
    await tester.pumpWidget(
      DigitalBrainApp(
        sessionOwnerFactory: () => owner,
        routerFactory: () => router,
      ),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );
    await tester.tap(find.byKey(runtimeSignInButtonKey));
    await _pumpUntil(
      tester,
      () => find.byKey(featureStudioDraftIdKey).evaluate().isNotEmpty,
    );
    await _openCompactStudioCode(tester);
    const sourceKey = ValueKey('source-Feature/Feature.cs');
    expect(find.byKey(sourceKey), findsOneWidget);

    owner.signOut();
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );

    expect(find.byKey(sourceKey, skipOffstage: false), findsNothing);
    expect(find.byKey(runtimeSignInKey).hitTestable(), findsOneWidget);

    await tester.pumpWidget(const SizedBox.shrink());
    await tester.pump(const Duration(milliseconds: 300));
  });

  testWidgets('forced reauthentication hides an open Studio leave dialog', (
    tester,
  ) async {
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(1200, 900);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPhysicalSize);
    const location =
        '/features/proposals/proposal-0123456789abcdef0123456789abcdef';
    final transport = _RouterTransport(_RouterFeedCall());
    final owner = RuntimeSessionOwner(
      configuration: _configuration(),
      transportFactory: (_) => transport,
    );
    final router = createDigitalBrainRouter(initialLocation: location);
    await tester.pumpWidget(
      DigitalBrainApp(
        sessionOwnerFactory: () => owner,
        routerFactory: () => router,
      ),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );
    await tester.tap(find.byKey(runtimeSignInButtonKey));
    await _pumpUntil(
      tester,
      () => find
          .byKey(const ValueKey('scenario-brief-name'))
          .evaluate()
          .isNotEmpty,
    );
    await tester.enterText(
      find.byKey(const ValueKey('scenario-brief-name')),
      '',
    );
    await tester.tap(find.byTooltip('Chat'));
    await _pumpUntil(
      tester,
      () => find.byKey(featureStudioLeaveDialogKey).evaluate().isNotEmpty,
    );

    await owner.controller!.requireAuthentication();
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );

    expect(find.byKey(featureStudioLeaveDialogKey), findsNothing);
    expect(
      find
          .byKey(featureStudioLeaveDialogKey, skipOffstage: false)
          .hitTestable(),
      findsNothing,
    );
    expect(find.byKey(runtimeSignInKey).hitTestable(), findsOneWidget);

    await tester.pumpWidget(const SizedBox.shrink());
    await tester.pump(const Duration(milliseconds: 500));
  });

  testWidgets('sign out removes an open Studio leave dialog', (tester) async {
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(1200, 900);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPhysicalSize);
    const location =
        '/features/proposals/proposal-0123456789abcdef0123456789abcdef';
    final transport = _RouterTransport(_RouterFeedCall());
    final owner = RuntimeSessionOwner(
      configuration: _configuration(),
      transportFactory: (_) => transport,
    );
    final router = createDigitalBrainRouter(initialLocation: location);
    await tester.pumpWidget(
      DigitalBrainApp(
        sessionOwnerFactory: () => owner,
        routerFactory: () => router,
      ),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );
    await tester.tap(find.byKey(runtimeSignInButtonKey));
    await _pumpUntil(
      tester,
      () => find
          .byKey(const ValueKey('scenario-brief-name'))
          .evaluate()
          .isNotEmpty,
    );
    await tester.enterText(
      find.byKey(const ValueKey('scenario-brief-name')),
      '',
    );
    await tester.tap(find.byTooltip('Chat'));
    await _pumpUntil(
      tester,
      () => find.byKey(featureStudioLeaveDialogKey).evaluate().isNotEmpty,
    );

    owner.signOut();
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );

    expect(
      find.byKey(featureStudioLeaveDialogKey, skipOffstage: false),
      findsNothing,
    );
    expect(find.byKey(runtimeSignInKey).hitTestable(), findsOneWidget);

    await tester.pumpWidget(const SizedBox.shrink());
    await tester.pump(const Duration(milliseconds: 500));
  });

  testWidgets('authentication loss during exit save never opens a dialog', (
    tester,
  ) async {
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(1200, 900);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPhysicalSize);
    const location =
        '/features/proposals/proposal-0123456789abcdef0123456789abcdef';
    final transport = _RouterTransport(
      _RouterFeedCall(),
      unauthenticateFirstRevise: true,
      failFirstRefresh: true,
    );
    final owner = RuntimeSessionOwner(
      configuration: _configuration(),
      transportFactory: (_) => transport,
    );
    final router = createDigitalBrainRouter(initialLocation: location);
    await tester.pumpWidget(
      DigitalBrainApp(
        sessionOwnerFactory: () => owner,
        routerFactory: () => router,
      ),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );
    await tester.tap(find.byKey(runtimeSignInButtonKey));
    await _pumpUntil(
      tester,
      () => find
          .byKey(const ValueKey('scenario-brief-name'))
          .evaluate()
          .isNotEmpty,
    );
    await tester.enterText(
      find.byKey(const ValueKey('scenario-brief-name')),
      'Exit save requiring authentication',
    );
    await tester.tap(find.byKey(featureStudioBackToChatButtonKey));
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );
    await tester.pump();

    expect(transport.reviseRequests, hasLength(1));
    expect(
      find.byKey(featureStudioLeaveDialogKey, skipOffstage: false),
      findsNothing,
    );
    expect(find.byKey(runtimeSignInKey).hitTestable(), findsOneWidget);

    await tester.pumpWidget(const SizedBox.shrink());
    await tester.pump(const Duration(milliseconds: 500));
  });

  testWidgets('backend app shell fails closed inside the trusted Chat shell', (
    tester,
  ) async {
    final feed = _RouterFeedCall();
    final transport = _RouterTransport(feed);
    final owner = RuntimeSessionOwner(
      configuration: _configuration(),
      transportFactory: (_) => transport,
    );
    final router = createDigitalBrainRouter();

    await tester.pumpWidget(
      DigitalBrainApp(
        sessionOwnerFactory: () => owner,
        routerFactory: () => router,
      ),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );
    await tester.tap(find.byKey(runtimeSignInButtonKey));
    await _pumpUntil(tester, () => transport.watchCalls == 1);
    feed.add(
      FeedSurfaceJson(
        surfaceJsonString(
          payload: <String, Object?>{
            'kind': 'widgetTree',
            'tree': <String, Object?>{
              'Type': 'ui:Screen',
              'Props': <String, Object?>{},
              'Children': <Object?>[
                <String, Object?>{
                  'Type': 'app-shell',
                  'Props': <String, Object?>{
                    'title': 'Untrusted product header',
                  },
                  'Children': <Object?>[
                    <String, Object?>{
                      'Type': 'sidebar',
                      'Props': <String, Object?>{
                        'label': 'Untrusted navigation',
                      },
                    },
                  ],
                },
              ],
            },
            'data': <String, Object?>{},
          },
        ),
      ),
    );
    await _pumpUntil(
      tester,
      () =>
          find.text('This view could not be displayed.').evaluate().isNotEmpty,
    );

    expect(find.textContaining('Untrusted'), findsNothing);
    expect(find.byTooltip('Chat'), findsOneWidget);
    expect(
      find.descendant(
        of: find.byKey(digitalBrainCurrentContextKey),
        matching: find.text('Chat'),
      ),
      findsOneWidget,
    );
    expect(find.byKey(digitalBrainSignOutButtonKey), findsOneWidget);

    await tester.pumpWidget(const SizedBox.shrink());
    await _pumpUntil(tester, () => transport.closeCalls > 0);
    expect(transport.closeCalls, 1);
  });

  testWidgets('responsive shell keeps the focused Chat canvas alive', (
    tester,
  ) async {
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(599, 900);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPhysicalSize);
    final feed = _RouterFeedCall();
    final transport = _RouterTransport(feed);
    final owner = RuntimeSessionOwner(
      configuration: _configuration(),
      transportFactory: (_) => transport,
    );
    final router = createDigitalBrainRouter();

    await tester.pumpWidget(
      DigitalBrainApp(
        sessionOwnerFactory: () => owner,
        routerFactory: () => router,
      ),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );
    await tester.tap(find.byKey(runtimeSignInButtonKey));
    await _pumpUntil(tester, () => transport.watchCalls == 1);
    feed.add(
      FeedSurfaceJson(
        surfaceJsonString(
          sequence: 1,
          payload: inoConversationPayload(),
          actions: [testInoActionJson()],
        ),
      ),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(inoComposerFieldKey).evaluate().isNotEmpty,
    );
    final controller = owner.controller!;
    final surface = controller.latestSurface;
    await tester.enterText(
      find.byKey(inoComposerFieldKey),
      'Keep this draft while resizing',
    );
    await tester.pump();

    for (final width in <double>[600, 1200, 320]) {
      tester.view.physicalSize = Size(width, 900);
      await tester.pumpAndSettle();

      final composer = tester.widget<TextField>(
        find.byKey(inoComposerFieldKey),
      );
      expect(composer.controller?.text, 'Keep this draft while resizing');
      expect(composer.focusNode?.hasFocus, isTrue);
      expect(identical(owner.controller, controller), isTrue);
      expect(identical(controller.latestSurface, surface), isTrue);
      expect(transport.watchCalls, 1);
    }

    await tester.pumpWidget(const SizedBox.shrink());
    await _pumpUntil(tester, () => transport.closeCalls > 0);
    expect(transport.closeCalls, 1);
  });

  testWidgets('Activity list opens a canonical deep-linked Run detail', (
    tester,
  ) async {
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(390, 900);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPhysicalSize);
    final run = _wireActivityRun();
    final transport = _RouterTransport(
      _RouterFeedCall(),
      activityReply: wire.ListActivityReply(runs: [run]),
      runReply: wire.RunReply(run: run),
    );
    final owner = RuntimeSessionOwner(
      configuration: _configuration(),
      transportFactory: (_) => transport,
    );
    final router = createDigitalBrainRouter(initialLocation: '/activity');

    await tester.pumpWidget(
      DigitalBrainApp(
        sessionOwnerFactory: () => owner,
        routerFactory: () => router,
      ),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );
    await tester.tap(find.byKey(runtimeSignInButtonKey));
    await _pumpUntil(tester, () => transport.activityRequests.length == 1);
    await _pumpUntil(
      tester,
      () => find.byType(ActivityPage).evaluate().isNotEmpty,
    );

    expect(transport.activityRequests.single.limit, 200);
    expect(find.text('Research brief'), findsOneWidget);
    expect(router.routeInformationProvider.value.uri.path, '/activity');

    await tester.tap(find.byKey(activityStatusFilterKey));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Completed').last);
    await _pumpUntil(tester, () => transport.activityRequests.length == 2);
    await _pumpUntil(
      tester,
      () => find.byKey(activityRunCardKey('run-a')).evaluate().isNotEmpty,
    );
    expect(find.text('Status: Completed'), findsOneWidget);

    await tester.ensureVisible(find.byKey(activityRunCardKey('run-a')));
    await tester.tap(find.byKey(activityRunCardKey('run-a')));
    await _pumpUntil(tester, () => transport.runRequests.length == 1);
    await _pumpUntil(
      tester,
      () => find.byType(ActivityRunPage).evaluate().isNotEmpty,
    );

    expect(transport.runRequests.single.runId, 'run-a');
    expect(router.routeInformationProvider.value.uri.path, '/activity/run-a');
    expect(router.canPop(), isTrue);
    expect(find.byType(AppBar), findsOneWidget);
    expect(
      find.descendant(
        of: find.byKey(digitalBrainCurrentContextKey),
        matching: find.text('Run details'),
      ),
      findsOneWidget,
    );
    expect(find.byKey(activityTechnicalDetailsKey), findsOneWidget);
    expect(find.text('Back to Activity'), findsOneWidget);

    await tester.tap(find.text('Back to Activity'));
    await _pumpUntil(
      tester,
      () => router.routeInformationProvider.value.uri.path == '/activity',
    );
    await _pumpUntil(
      tester,
      () => find.byType(ActivityPage).evaluate().isNotEmpty,
    );
    await tester.pumpAndSettle();

    expect(find.byType(ActivityPage), findsOneWidget);
    expect(find.text('Status: Completed'), findsOneWidget);
    expect(transport.activityRequests, hasLength(2));

    await tester.ensureVisible(find.byKey(activityRunCardKey('run-a')));
    await tester.tap(find.byKey(activityRunCardKey('run-a')));
    await _pumpUntil(tester, () => transport.runRequests.length == 2);
    await _pumpUntil(
      tester,
      () => find.byKey(activityOpenChatButtonKey).evaluate().isNotEmpty,
    );
    await tester.ensureVisible(find.byKey(activityOpenChatButtonKey));
    await tester.tap(find.byKey(activityOpenChatButtonKey));
    await _pumpUntil(
      tester,
      () => router.routeInformationProvider.value.uri.path == '/chat',
    );

    expect(
      router.routeInformationProvider.value.uri.queryParameters,
      containsPair('conversationId', 'conversation-a'),
    );
    expect(find.byKey(chatActivityContextKey), findsOneWidget);
    expect(find.text('Conversation conversation-a'), findsOneWidget);

    await tester.pumpWidget(const SizedBox.shrink());
    await _pumpUntil(tester, () => transport.closeCalls > 0);
    expect(transport.closeCalls, 1);
  });

  testWidgets('deep-linked Run has a real Back to Activity fallback', (
    tester,
  ) async {
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(390, 900);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPhysicalSize);
    final run = _wireActivityRun();
    final transport = _RouterTransport(
      _RouterFeedCall(),
      activityReply: wire.ListActivityReply(runs: [run]),
      runReply: wire.RunReply(run: run),
    );
    final owner = RuntimeSessionOwner(
      configuration: _configuration(),
      transportFactory: (_) => transport,
    );
    final router = createDigitalBrainRouter(initialLocation: '/activity/run-a');

    await tester.pumpWidget(
      DigitalBrainApp(
        sessionOwnerFactory: () => owner,
        routerFactory: () => router,
      ),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );
    await tester.tap(find.byKey(runtimeSignInButtonKey));
    await _pumpUntil(tester, () => transport.runRequests.length == 1);
    await _pumpUntil(
      tester,
      () => find.byType(ActivityRunPage).evaluate().isNotEmpty,
    );

    expect(router.routeInformationProvider.value.uri.path, '/activity/run-a');
    expect(router.canPop(), isFalse);
    expect(find.byType(AppBar), findsOneWidget);
    expect(find.text('Back to Activity'), findsOneWidget);

    await tester.tap(find.text('Back to Activity'));
    await _pumpUntil(
      tester,
      () => router.routeInformationProvider.value.uri.path == '/activity',
    );
    await _pumpUntil(
      tester,
      () => find.byType(ActivityPage).evaluate().isNotEmpty,
    );
    await _pumpUntil(tester, () => transport.activityRequests.length == 1);

    expect(find.byType(ActivityPage), findsOneWidget);
    expect(transport.activityRequests, hasLength(1));

    await tester.pumpWidget(const SizedBox.shrink());
    await _pumpUntil(tester, () => transport.closeCalls > 0);
    expect(transport.closeCalls, 1);
  });

  testWidgets('Activity Automation link focuses the matching Feature binding', (
    tester,
  ) async {
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(390, 900);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPhysicalSize);
    final run = _wireActivityRun()
      ..runId = 'run-automation'
      ..origin = wire_enums.FeatureRunOrigin.FEATURE_RUN_ORIGIN_SCHEDULE
      ..originReference = wire.FeatureRunOriginReference(
        automationId: 'schedule:weekday',
      );
    final transport = _RouterTransport(
      _RouterFeedCall(),
      runReply: wire.RunReply(run: run),
      featureReply: wireReleaseDetails(),
    );
    final owner = RuntimeSessionOwner(
      configuration: _configuration(),
      transportFactory: (_) => transport,
    );
    final router = createDigitalBrainRouter(
      initialLocation: '/activity/run-automation',
    );

    await tester.pumpWidget(
      DigitalBrainApp(
        sessionOwnerFactory: () => owner,
        routerFactory: () => router,
      ),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );
    await tester.tap(find.byKey(runtimeSignInButtonKey));
    await _pumpUntil(tester, () => transport.runRequests.length == 1);
    await _pumpUntil(
      tester,
      () => find.byKey(activityOpenAutomationButtonKey).evaluate().isNotEmpty,
    );

    await tester.ensureVisible(find.byKey(activityOpenAutomationButtonKey));
    await tester.tap(find.byKey(activityOpenAutomationButtonKey));
    await _pumpUntil(tester, () => transport.getFeatureRequests.length == 1);
    await _pumpUntil(
      tester,
      () => find.byKey(featureReleaseReferencedAutomationKey).evaluate().isNotEmpty,
    );

    expect(router.routeInformationProvider.value.uri.path, '/features/feature-a');
    expect(
      router.routeInformationProvider.value.uri.queryParameters,
      containsPair('automationId', 'schedule:weekday'),
    );
    expect(find.byKey(featureReleaseReferencedAutomationKey), findsOneWidget);

    await tester.pumpWidget(const SizedBox.shrink());
    await _pumpUntil(tester, () => transport.closeCalls > 0);
    expect(transport.closeCalls, 1);
  });
}

Future<void> _openCompactStudioCode(WidgetTester tester) async {
  for (
    var attempt = 0;
    attempt < 8 && find.byKey(featureStudioOpenCodeKey).evaluate().isEmpty;
    attempt++
  ) {
    await tester.drag(find.byType(ListView).last, const Offset(0, -300));
    await tester.pumpAndSettle();
  }
  expect(find.byKey(featureStudioOpenCodeKey), findsOneWidget);
  await tester.tap(find.byKey(featureStudioOpenCodeKey));
  await tester.pumpAndSettle();
  final implementationFile = find.text('Feature/Feature.cs').last;
  await tester.ensureVisible(implementationFile);
  await tester.tap(implementationFile);
  await tester.pumpAndSettle();
}

void _expectRestoredChat(
  WidgetTester tester, {
  required RuntimeSessionOwner owner,
  required RuntimeController controller,
  required SurfaceEnvelope? surface,
  required String draft,
}) {
  final restoredComposer = tester.widget<TextField>(
    find.byKey(inoComposerFieldKey),
  );
  expect(restoredComposer.controller?.text, draft);
  expect(restoredComposer.focusNode?.hasFocus, isTrue);
  expect(identical(owner.controller, controller), isTrue);
  expect(identical(controller.latestSurface, surface), isTrue);
}

RuntimeConfiguration _configuration() => RuntimeConfiguration(
  endpoint: Uri.parse('https://localhost:7443'),
  externalIdentity: null,
);

const _resumeOperationId =
    'runtime-op-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';

Future<void> _pumpUntil(WidgetTester tester, bool Function() condition) async {
  for (var attempt = 0; attempt < 100; attempt++) {
    await tester.pump(const Duration(milliseconds: 1));
    if (condition()) return;
  }
  fail('Widget condition was not reached.');
}

class _RouterTransport
    implements
        UiTransport,
        DigitalBrainTransport,
        SessionProductCallCancellation {
  _RouterTransport(
    this.feed, {
    this.unauthenticateFirstRevise = false,
    this.failFirstRefresh = false,
    this.failFirstResume = false,
    this.resumeReply,
    this.featureReply,
    this.rollbackReply,
    this.activityReply,
    this.runReply,
  });

  final _RouterFeedCall feed;
  final bool unauthenticateFirstRevise;
  final bool failFirstRefresh;
  final bool failFirstResume;
  wire.ResumeOriginatingRequestReply? resumeReply;
  wire.FeatureReply? featureReply;
  final wire.FeatureReply? rollbackReply;
  final wire.ListActivityReply? activityReply;
  final wire.RunReply? runReply;
  int loginCalls = 0;
  int watchCalls = 0;
  int closeCalls = 0;
  int getDraftCalls = 0;
  final List<String> getDraftIds = [];
  int refreshCalls = 0;
  int cancelProductCallCount = 0;
  final List<wire.ReviseFeatureDraftRequest> reviseRequests = [];
  final List<wire.ResumeOriginatingRequestRequest> resumeRequests = [];
  final List<wire.GetFeatureRequest> getFeatureRequests = [];
  final List<wire.GetFeatureReleaseSourceRequest> releaseSourceRequests = [];
  final List<wire.RollbackFeatureVersionRequest> rollbackRequests = [];
  final List<wire.ListActivityRequest> activityRequests = [];
  final List<wire.GetRunRequest> runRequests = [];

  @override
  Future<SessionBundle> login({
    required String username,
    required String password,
  }) async {
    loginCalls++;
    return testSession();
  }

  @override
  Future<SessionBundle> refreshSession({required String refreshToken}) async {
    refreshCalls++;
    if (failFirstRefresh && refreshCalls == 1) {
      throw const AuthenticationException();
    }
    return testSession(accessToken: 'access-refreshed');
  }

  @override
  Future<void> cancelProductCalls() async {
    cancelProductCallCount++;
  }

  @override
  Future<void> logout({required String refreshToken}) async {}

  @override
  Future<FeedCall> watchSurfaceFeed({
    required String accessToken,
    required int afterSequence,
    required FeedAudience audience,
    required Set<String> clientCapabilities,
    required int maxBatchSize,
  }) async {
    watchCalls++;
    return feed;
  }

  @override
  Future<void> acknowledgeSurfaceFeed({
    required String accessToken,
    required FeedAudience audience,
    required int sequence,
  }) async {}

  @override
  Future<ActionResult> submitAction({
    required String accessToken,
    required UiActionRef action,
    required Map<String, Object?> input,
  }) async => const ActionResult(
    operationId: 'operation-router',
    idempotencyKey: 'idempotency-router',
  );

  @override
  Future<void> close() async {
    closeCalls++;
  }

  @override
  Future<wire.FeatureDraftReply> getFeatureDraft({
    required String accessToken,
    required wire.GetFeatureDraftRequest request,
  }) async {
    getDraftCalls++;
    getDraftIds.add(request.draftId);
    return wire.FeatureDraftReply(draft: _wireDraft(request.draftId));
  }

  @override
  Future<wire.FeatureDraftReply> resetFeatureDraftInstallation({
    required String accessToken,
    required wire.ResetFeatureDraftInstallationRequest request,
  }) async => wire.FeatureDraftReply(
    draft: _wireDraft(request.draftId)..revision = Int64.ONE,
  );

  @override
  Future<wire.FeatureDraftReply> reviseFeatureDraft({
    required String accessToken,
    required wire.ReviseFeatureDraftRequest request,
  }) async {
    reviseRequests.add(request);
    if (unauthenticateFirstRevise && reviseRequests.length == 1) {
      throw const AuthenticationException();
    }
    final draft = _wireDraft(request.draftId)
      ..revision = request.expectedRevision + 1;
    if (request.hasReviseBehavior()) {
      draft.behavior = request.reviseBehavior.behavior;
    }
    if (request.hasReviseSource()) {
      draft.source = request.reviseSource.source;
    }
    return wire.FeatureDraftReply(draft: draft);
  }

  @override
  Future<wire.FeatureDraftPatchReply> suggestFeatureChange({
    required String accessToken,
    required wire.SuggestFeatureChangeRequest request,
  }) async => wire.FeatureDraftPatchReply();

  @override
  Future<wire.FeatureReleaseReviewReply> verifyFeatureDraft({
    required String accessToken,
    required wire.VerifyFeatureDraftRequest request,
  }) async => wire.FeatureReleaseReviewReply();

  @override
  Future<wire.FeatureAccessReviewReply> reviewFeatureAccess({
    required String accessToken,
    required wire.ReviewFeatureAccessRequest request,
  }) async => wire.FeatureAccessReviewReply();

  @override
  Future<wire.FeatureInstallReply> installFeatureVersion({
    required String accessToken,
    required wire.InstallFeatureVersionRequest request,
  }) async => wire.FeatureInstallReply();

  @override
  Future<wire.ResumeOriginatingRequestReply> resumeOriginatingRequest({
    required String accessToken,
    required wire.ResumeOriginatingRequestRequest request,
  }) async {
    resumeRequests.add(request.deepCopy());
    if (failFirstResume && resumeRequests.length == 1) {
      throw const ProtocolException('unsafe backend detail');
    }
    return resumeReply ??
        wire.ResumeOriginatingRequestReply(
          commandId: request.idempotencyId,
          operationId: _resumeOperationId,
          phase: 'Accepted',
          version: Int64.ONE,
        );
  }

  @override
  Future<wire.FeatureReply> getFeature({
    required String accessToken,
    required wire.GetFeatureRequest request,
  }) async {
    getFeatureRequests.add(request);
    return featureReply ?? wire.FeatureReply();
  }

  @override
  Future<wire.FeatureReleaseSourceReply> getFeatureReleaseSource({
    required String accessToken,
    required wire.GetFeatureReleaseSourceRequest request,
  }) async {
    releaseSourceRequests.add(request.deepCopy());
    return wireReleaseSource(request.releaseDigest[0]);
  }

  @override
  Future<wire.FeatureReply> rollbackFeatureVersion({
    required String accessToken,
    required wire.RollbackFeatureVersionRequest request,
  }) async {
    rollbackRequests.add(request);
    final reply = rollbackReply ?? wire.FeatureReply();
    featureReply = reply;
    return reply;
  }

  @override
  Future<wire.ListActivityReply> listActivity({
    required String accessToken,
    required wire.ListActivityRequest request,
  }) async {
    activityRequests.add(request.deepCopy());
    return activityReply ?? wire.ListActivityReply();
  }

  @override
  Future<wire.RunReply> getRun({
    required String accessToken,
    required wire.GetRunRequest request,
  }) async {
    runRequests.add(request.deepCopy());
    return runReply ?? wire.RunReply();
  }
}

wire.FeatureRunSnapshot _wireActivityRun() => wire.FeatureRunSnapshot(
  runId: 'run-a',
  featureId: 'feature-a',
  featureName: 'Research brief',
  installationId: 'installation-a',
  releaseDigest: 'a' * 64,
  inputKind: 'chat.request',
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

wire.FeatureDraft _wireDraft(String draftId) => wire.FeatureDraft(
  draftId: draftId,
  originatingRequest: wire.OriginatingRequest(
    operationId: 'operation-router',
    conversationId: 'conversation-router',
    text: 'Research Acme',
  ),
  goal: 'Create a concise company brief',
  status: wire.FeatureDraftStatus.FEATURE_DRAFT_STATUS_DRAFT,
  behavior: wire.FeatureBehavior(
    scenarios: [
      wire.FeatureScenario(
        scenarioId: 'brief',
        name: switch (draftId) {
          'draft-a' => 'Draft A Behavior',
          'draft-b' => 'Draft B Behavior',
          _ => 'Create a brief',
        },
        given: 'A company name',
        when: 'The Feature runs',
        then: 'A concise brief is returned',
      ),
    ],
  ),
  source: wire.FeatureSourceSnapshot(
    implementationProjectPath: 'Feature/Feature.csproj',
    scenarioProjectPath: 'Feature.Tests/Feature.Tests.csproj',
    files: [
      wire.FeatureSourceFile(
        path: 'Feature/Feature.csproj',
        content: '<Project Sdk="Microsoft.NET.Sdk" />',
      ),
      wire.FeatureSourceFile(
        path: 'Feature.Tests/Feature.Tests.csproj',
        content: '<Project Sdk="Microsoft.NET.Sdk" />',
      ),
      wire.FeatureSourceFile(path: 'Feature/Feature.cs', content: 'source'),
    ],
  ),
  revision: Int64(4),
  createdAtUnixMs: Int64(1_752_537_600_000),
  updatedAtUnixMs: Int64(1_752_537_660_000),
);

class _RouterFeedCall implements FeedCall {
  final StreamController<FeedEvent> _controller = StreamController<FeedEvent>();

  void add(FeedEvent event) => _controller.add(event);

  @override
  Stream<FeedEvent> get events => _controller.stream;

  @override
  Future<void> cancel() async {
    if (!_controller.isClosed) await _controller.close();
  }
}
