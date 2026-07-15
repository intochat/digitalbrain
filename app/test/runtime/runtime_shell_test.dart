import 'dart:async';

import 'package:digitalbrain_flutter/core/session/app_session_scope.dart';
import 'package:digitalbrain_flutter/router.dart';
import 'package:digitalbrain_flutter/runtime/external_identity.dart';
import 'package:digitalbrain_flutter/runtime/protocol/surface_protocol.dart';
import 'package:digitalbrain_flutter/runtime/runtime_configuration.dart';
import 'package:digitalbrain_flutter/runtime/runtime.dart';
import 'package:digitalbrain_flutter/runtime/runtime_session_owner.dart';
import 'package:digitalbrain_flutter/runtime/widgets/chat_page.dart';
import 'package:digitalbrain_flutter/runtime/widgets/ino_composer.dart';
import 'package:digitalbrain_flutter/runtime/widgets/ino_conversation_view.dart';
import 'package:digitalbrain_flutter/runtime/widgets/runtime_shell.dart';
import 'package:digitalbrain_flutter/runtime/widgets/surface_view.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'test_fixtures.dart';

void main() {
  testWidgets('normal authenticated runtime shell renders its first surface', (
    tester,
  ) async {
    final feed = _ShellFeedCall.open();
    final transport = _ShellTransport(feed);
    final runtime = _runtime(transport);

    await tester.pumpWidget(_runtimeHost(controller: runtime));
    await _pumpUntil(tester, () => transport.watchStarted);
    feed.add(
      FeedSurfaceJson(
        surfaceJsonString(
          sequence: 1,
          payload: inoConversationPayload(),
          actions: [testInoActionJson()],
        ),
      ),
    );
    await _pumpUntil(tester, () => runtime.latestSurface != null);

    expect(find.byKey(runtimeSurfaceKey), findsOneWidget);
    expect(find.text('Ask INO about this workspace.'), findsOneWidget);
    expect(find.byKey(inoComposerFieldKey), findsOneWidget);
    await runtime.stop();
  });

  testWidgets('feature approval stays on the authenticated chat feed route', (
    tester,
  ) async {
    final feed = _ShellFeedCall.open();
    final transport = _ShellTransport(feed);
    final runtime = _runtime(transport);

    await tester.pumpWidget(_runtimeHost(controller: runtime));
    await _pumpUntil(tester, () => transport.watchStarted);
    feed.add(
      FeedSurfaceJson(
        surfaceJsonString(
          sequence: 1,
          payload: featureApprovalPayload(),
          actions: [
            testActionJson(
              bindingId: 'feature-approval-${List.filled(64, 'a').join()}',
              actionType: 'feature.release.decision.v1',
            ),
          ],
        ),
      ),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(featureApprovalApproveKey).evaluate().isNotEmpty,
    );
    await tester.ensureVisible(find.byKey(featureApprovalApproveKey));
    await tester.tap(find.byKey(featureApprovalApproveKey));
    await _pumpUntil(tester, () => transport.submittedInput != null);

    expect(
      transport.submittedAction?.bindingId,
      'feature-approval-${List.filled(64, 'a').join()}',
    );
    expect(
      transport.submittedInput?['releaseDigest'],
      List.filled(64, 'b').join(),
    );
    expect(transport.submittedInput?['expectedRevision'], 7);
    expect(transport.submittedInput?['decision'], 'approve');

    await runtime.stop();
  });

  testWidgets('app session owner asynchronously closes its transport', (
    tester,
  ) async {
    final transport = _ShellTransport(_ShellFeedCall.open());
    Uri? connectedEndpoint;

    await tester.pumpWidget(
      _runtimeHost(
        transportFactory: (endpoint) {
          connectedEndpoint = endpoint;
          return transport;
        },
      ),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );
    await tester.tap(find.byKey(runtimeSignInButtonKey));
    await _pumpUntil(tester, () => transport.watchStarted);

    expect(connectedEndpoint, Uri.parse('https://localhost:7443'));

    await tester.pumpWidget(const SizedBox.shrink());
    await _pumpUntil(tester, () => transport.closeCount > 0);

    expect(transport.closeCount, 1);
  });

  testWidgets('shell hides transport construction failures', (tester) async {
    await tester.pumpWidget(
      _runtimeHost(
        transportFactory: (_) => throw StateError('private transport error'),
      ),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeTerminalErrorKey).evaluate().isNotEmpty,
    );

    expect(
      find.text('DigitalBrain could not start. Please try again.'),
      findsOneWidget,
    );
    expect(find.textContaining('private transport error'), findsNothing);
  });

  testWidgets('development login is prefilled and submits both fields', (
    tester,
  ) async {
    final feed = _ShellFeedCall.open();
    final transport = _ShellTransport(feed);
    final runtime = _runtime(transport, authenticated: false);

    await tester.pumpWidget(_runtimeHost(controller: runtime));
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );
    final username = tester.widget<TextField>(
      find.byKey(runtimeUsernameFieldKey),
    );
    final password = tester.widget<TextField>(
      find.byKey(runtimePasswordFieldKey),
    );
    expect(username.controller?.text, 'admin');
    expect(username.obscureText, isFalse);
    expect(password.controller?.text, 'admin');
    expect(password.obscureText, isTrue);

    await tester.tap(find.byKey(runtimeSignInButtonKey));
    await _pumpUntil(tester, () => transport.watchStarted);

    expect(transport.loginUsername, 'admin');
    expect(transport.loginPassword, 'admin');
    expect(runtime.session.status, SessionStatus.authenticated);

    feed.add(FeedSurfaceJson(surfaceJsonString(sequence: 1)));
    await _pumpUntil(tester, () => runtime.latestSurface != null);
    await tester.tap(find.byKey(runtimeSignOutButtonKey));
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );

    final signedOutUsername = tester.widget<TextField>(
      find.byKey(runtimeUsernameFieldKey),
    );
    final signedOutPassword = tester.widget<TextField>(
      find.byKey(runtimePasswordFieldKey),
    );
    expect(signedOutUsername.controller?.text, 'admin');
    expect(signedOutPassword.controller?.text, 'admin');
    expect(signedOutPassword.obscureText, isTrue);
    expect(find.byKey(runtimeSurfaceKey), findsNothing);

    await runtime.stop();
  });

  testWidgets('rejected login is generic and restores development defaults', (
    tester,
  ) async {
    final transport = _ShellTransport(
      _ShellFeedCall.open(),
      loginResults: [
        const AuthenticationException('Private authentication detail.'),
      ],
    );
    final runtime = _runtime(transport, authenticated: false);

    await tester.pumpWidget(_runtimeHost(controller: runtime));
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );
    await tester.enterText(find.byKey(runtimeUsernameFieldKey), 'other-user');
    await tester.enterText(
      find.byKey(runtimePasswordFieldKey),
      'wrong-password',
    );
    await tester.tap(find.byKey(runtimeSignInButtonKey));
    await _pumpUntil(
      tester,
      () => find
          .text('Sign-in was not accepted. Please try again.')
          .evaluate()
          .isNotEmpty,
    );

    expect(transport.loginAttempts, [
      ['other-user', 'wrong-password'],
    ]);
    expect(
      tester
          .widget<TextField>(find.byKey(runtimeUsernameFieldKey))
          .controller
          ?.text,
      'admin',
    );
    final password = tester.widget<TextField>(
      find.byKey(runtimePasswordFieldKey),
    );
    expect(password.controller?.text, 'admin');
    expect(password.obscureText, isTrue);
    expect(find.textContaining('Private authentication detail'), findsNothing);
    expect(find.textContaining('wrong-password'), findsNothing);

    await runtime.stop();
  });

  testWidgets('enter from either development field submits the form', (
    tester,
  ) async {
    for (final key in [runtimeUsernameFieldKey, runtimePasswordFieldKey]) {
      final transport = _ShellTransport(_ShellFeedCall.open());
      final runtime = _runtime(transport, authenticated: false);
      await tester.pumpWidget(_runtimeHost(controller: runtime));
      await _pumpUntil(
        tester,
        () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
      );

      tester.widget<TextField>(find.byKey(key)).onSubmitted?.call('admin');
      await _pumpUntil(tester, () => transport.watchStarted);

      expect(transport.loginAttempts, [
        ['admin', 'admin'],
      ]);
      await runtime.stop();
      await tester.pumpWidget(const SizedBox.shrink());
    }
  });

  testWidgets('external identity hides development credentials', (
    tester,
  ) async {
    final transport = _ShellTransport(_ShellFeedCall.open());
    final runtime = _runtime(transport, authenticated: false);

    await tester.pumpWidget(
      _runtimeHost(
        configuration: _configuration(
          externalIdentity: ExternalIdentityConfiguration(
            issuer: Uri.parse('https://identity.example'),
            clientId: 'digitalbrain-ui',
          ),
        ),
        controller: runtime,
        externalIdentityTokenSourceFactory: (_) => _ExternalTokenSource(),
      ),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );

    expect(find.text('Continue'), findsOneWidget);
    expect(find.byKey(runtimeUsernameFieldKey), findsNothing);
    expect(find.byKey(runtimePasswordFieldKey), findsNothing);
    expect(find.textContaining('organization identity'), findsOneWidget);

    await runtime.stop();
  });

  testWidgets('shell preserves and displays the terminal transport error', (
    tester,
  ) async {
    final feed = _ShellFeedCall.error(
      const TransportException(
        TransportErrorCode.permissionDenied,
        'Signed runtime feed was denied.',
      ),
    );
    final runtime = _runtime(_ShellTransport(feed));

    await tester.pumpWidget(_runtimeHost(controller: runtime));
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeTerminalErrorKey).evaluate().isNotEmpty,
    );

    expect(
      find.text('DigitalBrain is unavailable right now. Please try again.'),
      findsOneWidget,
    );
    expect(find.textContaining('feed'), findsNothing);

    await runtime.stop();
  });

  testWidgets('shell suppresses a rendered surface when its expiry passes', (
    tester,
  ) async {
    var now = testNow;
    final feed = _ShellFeedCall.open();
    final transport = _ShellTransport(feed);
    final runtime = _runtime(transport);

    await tester.pumpWidget(_runtimeHost(controller: runtime, now: () => now));
    await _pumpUntil(tester, () => transport.watchStarted);
    feed.add(FeedSurfaceJson(surfaceJsonString(sequence: 1)));
    await _pumpUntil(tester, () => runtime.latestSurface != null);
    expect(find.byKey(runtimeSurfaceKey), findsOneWidget);

    now = testNow.add(const Duration(hours: 2));
    await tester.pump(const Duration(hours: 1));

    expect(find.byKey(runtimeSurfaceKey), findsNothing);
    expect(find.byKey(runtimeLoadingKey), findsOneWidget);
    await runtime.stop();
  });

  testWidgets('shell retains the INO draft while reconnecting', (tester) async {
    final first = _ShellFeedCall.open();
    final transport = _ShellTransport(first);
    final runtime = _runtime(transport);
    final owner = _RefreshableSessionOwner(runtime);
    final shell = _runtimeHost(sessionOwner: owner);

    await tester.pumpWidget(shell);
    await _pumpUntil(tester, () => transport.watchStarted);
    first.add(
      FeedSurfaceJson(
        surfaceJsonString(
          sequence: 1,
          payload: inoConversationPayload(),
          actions: [testInoActionJson()],
        ),
      ),
    );
    await _pumpUntil(tester, () => runtime.latestSurface != null);
    await tester.enterText(
      find.byKey(inoComposerFieldKey),
      'A draft that stays private',
    );

    runtime.status = RuntimeStatus.reconnecting;
    owner.refresh();
    await tester.pump();

    expect(find.byKey(inoReconnectBannerKey), findsOneWidget);
    expect(
      tester
          .widget<TextField>(find.byKey(inoComposerFieldKey))
          .controller
          ?.text,
      'A draft that stays private',
    );
    expect(
      tester.widget<FilledButton>(find.byKey(inoSendButtonKey)).onPressed,
      isNull,
    );

    runtime.status = RuntimeStatus.streaming;
    owner.refresh();
    await tester.pump();
    expect(find.byKey(inoReconnectBannerKey), findsNothing);
    expect(
      tester
          .widget<TextField>(find.byKey(inoComposerFieldKey))
          .controller
          ?.text,
      'A draft that stays private',
    );
    await runtime.stop();
  });

  testWidgets('retained chat explains a terminal connection failure', (
    tester,
  ) async {
    final feed = _ShellFeedCall.open();
    final transport = _ShellTransport(feed);
    final runtime = _runtime(transport);
    final owner = _RefreshableSessionOwner(runtime);
    final shell = _runtimeHost(sessionOwner: owner);

    await tester.pumpWidget(shell);
    await _pumpUntil(tester, () => transport.watchStarted);
    feed.add(
      FeedSurfaceJson(
        surfaceJsonString(
          sequence: 1,
          payload: inoConversationPayload(),
          actions: [testInoActionJson()],
        ),
      ),
    );
    await _pumpUntil(tester, () => runtime.latestSurface != null);
    await tester.enterText(
      find.byKey(inoComposerFieldKey),
      'A draft that remains local',
    );

    runtime.status = RuntimeStatus.terminalError;
    owner.refresh();
    await tester.pump();

    expect(find.byKey(inoConnectionUnavailableBannerKey), findsOneWidget);
    expect(find.text('A draft that remains local'), findsOneWidget);
    expect(
      tester.widget<FilledButton>(find.byKey(inoSendButtonKey)).onPressed,
      isNull,
    );
    await runtime.stop();
  });

  testWidgets('chat copy replaces the legacy INO product language', (
    tester,
  ) async {
    final feed = _ShellFeedCall.open();
    final transport = _ShellTransport(feed);
    final runtime = _runtime(transport);

    await tester.pumpWidget(_runtimeHost(controller: runtime));
    await _pumpUntil(tester, () => transport.watchStarted);
    feed.add(
      FeedSurfaceJson(
        surfaceJsonString(
          sequence: 1,
          payload: inoConversationPayload(),
          actions: [testInoActionJson()],
        ),
      ),
    );
    await _pumpUntil(tester, () => runtime.latestSurface != null);

    expect(find.text('Chat'), findsOneWidget);
    expect(
      tester.getSemantics(find.byKey(inoConversationKey)).label,
      startsWith('Chat conversation'),
    );

    await runtime.stop();
  });

  testWidgets(
    'chat surface renders a capability chip showing only the human name',
    (tester) async {
      final feed = _ShellFeedCall.open();
      final transport = _ShellTransport(feed);
      final runtime = _runtime(transport);

      await tester.pumpWidget(_runtimeHost(controller: runtime));
      await _pumpUntil(tester, () => transport.watchStarted);
      feed.add(
        FeedSurfaceJson(
          surfaceJsonString(
            sequence: 1,
            payload: inoConversationPayload(
              operation: inoOperation(
                state: 'succeeded',
                capability: inoCapability(),
              ),
            ),
            actions: [testInoActionJson()],
          ),
        ),
      );
      await _pumpUntil(
        tester,
        () => find.byKey(chatCapabilityChipKey).evaluate().isNotEmpty,
      );

      expect(find.text('Read Salesforce records'), findsOneWidget);
      expect(find.textContaining('salesforce.record.read.v1'), findsNothing);

      await runtime.stop();
    },
  );

  testWidgets(
    'chat surface renders ambiguous capability choices from assistant text',
    (tester) async {
      final feed = _ShellFeedCall.open();
      final transport = _ShellTransport(feed);
      final runtime = _runtime(transport);

      await tester.pumpWidget(_runtimeHost(controller: runtime));
      await _pumpUntil(tester, () => transport.watchStarted);
      feed.add(
        FeedSurfaceJson(
          surfaceJsonString(
            sequence: 1,
            payload: inoConversationPayload(
              messages: [
                inoMessage(
                  role: 'assistant',
                  text:
                      'A few capabilities could match this request: Read a Gmail message; List Gmail mailbox messages. Please choose one and ask again.',
                  state: 'succeeded',
                ),
              ],
              operation: inoOperation(state: 'succeeded'),
            ),
            actions: [testInoActionJson()],
          ),
        ),
      );
      await _pumpUntil(
        tester,
        () => find.textContaining('Read a Gmail message').evaluate().isNotEmpty,
      );

      expect(find.textContaining('Read a Gmail message'), findsOneWidget);
      expect(
        find.textContaining('List Gmail mailbox messages'),
        findsOneWidget,
      );

      await runtime.stop();
    },
  );

  testWidgets(
    'chat surface hides the capability chip and Open Studio without metadata',
    (tester) async {
      final feed = _ShellFeedCall.open();
      final transport = _ShellTransport(feed);
      final runtime = _runtime(transport);

      await tester.pumpWidget(_runtimeHost(controller: runtime));
      await _pumpUntil(tester, () => transport.watchStarted);
      feed.add(
        FeedSurfaceJson(
          surfaceJsonString(
            sequence: 1,
            payload: inoConversationPayload(
              operation: inoOperation(state: 'succeeded'),
            ),
            actions: [testInoActionJson()],
          ),
        ),
      );
      await _pumpUntil(tester, () => runtime.latestSurface != null);

      expect(find.byKey(chatCapabilityChipKey), findsNothing);
      expect(find.byKey(chatOpenStudioButtonKey), findsNothing);

      await runtime.stop();
    },
  );

  testWidgets(
    'chat surface hides the capability chip for a missing resolution',
    (tester) async {
      final feed = _ShellFeedCall.open();
      final transport = _ShellTransport(feed);
      final runtime = _runtime(transport);

      await tester.pumpWidget(_runtimeHost(controller: runtime));
      await _pumpUntil(tester, () => transport.watchStarted);
      feed.add(
        FeedSurfaceJson(
          surfaceJsonString(
            sequence: 1,
            payload: inoConversationPayload(
              operation: inoOperation(
                state: 'succeeded',
                capability: inoCapability(
                  kind: 'missing',
                  id: 'memory.fact.remember.v1',
                  name: 'Remember a fact',
                  confidence: 0.41,
                ),
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

      expect(find.byKey(chatCapabilityChipKey), findsNothing);
      expect(find.byKey(chatOpenStudioButtonKey), findsOneWidget);

      await runtime.stop();
    },
  );

  testWidgets('Open Studio navigates to the safe Feature Studio placeholder', (
    tester,
  ) async {
    final feed = _ShellFeedCall.open();
    final transport = _ShellTransport(feed);
    final runtime = _runtime(transport);
    final owner = RuntimeSessionOwner(
      configuration: _configuration(),
      controller: runtime,
      transportFactory: _unexpectedTransport,
    );
    final router = createDigitalBrainRouter();
    addTearDown(router.dispose);

    await tester.pumpWidget(
      _ScopedSessionHost(
        owner: owner,
        child: MaterialApp.router(routerConfig: router),
      ),
    );
    await _pumpUntil(tester, () => transport.watchStarted);
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

    await tester.tap(find.byKey(chatOpenStudioButtonKey));
    await tester.pumpAndSettle();

    expect(find.text('Feature Studio'), findsOneWidget);
    expect(find.text('Draft created from Chat'), findsOneWidget);
    expect(
      find.text('proposal-0123456789abcdef0123456789abcdef'),
      findsOneWidget,
    );
    expect(find.byKey(runtimeSurfaceKey), findsNothing);

    await runtime.stop();
  });
}

Widget _runtimeHost({
  RuntimeSessionOwner? sessionOwner,
  RuntimeConfiguration? configuration,
  RuntimeController? controller,
  UiTransportFactory? transportFactory,
  ExternalIdentityTokenSourceFactory? externalIdentityTokenSourceFactory,
  bool autoStart = true,
  DateTime Function()? now,
}) {
  final owner =
      sessionOwner ??
      RuntimeSessionOwner(
        configuration: configuration ?? _configuration(),
        controller: controller,
        transportFactory: transportFactory ?? _unexpectedTransport,
        externalIdentityTokenSourceFactory: externalIdentityTokenSourceFactory,
        autoStart: autoStart,
      );
  final chat = now == null ? const ChatPage() : ChatPage(now: now);
  return _ScopedSessionHost(
    owner: owner,
    child: MaterialApp(home: RuntimeShell(child: chat)),
  );
}

UiTransport _unexpectedTransport(Uri endpoint) =>
    throw StateError('Unexpected transport construction for $endpoint.');

class _ScopedSessionHost extends StatefulWidget {
  const _ScopedSessionHost({required this.owner, required this.child});

  final RuntimeSessionOwner owner;
  final Widget child;

  @override
  State<_ScopedSessionHost> createState() => _ScopedSessionHostState();
}

class _ScopedSessionHostState extends State<_ScopedSessionHost> {
  @override
  void initState() {
    super.initState();
    scheduleMicrotask(widget.owner.initialize);
  }

  @override
  void dispose() {
    unawaited(widget.owner.close());
    super.dispose();
  }

  @override
  Widget build(BuildContext context) =>
      AppSessionScope(owner: widget.owner, child: widget.child);
}

RuntimeConfiguration _configuration({
  ExternalIdentityConfiguration? externalIdentity,
}) => RuntimeConfiguration(
  endpoint: Uri.parse('https://localhost:7443'),
  externalIdentity: externalIdentity,
);

RuntimeController _runtime(
  _ShellTransport transport, {
  bool authenticated = true,
}) {
  final runtime = RuntimeController(
    transport: transport,
    reconnectPolicy: const ReconnectPolicy(
      delays: [Duration.zero],
      maxAttempts: 1,
    ),
    delay: (_) async {},
  );
  if (authenticated) runtime.session.establish(testSession());
  return runtime;
}

Future<void> _pumpUntil(WidgetTester tester, bool Function() condition) async {
  for (var attempt = 0; attempt < 100; attempt++) {
    await tester.pump(const Duration(milliseconds: 1));
    if (condition()) return;
  }
  fail('Widget condition was not reached.');
}

class _ShellTransport implements UiTransport {
  _ShellTransport(this.feed, {Iterable<Object>? loginResults})
    : _loginResults = [...?loginResults];

  final _ShellFeedCall feed;
  final List<Object> _loginResults;
  final List<List<String>> loginAttempts = [];
  String? loginUsername;
  String? loginPassword;
  bool watchStarted = false;
  int closeCount = 0;
  UiActionRef? submittedAction;
  Map<String, Object?>? submittedInput;

  @override
  Future<SessionBundle> login({
    required String username,
    required String password,
  }) async {
    loginUsername = username;
    loginPassword = password;
    loginAttempts.add([username, password]);
    if (_loginResults.isNotEmpty) {
      final result = _loginResults.removeAt(0);
      if (result is SessionBundle) return result;
      throw result;
    }
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
    watchStarted = true;
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
  }) async {
    submittedAction = action;
    submittedInput = input;
    return const ActionResult(
      operationId: 'operation-a',
      idempotencyKey: 'idempotency-a',
    );
  }

  @override
  Future<void> close() async {
    closeCount++;
  }
}

class _RefreshableSessionOwner extends RuntimeSessionOwner {
  _RefreshableSessionOwner(RuntimeController controller)
    : super(
        configuration: _configuration(),
        controller: controller,
        transportFactory: _unexpectedTransport,
      );

  void refresh() => notifyListeners();
}

class _ExternalTokenSource implements ExternalIdentityTokenSource {
  @override
  Future<void> beginAuthentication() async {}

  @override
  Future<String?> restoreIdentityToken() async => null;
}

class _ShellFeedCall implements FeedCall {
  _ShellFeedCall._(this._controller);

  factory _ShellFeedCall.open() =>
      _ShellFeedCall._(StreamController<FeedEvent>());

  factory _ShellFeedCall.error(Object error) {
    final controller = StreamController<FeedEvent>();
    scheduleMicrotask(() async {
      controller.addError(error);
      await controller.close();
    });
    return _ShellFeedCall._(controller);
  }

  final StreamController<FeedEvent> _controller;

  void add(FeedEvent event) => _controller.add(event);

  @override
  Stream<FeedEvent> get events => _controller.stream;

  @override
  Future<void> cancel() async {
    if (!_controller.isClosed) await _controller.close();
  }
}
