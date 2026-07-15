import 'dart:async';

import 'package:digitalbrain_flutter/app.dart';
import 'package:digitalbrain_flutter/core/session/digitalbrain_client.dart';
import 'package:digitalbrain_flutter/features/studio/feature_studio_page.dart';
import 'package:digitalbrain_flutter/grpc/ui.pb.dart' as wire;
import 'package:digitalbrain_flutter/router.dart';
import 'package:digitalbrain_flutter/runtime/protocol/surface_protocol.dart';
import 'package:digitalbrain_flutter/runtime/runtime.dart';
import 'package:digitalbrain_flutter/runtime/runtime_configuration.dart';
import 'package:digitalbrain_flutter/runtime/runtime_session_owner.dart';
import 'package:digitalbrain_flutter/runtime/widgets/ino_composer.dart';
import 'package:digitalbrain_flutter/runtime/widgets/ino_conversation_view.dart';
import 'package:digitalbrain_flutter/runtime/widgets/runtime_shell.dart';
import 'package:digitalbrain_flutter/shell/digitalbrain_shell.dart';
import 'package:fixnum/fixnum.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

import 'runtime/test_fixtures.dart';

void main() {
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
  });

  final _RouterFeedCall feed;
  final bool unauthenticateFirstRevise;
  final bool failFirstRefresh;
  int loginCalls = 0;
  int watchCalls = 0;
  int closeCalls = 0;
  int getDraftCalls = 0;
  final List<String> getDraftIds = [];
  int refreshCalls = 0;
  int cancelProductCallCount = 0;
  final List<wire.ReviseFeatureDraftRequest> reviseRequests = [];

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
}

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
