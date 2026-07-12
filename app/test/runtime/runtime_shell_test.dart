import 'dart:async';

import 'package:digitalbrain_flutter/runtime/protocol/surface_protocol.dart';
import 'package:digitalbrain_flutter/runtime/runtime_configuration.dart';
import 'package:digitalbrain_flutter/runtime/runtime.dart';
import 'package:digitalbrain_flutter/runtime/widgets/ino_composer.dart';
import 'package:digitalbrain_flutter/runtime/widgets/ino_conversation_view.dart';
import 'package:digitalbrain_flutter/runtime/widgets/runtime_shell.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'test_fixtures.dart';

void main() {
  testWidgets('normal runtime shell bootstraps and renders its first surface', (
    tester,
  ) async {
    final feed = _ShellFeedCall.open();
    final transport = _ShellTransport(feed);
    final runtime = _runtime(transport);

    await tester.pumpWidget(
      _host(
        RuntimeShell(
          configuration: _configuration(bootstrapSecret: 'bootstrap-once'),
          controller: runtime,
        ),
      ),
    );
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
    expect(transport.bootstrapSecret, 'bootstrap-once');

    await runtime.stop();
  });

  testWidgets('shell owns and asynchronously closes its generated transport', (
    tester,
  ) async {
    final transport = _ShellTransport(_ShellFeedCall.open());
    Uri? connectedEndpoint;

    await tester.pumpWidget(
      _host(
        RuntimeShell(
          configuration: _configuration(bootstrapSecret: 'bootstrap-once'),
          transportFactory: (endpoint) {
            connectedEndpoint = endpoint;
            return transport;
          },
        ),
      ),
    );
    await _pumpUntil(tester, () => transport.watchStarted);

    expect(connectedEndpoint, Uri.parse('https://localhost:7443'));

    await tester.pumpWidget(const SizedBox.shrink());
    await _pumpUntil(tester, () => transport.closeCount > 0);

    expect(transport.closeCount, greaterThanOrEqualTo(1));
  });

  testWidgets('shell hides transport construction failures', (tester) async {
    await tester.pumpWidget(
      _host(
        RuntimeShell(
          configuration: _configuration(),
          transportFactory: (_) => throw StateError('private transport error'),
        ),
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

  testWidgets('manual bootstrap sign-in does not retain the typed secret', (
    tester,
  ) async {
    final feed = _ShellFeedCall.open();
    final transport = _ShellTransport(feed);
    final runtime = _runtime(transport);

    await tester.pumpWidget(
      _host(RuntimeShell(configuration: _configuration(), controller: runtime)),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(runtimeSignInKey).evaluate().isNotEmpty,
    );
    await tester.enterText(find.byKey(runtimeSecretFieldKey), 'typed-once');
    await tester.tap(find.byKey(runtimeSignInButtonKey));
    await _pumpUntil(tester, () => transport.watchStarted);

    expect(transport.bootstrapSecret, 'typed-once');
    expect(find.text('typed-once'), findsNothing);
    expect(runtime.session.status, SessionStatus.authenticated);

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

    await tester.pumpWidget(
      _host(
        RuntimeShell(
          configuration: _configuration(bootstrapSecret: 'bootstrap-once'),
          controller: runtime,
        ),
      ),
    );
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

    await tester.pumpWidget(
      _host(
        RuntimeShell(
          configuration: _configuration(bootstrapSecret: 'bootstrap-once'),
          controller: runtime,
          now: () => now,
        ),
      ),
    );
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
    Widget shell() => _host(
      RuntimeShell(
        configuration: _configuration(bootstrapSecret: 'bootstrap-once'),
        controller: runtime,
      ),
    );

    await tester.pumpWidget(shell());
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
    await tester.pumpWidget(shell());
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
    await tester.pumpWidget(shell());
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
    Widget shell() => _host(
      RuntimeShell(
        configuration: _configuration(bootstrapSecret: 'bootstrap-once'),
        controller: runtime,
      ),
    );

    await tester.pumpWidget(shell());
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
    await tester.pumpWidget(shell());
    await tester.pump();

    expect(find.byKey(inoConnectionUnavailableBannerKey), findsOneWidget);
    expect(find.text('A draft that remains local'), findsOneWidget);
    expect(
      tester.widget<FilledButton>(find.byKey(inoSendButtonKey)).onPressed,
      isNull,
    );
    await runtime.stop();
  });
}

Widget _host(Widget child) => MaterialApp(home: child);

RuntimeConfiguration _configuration({String? bootstrapSecret}) =>
    RuntimeConfiguration(
      endpoint: Uri.parse('https://localhost:7443'),
      bootstrapSecret: bootstrapSecret,
    );

RuntimeController _runtime(_ShellTransport transport) => RuntimeController(
  transport: transport,
  reconnectPolicy: const ReconnectPolicy(
    delays: [Duration.zero],
    maxAttempts: 1,
  ),
  delay: (_) async {},
);

Future<void> _pumpUntil(WidgetTester tester, bool Function() condition) async {
  for (var attempt = 0; attempt < 100; attempt++) {
    await tester.pump(const Duration(milliseconds: 1));
    if (condition()) return;
  }
  fail('Widget condition was not reached.');
}

class _ShellTransport implements UiTransport {
  _ShellTransport(this.feed);

  final _ShellFeedCall feed;
  String? bootstrapSecret;
  bool watchStarted = false;
  int closeCount = 0;

  @override
  Future<SessionBundle> bootstrapSession(String bootstrapSecret) async {
    this.bootstrapSecret = bootstrapSecret;
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
  }) async => const ActionResult(
    operationId: 'operation-a',
    idempotencyKey: 'idempotency-a',
  );

  @override
  Future<void> close() async {
    closeCount++;
  }
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
