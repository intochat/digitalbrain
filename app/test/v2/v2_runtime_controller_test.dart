import 'dart:async';
import 'dart:collection';

import 'package:digitalbrain_flutter/v2/protocol/surface_protocol.dart';
import 'package:digitalbrain_flutter/v2/v2_runtime.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_test_fixtures.dart';

void main() {
  group('V2RuntimeController', () {
    test('authenticates and delivers the first renderable surface', () async {
      final call = _FakeFeedCall.open();
      final transport = _FakeUiTransport([call]);
      final runtime = _runtime(transport);

      await runtime.authenticateWithBootstrap('bootstrap-once');
      await _eventually(() => transport.watchAfter.isNotEmpty);
      call.add(V2FeedSurfaceJson(surfaceJsonString(sequence: 1)));
      await _eventually(() => runtime.latestSurface != null);

      expect(transport.bootstrapSecret, 'bootstrap-once');
      expect(runtime.status, V2RuntimeStatus.streaming);
      expect(runtime.latestSurface?.feedSequence, 1);
      expect(runtime.latestSurface?.payload, isA<NativeSurfacePayload>());
      expect(transport.acknowledged, [1]);

      await runtime.stop();
      expect(call.cancelled, isTrue);
    });

    test('reconnect resumes after the last accepted sequence', () async {
      final first = _FakeFeedCall.fromEvents([
        V2FeedSurfaceJson(surfaceJsonString(sequence: 1)),
      ]);
      final second = _FakeFeedCall.open();
      final transport = _FakeUiTransport([first, second]);
      final runtime = _runtime(transport);

      await runtime.authenticateWithBootstrap('bootstrap-once');
      await _eventually(() => transport.watchAfter.length == 2);

      expect(transport.watchAfter, [0, 1]);
      expect(runtime.latestSurface?.feedSequence, 1);
      expect(runtime.status, V2RuntimeStatus.streaming);

      await runtime.stop();
    });

    test(
      'client gap clears state and converges from an ordinary full replay',
      () async {
        final first = _FakeFeedCall.fromEvents([
          V2FeedSurfaceJson(
            surfaceJsonString(sequence: 1, actions: [testActionJson()]),
          ),
          V2FeedSurfaceJson(surfaceJsonString(sequence: 3, revision: 2)),
        ]);
        final second = _FakeFeedCall.open();
        final transport = _FakeUiTransport([first, second]);
        final runtime = _runtime(transport);

        await runtime.authenticateWithBootstrap('bootstrap-once');
        await _eventually(() => transport.watchAfter.length == 2);

        expect(transport.watchAfter, [0, 0]);
        expect(runtime.lastReset?.reason, 'sequence-gap');
        expect(runtime.feed.surfaces, isEmpty);
        expect(runtime.latestSurface, isNull);
        second.add(V2FeedSurfaceJson(surfaceJsonString(sequence: 1)));
        second.add(
          V2FeedSurfaceJson(surfaceJsonString(sequence: 2, revision: 2)),
        );
        second.add(
          V2FeedSurfaceJson(surfaceJsonString(sequence: 3, revision: 3)),
        );
        await _eventually(() => runtime.feed.lastSequence == 3);
        expect(runtime.feed.needsReset, isFalse);
        expect(runtime.latestSurface?.feedSequence, 3);
        expect(runtime.latestSurface?.revision, 3);
        expect(runtime.status, V2RuntimeStatus.streaming);

        await runtime.stop();
      },
    );

    test(
      'server reset atomically installs snapshot then accepts baseline + 1',
      () async {
        final call = _FakeFeedCall.open();
        final transport = _FakeUiTransport([call]);
        final runtime = _runtime(transport);

        await runtime.authenticateWithBootstrap('bootstrap-once');
        await _eventually(() => transport.watchAfter.isNotEmpty);
        call.add(
          V2FeedResetEvent(
            reason: 'retention-gap',
            resumeSequence: 5,
            snapshotJson: [
              surfaceJsonString(
                sequence: 4,
                revision: 2,
                surfaceId: 'snapshot',
              ),
            ],
          ),
        );
        call.add(
          V2FeedSurfaceJson(
            surfaceJsonString(sequence: 6, revision: 3, surfaceId: 'snapshot'),
          ),
        );
        await _eventually(() => runtime.feed.lastSequence == 6);

        expect(runtime.lastReset?.reason, 'retention-gap');
        expect(runtime.latestSurface?.feedSequence, 6);
        expect(transport.acknowledged, [5, 6]);

        await runtime.stop();
      },
    );

    test('reconnects after a transient transport failure', () async {
      final first = _FakeFeedCall.error(
        const V2TransportException(
          V2TransportErrorCode.unavailable,
          'Temporary V2 feed failure.',
        ),
      );
      final second = _FakeFeedCall.open();
      final transport = _FakeUiTransport([first, second]);
      final runtime = _runtime(transport);

      await runtime.authenticateWithBootstrap('bootstrap-once');
      await _eventually(() => transport.watchAfter.length == 2);

      expect(transport.watchAfter, [0, 0]);
      expect(runtime.status, V2RuntimeStatus.streaming);

      await runtime.stop();
    });

    test(
      'rotates refresh token after server rejects an access session',
      () async {
        final first = _FakeFeedCall.error(
          const V2AuthenticationException('Access session expired.'),
        );
        final second = _FakeFeedCall.open();
        final transport = _FakeUiTransport([first, second]);
        final runtime = _runtime(transport);

        await runtime.authenticateWithBootstrap('bootstrap-once');
        await _eventually(() => transport.watchAfter.length == 2);

        expect(transport.refreshCount, 1);
        expect(transport.watchAccessTokens, [
          'access-token',
          'access-refreshed',
        ]);
        expect(runtime.status, V2RuntimeStatus.streaming);

        await runtime.stop();
      },
    );

    test(
      'terminal stream error is never overwritten by subsequent done',
      () async {
        const original = V2TransportException(
          V2TransportErrorCode.unavailable,
          'Original authenticated feed error.',
        );
        final transport = _FakeUiTransport([_FakeFeedCall.error(original)]);
        final runtime = _runtime(
          transport,
          reconnectPolicy: const V2ReconnectPolicy(
            delays: [Duration.zero],
            maxAttempts: 0,
          ),
        );

        await runtime.authenticateWithBootstrap('bootstrap-once');
        await _eventually(
          () => runtime.status == V2RuntimeStatus.terminalError,
        );

        expect(runtime.terminalError, same(original));
        expect(
          runtime.terminalError.toString(),
          'Original authenticated feed error.',
        );
        expect(runtime.terminalError.toString(), isNot(contains('closed')));

        await runtime.stop();
      },
    );

    test(
      'cancellation closes the active feed and does not reconnect',
      () async {
        final call = _FakeFeedCall.open();
        final transport = _FakeUiTransport([call]);
        final runtime = _runtime(transport);

        await runtime.authenticateWithBootstrap('bootstrap-once');
        await _eventually(() => runtime.status == V2RuntimeStatus.streaming);
        await runtime.stop();

        expect(call.cancelled, isTrue);
        expect(runtime.status, V2RuntimeStatus.stopped);
        expect(transport.watchAfter, [0]);
        expect(transport.closed, isTrue);
      },
    );

    test(
      'submits only the current action binding and its signed token',
      () async {
        final call = _FakeFeedCall.open();
        final transport = _FakeUiTransport([call]);
        final runtime = _runtime(transport);
        final json = surfaceJsonString(
          sequence: 1,
          actions: [testActionJson()],
        );

        await runtime.authenticateWithBootstrap('bootstrap-once');
        await _eventually(() => runtime.status == V2RuntimeStatus.streaming);
        call.add(V2FeedSurfaceJson(json));
        await _eventually(() => runtime.latestSurface != null);
        final surface = runtime.latestSurface!;
        final result = await runtime.submitAction(
          surface,
          'refresh-binding',
          const {'confirmed': true},
          now: v2TestNow,
        );

        expect(result.operationId, 'operation-a');
        expect(transport.submittedAction?.actionToken, 'signed-action-token');
        expect(transport.submittedInput, {'confirmed': true});
        expect(
          () => runtime.submitAction(
            surface,
            'forged-binding',
            const {},
            now: v2TestNow,
          ),
          throwsA(isA<StateError>()),
        );
        expect(
          () => runtime.submitAction(
            surface,
            'refresh-binding',
            const {},
            now: DateTime.utc(2040),
          ),
          throwsA(isA<StateError>()),
        );

        await runtime.stop();
      },
    );

    test(
      'identity rebind clears prior scope before notifying listeners',
      () async {
        final first = _FakeFeedCall.open();
        final second = _FakeFeedCall.open();
        final transport = _FakeUiTransport(
          [first, second],
          bootstrapResults: [
            testSession(),
            testSession(
              identity: testIdentity(
                session: 'session-b',
              ),
            ),
          ],
        );
        final runtime = _runtime(transport);

        await runtime.authenticateWithBootstrap('bootstrap-a');
        await _eventually(() => runtime.status == V2RuntimeStatus.streaming);
        first.add(
          V2FeedSurfaceJson(
            surfaceJsonString(sequence: 1, actions: [testActionJson()]),
          ),
        );
        await _eventually(() => runtime.latestSurface != null);
        final priorSurface = runtime.latestSurface!;
        runtime.lastReset = const V2FeedReset('prior-scope');
        final priorEpoch = runtime.scopeEpoch;
        var observedNewScope = false;
        var observedMixedScope = false;
        runtime.addListener(() {
          if (runtime.session.sessionId != 'session-b') return;
          observedNewScope = true;
          observedMixedScope |=
              runtime.feed.surfaces.isNotEmpty ||
              runtime.feed.lastSequence != 0 ||
              runtime.feed.needsReset ||
              runtime.latestSurface != null ||
              runtime.lastReset != null;
        });

        await runtime.authenticateWithBootstrap('bootstrap-b');
        await _eventually(() => transport.watchAfter.length == 2);

        expect(observedNewScope, isTrue);
        expect(observedMixedScope, isFalse);
        expect(runtime.scopeEpoch, priorEpoch + 1);
        expect(runtime.feed.surfaces, isEmpty);
        expect(runtime.feed.lastSequence, 0);
        expect(runtime.feed.needsReset, isFalse);
        expect(runtime.latestSurface, isNull);
        expect(runtime.lastReset, isNull);
        await expectLater(
          runtime.submitAction(
            priorSurface,
            'refresh-binding',
            const <String, Object?>{},
            now: v2TestNow,
          ),
          throwsA(isA<StateError>()),
        );

        await runtime.stop();
      },
    );

    test(
      'transient reconnect preserves scoped surface state and epoch',
      () async {
        final first = _FakeFeedCall.open();
        final second = _FakeFeedCall.open();
        final transport = _FakeUiTransport([first, second]);
        final runtime = _runtime(transport);

        await runtime.authenticateWithBootstrap('bootstrap-once');
        await _eventually(() => runtime.status == V2RuntimeStatus.streaming);
        first.add(V2FeedSurfaceJson(surfaceJsonString(sequence: 1)));
        await _eventually(() => runtime.latestSurface != null);
        final surface = runtime.latestSurface;
        final epoch = runtime.scopeEpoch;

        first.addError(
          const V2TransportException(
            V2TransportErrorCode.unavailable,
            'Temporary V2 feed failure.',
          ),
        );
        await _eventually(() => transport.watchAfter.length == 2);

        expect(runtime.scopeEpoch, epoch);
        expect(runtime.feed.lastSequence, 1);
        expect(runtime.latestSurface, same(surface));
        expect(runtime.status, V2RuntimeStatus.streaming);

        await runtime.stop();
      },
    );

    test('failed bootstrap clears protected in-memory scope state', () async {
      final call = _FakeFeedCall.open();
      final transport = _FakeUiTransport(
        [call],
        bootstrapResults: [
          testSession(),
          const V2AuthenticationException('Bootstrap was denied.'),
        ],
      );
      final runtime = _runtime(transport);

      await runtime.authenticateWithBootstrap('bootstrap-once');
      await _eventually(() => runtime.status == V2RuntimeStatus.streaming);
      call.add(V2FeedSurfaceJson(surfaceJsonString(sequence: 1)));
      await _eventually(() => runtime.latestSurface != null);
      runtime.lastReset = const V2FeedReset('prior-scope');
      final epoch = runtime.scopeEpoch;

      await expectLater(
        runtime.authenticateWithBootstrap('bootstrap-invalid'),
        throwsA(isA<V2AuthenticationException>()),
      );

      expect(runtime.scopeEpoch, epoch + 1);
      expect(runtime.status, V2RuntimeStatus.awaitingSignIn);
      expect(runtime.session.identity, isNull);
      expect(runtime.feed.surfaces, isEmpty);
      expect(runtime.feed.lastSequence, 0);
      expect(runtime.latestSurface, isNull);
      expect(runtime.lastReset, isNull);
      expect(
        () => runtime.feed.accept(testSurface(sequence: 2, revision: 2)),
        throwsA(isA<V2ScopeViolation>()),
      );

      await runtime.stop();
    });

    test(
      'authentication expiry clears protected in-memory scope state',
      () async {
        final call = _FakeFeedCall.open();
        final transport = _FakeUiTransport(
          [call],
          refreshError: const V2AuthenticationException('Refresh was denied.'),
        );
        final runtime = _runtime(transport);

        await runtime.authenticateWithBootstrap('bootstrap-once');
        await _eventually(() => runtime.status == V2RuntimeStatus.streaming);
        call.add(V2FeedSurfaceJson(surfaceJsonString(sequence: 1)));
        await _eventually(() => runtime.latestSurface != null);
        runtime.lastReset = const V2FeedReset('prior-scope');
        final epoch = runtime.scopeEpoch;

        call.addError(const V2AuthenticationException('Session expired.'));
        await _eventually(
          () => runtime.status == V2RuntimeStatus.awaitingSignIn,
        );

        expect(runtime.scopeEpoch, epoch + 1);
        expect(runtime.session.identity, isNull);
        expect(runtime.feed.surfaces, isEmpty);
        expect(runtime.feed.lastSequence, 0);
        expect(runtime.latestSurface, isNull);
        expect(runtime.lastReset, isNull);
        expect(
          () => runtime.feed.accept(testSurface(sequence: 2, revision: 2)),
          throwsA(isA<V2ScopeViolation>()),
        );

        await runtime.stop();
      },
    );

    test('rejects an action receipt completed after a scope change', () async {
      final first = _FakeFeedCall.open();
      final second = _FakeFeedCall.open();
      final receipt = Completer<V2ActionResult>();
      final transport = _FakeUiTransport(
        [first, second],
        bootstrapResults: [
          testSession(),
          testSession(
            identity: testIdentity(
              principal: 'principal-b',
              session: 'session-b',
            ),
          ),
        ],
        actionResult: receipt.future,
      );
      final runtime = _runtime(transport);

      await runtime.authenticateWithBootstrap('bootstrap-a');
      await _eventually(() => runtime.status == V2RuntimeStatus.streaming);
      first.add(
        V2FeedSurfaceJson(
          surfaceJsonString(sequence: 1, actions: [testActionJson()]),
        ),
      );
      await _eventually(() => runtime.latestSurface != null);
      final surface = runtime.latestSurface!;
      final submission = runtime.submitAction(
        surface,
        'refresh-binding',
        const <String, Object?>{},
        now: v2TestNow,
      );
      await _eventually(() => transport.submittedAction != null);

      await runtime.authenticateWithBootstrap('bootstrap-b');
      await _eventually(() => transport.watchAfter.length == 2);
      receipt.complete(
        const V2ActionResult(
          operationId: 'operation-a',
          idempotencyKey: 'idempotency-a',
        ),
      );

      await expectLater(submission, throwsA(isA<StateError>()));

      await runtime.stop();
    });
  });
}

