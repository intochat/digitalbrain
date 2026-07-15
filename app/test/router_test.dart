import 'dart:async';

import 'package:digitalbrain_flutter/app.dart';
import 'package:digitalbrain_flutter/router.dart';
import 'package:digitalbrain_flutter/runtime/protocol/surface_protocol.dart';
import 'package:digitalbrain_flutter/runtime/runtime.dart';
import 'package:digitalbrain_flutter/runtime/runtime_configuration.dart';
import 'package:digitalbrain_flutter/runtime/runtime_session_owner.dart';
import 'package:digitalbrain_flutter/runtime/widgets/feature_proposal_placeholder.dart';
import 'package:digitalbrain_flutter/runtime/widgets/ino_composer.dart';
import 'package:digitalbrain_flutter/runtime/widgets/ino_conversation_view.dart';
import 'package:digitalbrain_flutter/runtime/widgets/runtime_shell.dart';
import 'package:digitalbrain_flutter/shell/digitalbrain_shell.dart';
import 'package:flutter/material.dart';
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
    expect(find.byKey(featureProposalIdKey), findsNothing);

    await tester.tap(find.byKey(runtimeSignInButtonKey));
    await _pumpUntil(
      tester,
      () => find.byKey(featureProposalIdKey).evaluate().isNotEmpty,
    );

    expect(router.routeInformationProvider.value.uri.path, location);
    expect(
      find.text('proposal-0123456789abcdef0123456789abcdef'),
      findsOneWidget,
    );
    expect(
      find.descendant(
        of: find.byKey(digitalBrainCurrentContextKey),
        matching: find.text('Feature Studio'),
      ),
      findsOneWidget,
    );
    expect(find.byKey(digitalBrainSignOutButtonKey), findsOneWidget);

    await tester.tap(find.byKey(featureProposalBackToChatButtonKey));
    await _pumpUntil(
      tester,
      () => router.routeInformationProvider.value.uri.path == '/chat',
    );
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
      await tester.pump(const Duration(milliseconds: 500));
      _expectRestoredChat(
        tester,
        owner: owner,
        controller: controller,
        surface: surface,
        draft: 'Keep this draft through Studio',
      );

      await tester.tap(find.byKey(chatOpenStudioButtonKey));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(featureProposalBackToChatButtonKey));
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

class _RouterTransport implements UiTransport {
  _RouterTransport(this.feed);

  final _RouterFeedCall feed;
  int loginCalls = 0;
  int watchCalls = 0;
  int closeCalls = 0;

  @override
  Future<SessionBundle> login({
    required String username,
    required String password,
  }) async {
    loginCalls++;
    return testSession();
  }

  @override
  Future<SessionBundle> refreshSession({required String refreshToken}) async =>
      testSession();

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
}

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
