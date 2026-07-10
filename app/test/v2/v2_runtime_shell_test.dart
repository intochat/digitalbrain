import 'dart:async';

import 'package:digitalbrain_flutter/v2/protocol/surface_protocol.dart';
import 'package:digitalbrain_flutter/v2/v2_config.dart';
import 'package:digitalbrain_flutter/v2/v2_runtime.dart';
import 'package:digitalbrain_flutter/v2/widgets/v2_runtime_shell.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_test_fixtures.dart';

void main() {
  testWidgets('normal V2 shell bootstraps and renders its first surface', (
    tester,
  ) async {
    final feed = _ShellFeedCall.open();
    final transport = _ShellTransport(feed);
    final runtime = _runtime(transport);

    await tester.pumpWidget(
      _host(
        V2RuntimeShell(
          configuration: _configuration(bootstrapSecret: 'bootstrap-once'),
          controller: runtime,
        ),
      ),
    );
    await _pumpUntil(tester, () => transport.watchStarted);
    feed.add(V2FeedSurfaceJson(surfaceJsonString(sequence: 1)));
    await _pumpUntil(tester, () => runtime.latestSurface != null);

    expect(find.byKey(v2RuntimeSurfaceKey), findsOneWidget);
    expect(find.text('V2 ready'), findsOneWidget);
    expect(find.text('Authenticated surface'), findsOneWidget);
    expect(transport.bootstrapSecret, 'bootstrap-once');

    await runtime.stop();
  });

  testWidgets('manual bootstrap sign-in does not retain the typed secret', (
    tester,
  ) async {
    final feed = _ShellFeedCall.open();
    final transport = _ShellTransport(feed);
    final runtime = _runtime(transport);

    await tester.pumpWidget(
      _host(
        V2RuntimeShell(configuration: _configuration(), controller: runtime),
      ),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(v2RuntimeSignInKey).evaluate().isNotEmpty,
    );
    await tester.enterText(find.byKey(v2RuntimeSecretFieldKey), 'typed-once');
    await tester.tap(find.byKey(v2RuntimeSignInButtonKey));
    await _pumpUntil(tester, () => transport.watchStarted);

    expect(transport.bootstrapSecret, 'typed-once');
    expect(find.text('typed-once'), findsNothing);
    expect(runtime.session.status, V2SessionStatus.authenticated);

    await runtime.stop();
  });

  testWidgets('shell preserves and displays the terminal transport error', (
    tester,
  ) async {
    final feed = _ShellFeedCall.error(
      const V2TransportException(
        V2TransportErrorCode.permissionDenied,
        'Signed V2 feed was denied.',
      ),
    );
    final runtime = _runtime(_ShellTransport(feed));

    await tester.pumpWidget(
      _host(
        V2RuntimeShell(
          configuration: _configuration(bootstrapSecret: 'bootstrap-once'),
          controller: runtime,
        ),
      ),
    );
    await _pumpUntil(
      tester,
      () => find.byKey(v2RuntimeTerminalErrorKey).evaluate().isNotEmpty,
    );

    expect(find.text('Signed V2 feed was denied.'), findsOneWidget);
    expect(find.textContaining('closed before'), findsNothing);

    await runtime.stop();
  });

  testWidgets('shell suppresses a rendered surface when its expiry passes', (
    tester,
  ) async {
    var now = v2TestNow;
    final feed = _ShellFeedCall.open();
    final transport = _ShellTransport(feed);
    final runtime = _runtime(transport);

    await tester.pumpWidget(
      _host(
        V2RuntimeShell(
          configuration: _configuration(bootstrapSecret: 'bootstrap-once'),
          controller: runtime,
          now: () => now,
        ),
      ),
    );
    await _pumpUntil(tester, () => transport.watchStarted);
    feed.add(V2FeedSurfaceJson(surfaceJsonString(sequence: 1)));
    await _pumpUntil(tester, () => runtime.latestSurface != null);
    expect(find.byKey(v2RuntimeSurfaceKey), findsOneWidget);

    now = v2TestNow.add(const Duration(hours: 2));
    await tester.pump(const Duration(hours: 1));

    expect(find.byKey(v2RuntimeSurfaceKey), findsNothing);
    expect(find.byKey(v2RuntimeLoadingKey), findsOneWidget);
    await runtime.stop();
  });
}

Widget _host(Widget child) => MaterialApp(home: child);

V2RuntimeConfiguration _configuration({String? bootstrapSecret}) =>
    V2RuntimeConfiguration(
      endpoint: Uri.parse('https://localhost:7443'),
      bootstrapSecret: bootstrapSecret,
    );

V2RuntimeController _runtime(_ShellTransport transport) => V2RuntimeController(
  transport: transport,
  reconnectPolicy: const V2ReconnectPolicy(
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

class _ShellTransport implements V2UiTransport {
  _ShellTransport(this.feed);

  final _ShellFeedCall feed;
  String? bootstrapSecret;
  bool watchStarted = false;

  @override
  Future<V2SessionBundle> bootstrapSession(String bootstrapSecret) async {
    this.bootstrapSecret = bootstrapSecret;
    return testSession();
  }

  @override
  Future<V2SessionBundle> refreshSession({
    required String refreshToken,
  }) async => testSession();

  @override
  Future<V2FeedCall> watchSurfaceFeed({
    required String accessToken,
    required int afterSequence,
    required V2FeedAudience audience,
    required Set<String> clientCapabilities,
    required int maxBatchSize,
  }) async {
    watchStarted = true;
    return feed;
  }

  @override
  Future<void> acknowledgeSurfaceFeed({
    required String accessToken,
    required V2FeedAudience audience,
    required int sequence,
  }) async {}

  @override
  Future<V2ActionResult> submitAction({
    required String accessToken,
    required UiActionRef action,
    required Map<String, Object?> input,
  }) async => const V2ActionResult(
    operationId: 'operation-a',
    idempotencyKey: 'idempotency-a',
  );

  @override
  Future<void> close() async {}
}

class _ShellFeedCall implements V2FeedCall {
  _ShellFeedCall._(this._controller);

  factory _ShellFeedCall.open() =>
      _ShellFeedCall._(StreamController<V2FeedEvent>());

  factory _ShellFeedCall.error(Object error) {
    final controller = StreamController<V2FeedEvent>();
    scheduleMicrotask(() async {
      controller.addError(error);
      await controller.close();
    });
    return _ShellFeedCall._(controller);
  }

  final StreamController<V2FeedEvent> _controller;

  void add(V2FeedEvent event) => _controller.add(event);

  @override
  Stream<V2FeedEvent> get events => _controller.stream;

  @override
  Future<void> cancel() async {
    if (!_controller.isClosed) await _controller.close();
  }
}