V2RuntimeController _runtime(
  _FakeUiTransport transport, {
  V2ReconnectPolicy reconnectPolicy = const V2ReconnectPolicy(
    delays: [Duration.zero],
    maxAttempts: 3,
  ),
}) => V2RuntimeController(
  transport: transport,
  reconnectPolicy: reconnectPolicy,
  delay: (_) async {},
);

Future<void> _eventually(bool Function() condition) async {
  for (var attempt = 0; attempt < 100; attempt++) {
    if (condition()) return;
    await Future<void>.delayed(const Duration(milliseconds: 1));
  }
  fail('Condition was not reached.');
}

class _FakeUiTransport implements V2UiTransport {
  _FakeUiTransport(
    Iterable<_FakeFeedCall> calls, {
    Iterable<Object>? bootstrapResults,
    this.refreshError,
    this.actionResult,
  }) : _calls = Queue.of(calls),
       _bootstrapResults = Queue.of(
         bootstrapResults ?? <Object>[testSession()],
       );

  final Queue<_FakeFeedCall> _calls;
  final Queue<Object> _bootstrapResults;
  final Object? refreshError;
  final Future<V2ActionResult>? actionResult;
  final List<int> watchAfter = [];
  final List<String> watchAccessTokens = [];
  final List<int> acknowledged = [];
  String? bootstrapSecret;
  UiActionRef? submittedAction;
  Map<String, Object?>? submittedInput;
  bool closed = false;
  int refreshCount = 0;

  @override
  Future<V2SessionBundle> bootstrapSession(String bootstrapSecret) async {
    this.bootstrapSecret = bootstrapSecret;
    if (_bootstrapResults.isEmpty) {
      throw StateError('No fake bootstrap result is available.');
    }
    final result = _bootstrapResults.removeFirst();
    if (result is V2SessionBundle) return result;
    throw result;
  }

  @override
  Future<V2SessionBundle> refreshSession({required String refreshToken}) async {
    refreshCount++;
    final error = refreshError;
    if (error != null) throw error;
    return testSession(
      accessToken: 'access-refreshed',
      refreshToken: 'refresh-rotated',
    );
  }

  @override
  Future<V2FeedCall> watchSurfaceFeed({
    required String accessToken,
    required int afterSequence,
    required V2FeedAudience audience,
    required Set<String> clientCapabilities,
    required int maxBatchSize,
  }) async {
    watchAfter.add(afterSequence);
    watchAccessTokens.add(accessToken);
    if (_calls.isEmpty) {
      throw const V2TransportException(
        V2TransportErrorCode.unavailable,
        'No fake feed is available.',
      );
    }
    return _calls.removeFirst();
  }

  @override
  Future<void> acknowledgeSurfaceFeed({
    required String accessToken,
    required V2FeedAudience audience,
    required int sequence,
  }) async {
    acknowledged.add(sequence);
  }

  @override
  Future<V2ActionResult> submitAction({
    required String accessToken,
    required UiActionRef action,
    required Map<String, Object?> input,
  }) async {
    submittedAction = action;
    submittedInput = input;
    final pendingResult = actionResult;
    if (pendingResult != null) return pendingResult;
    return const V2ActionResult(
      operationId: 'operation-a',
      idempotencyKey: 'idempotency-a',
    );
  }

  @override
  Future<void> close() async {
    closed = true;
  }
}

class _FakeFeedCall implements V2FeedCall {
  _FakeFeedCall._(this._controller);

  factory _FakeFeedCall.open() =>
      _FakeFeedCall._(StreamController<V2FeedEvent>());

  factory _FakeFeedCall.fromEvents(Iterable<V2FeedEvent> events) {
    final controller = StreamController<V2FeedEvent>();
    scheduleMicrotask(() async {
      for (final event in events) {
        if (!controller.isClosed) controller.add(event);
      }
      if (!controller.isClosed) await controller.close();
    });
    return _FakeFeedCall._(controller);
  }

  factory _FakeFeedCall.error(Object error) {
    final controller = StreamController<V2FeedEvent>();
    scheduleMicrotask(() async {
      if (!controller.isClosed) controller.addError(error);
      if (!controller.isClosed) await controller.close();
    });
    return _FakeFeedCall._(controller);
  }

  final StreamController<V2FeedEvent> _controller;
  bool cancelled = false;

  void add(V2FeedEvent event) => _controller.add(event);

  void addError(Object error) => _controller.addError(error);

  @override
  Stream<V2FeedEvent> get events => _controller.stream;

  @override
  Future<void> cancel() async {
    cancelled = true;
    if (!_controller.isClosed) await _controller.close();
  }
}
