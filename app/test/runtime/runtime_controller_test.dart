import 'dart:async';
import 'dart:collection';
import 'dart:convert';

import 'package:digitalbrain_flutter/runtime/protocol/surface_protocol.dart';
import 'package:digitalbrain_flutter/runtime/runtime.dart';
import 'package:flutter_test/flutter_test.dart';

import 'test_fixtures.dart';

void main() {
  group('RuntimeController', () {
    test('authenticates and delivers the first renderable surface', () async {
      final call = _FakeFeedCall.open();
      final transport = _FakeUiTransport([call]);
      final runtime = _runtime(transport);

      await _login(runtime, 'bootstrap-once');
      await _eventually(() => transport.watchAfter.isNotEmpty);
      call.add(FeedSurfaceJson(surfaceJsonString(sequence: 1)));
      await _eventually(() => runtime.latestSurface != null);

      expect(transport.loginPassword, 'bootstrap-once');
      expect(runtime.status, RuntimeStatus.streaming);
      expect(runtime.latestSurface?.feedSequence, 1);
      expect(runtime.latestSurface?.payload, isA<NativeSurfacePayload>());
      expect(transport.acknowledged, [1]);

      await runtime.stop();
      expect(call.cancelled, isTrue);
    });

    test(
      'external identity exchange establishes and streams a session',
      () async {
        final call = _FakeFeedCall.open();
        final transport = _FakeUiTransport([call]);
        final runtime = _runtime(transport);

        await runtime.authenticateWithExternalIdentityToken(
          'identityheader.identitypayload.identitysignature',
        );
        await _eventually(() => runtime.status == RuntimeStatus.streaming);

        expect(transport.externalIdentityTokens, [
          'identityheader.identitypayload.identitysignature',
        ]);
        expect(transport.loginPasswords, isEmpty);
        expect(runtime.session.isAuthenticated, isTrue);
        expect(runtime.session.sessionId, 'session-a');
        expect(transport.watchAccessTokens, ['access-token']);

        await runtime.stop();
      },
    );

    test(
      'sign out clears runtime state only after server revocation',
      () async {
        final call = _FakeFeedCall.open();
        final transport = _FakeUiTransport([call]);
        final runtime = _runtime(transport);

        await _login(runtime, 'bootstrap-once');
        await _eventually(() => runtime.status == RuntimeStatus.streaming);
        await runtime.signOut();

        expect(call.cancelled, isTrue);
        expect(transport.logoutRefreshTokens, ['refresh-token']);
        expect(runtime.status, RuntimeStatus.awaitingSignIn);
        expect(runtime.session.isAuthenticated, isFalse);
        expect(runtime.session.identity, isNull);

        await runtime.stop();
      },
    );

    test(
      'failed server revocation retains authentication and resumes streaming',
      () async {
        final initialCall = _FakeFeedCall.open();
        final resumedCall = _FakeFeedCall.open();
        final transport = _FakeUiTransport(
          [initialCall, resumedCall],
          logoutError: const TransportException(
            TransportErrorCode.unavailable,
            'Logout unavailable.',
          ),
        );
        final runtime = _runtime(transport);

        await _login(runtime, 'bootstrap-once');
        await _eventually(() => runtime.status == RuntimeStatus.streaming);
        await expectLater(
          runtime.signOut(),
          throwsA(isA<TransportException>()),
        );
        await _eventually(
          () =>
              runtime.status == RuntimeStatus.streaming &&
              transport.watchAccessTokens.length == 2,
        );

        expect(initialCall.cancelled, isTrue);
        expect(transport.logoutRefreshTokens, ['refresh-token']);
        expect(runtime.session.isAuthenticated, isTrue);
        expect(runtime.session.sessionId, 'session-a');
        expect(runtime.transientError, isA<TransportException>());
        expect(transport.watchAccessTokens, ['access-token', 'access-token']);

        await runtime.stop();
      },
    );

    test('reconnect resumes after the last accepted sequence', () async {
      final first = _FakeFeedCall.fromEvents([
        FeedSurfaceJson(surfaceJsonString(sequence: 1)),
      ]);
      final second = _FakeFeedCall.open();
      final transport = _FakeUiTransport([first, second]);
      final runtime = _runtime(transport);

      await _login(runtime, 'bootstrap-once');
      await _eventually(() => transport.watchAfter.length == 2);

      expect(transport.watchAfter, [0, 1]);
      expect(runtime.latestSurface?.feedSequence, 1);
      expect(runtime.status, RuntimeStatus.streaming);

      await runtime.stop();
    });

    test(
      'client gap preserves the visible surface and converges from a full replay',
      () async {
        final first = _FakeFeedCall.fromEvents([
          FeedSurfaceJson(
            surfaceJsonString(sequence: 1, actions: [testActionJson()]),
          ),
          FeedSurfaceJson(surfaceJsonString(sequence: 3, revision: 2)),
        ]);
        final second = _FakeFeedCall.open();
        final transport = _FakeUiTransport([first, second]);
        final runtime = _runtime(transport);

        await _login(runtime, 'bootstrap-once');
        await _eventually(() => transport.watchAfter.length == 2);

        expect(transport.watchAfter, [0, 0]);
        expect(runtime.lastReset?.reason, 'sequence-gap');
        expect(runtime.feed.surfaces, isEmpty);
        expect(runtime.latestSurface?.feedSequence, 1);
        expect(runtime.canSubmitActionsFrom(runtime.latestSurface!), isFalse);
        second.add(FeedSurfaceJson(surfaceJsonString(sequence: 1)));
        second.add(
          FeedSurfaceJson(surfaceJsonString(sequence: 2, revision: 2)),
        );
        second.add(
          FeedSurfaceJson(surfaceJsonString(sequence: 3, revision: 3)),
        );
        await _eventually(() => runtime.feed.lastSequence == 3);
        expect(runtime.feed.needsReset, isFalse);
        expect(runtime.latestSurface?.feedSequence, 3);
        expect(runtime.latestSurface?.revision, 3);
        expect(runtime.status, RuntimeStatus.streaming);

        await runtime.stop();
      },
    );

    test(
      'server reset atomically installs snapshot then accepts baseline + 1',
      () async {
        final call = _FakeFeedCall.open();
        final transport = _FakeUiTransport([call]);
        final runtime = _runtime(transport);

        await _login(runtime, 'bootstrap-once');
        await _eventually(() => transport.watchAfter.isNotEmpty);
        call.add(
          FeedResetEvent(
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
          FeedSurfaceJson(
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

    test(
      'server reset accepts a complete legacy INO operation and acknowledges it',
      () async {
        final call = _FakeFeedCall.open();
        final transport = _FakeUiTransport([call]);
        final runtime = _runtime(transport);

        await _login(runtime, 'bootstrap-once');
        await _eventually(() => runtime.status == RuntimeStatus.streaming);
        call.add(
          FeedResetEvent(
            reason: 'retention-gap',
            resumeSequence: 1,
            snapshotJson: [
              surfaceJsonString(
                payload: inoConversationPayload(
                  operation: const {'state': 'running', 'retryable': false},
                ),
              ),
            ],
          ),
        );
        await _eventually(() => transport.acknowledged.isNotEmpty);

        final payload = runtime.latestSurface?.payload;
        expect(runtime.status, RuntimeStatus.streaming);
        expect(runtime.feed.lastSequence, 1);
        expect(transport.acknowledged, [1]);
        expect(payload, isA<InoConversationSurfacePayload>());
        final operation = (payload! as InoConversationSurfacePayload).operation;
        expect(operation?.operationId, isEmpty);
        expect(operation?.phase, InoConversationOperationPhase.running);
        expect(operation?.version, 0);

        await runtime.stop();
      },
    );

    final invalidSurfaces = <String, String>{
      'invalid action target': surfaceJsonString(
        payload: inoConversationPayload(
          operation: inoOperation(
            state: 'succeeded',
            action: salesforceConnectionAction(
              target:
                  'https://login.salesforce.com/services/oauth2/authorize?response_type=code',
            ),
          ),
        ),
      ),
      'unsupported capability': jsonEncode(
        surfaceJsonMap()
          ..['requiredClientCapabilities'] = ['ui.payload.future'],
      ),
    };
    for (final invalidSurface in invalidSurfaces.entries) {
      for (final reset in [false, true]) {
        test(
          '${invalidSurface.key} in ${reset ? 'reset' : 'ordinary event'} is terminal without reconnect or ACK',
          () async {
            final first = _FakeFeedCall.open();
            final unusedReconnect = _FakeFeedCall.open();
            final transport = _FakeUiTransport([first, unusedReconnect]);
            final runtime = _runtime(transport);

            await _login(runtime, 'bootstrap-once');
            await _eventually(() => runtime.status == RuntimeStatus.streaming);
            first.add(
              reset
                  ? FeedResetEvent(
                      reason: 'retention-gap',
                      resumeSequence: 1,
                      snapshotJson: [invalidSurface.value],
                    )
                  : FeedSurfaceJson(invalidSurface.value),
            );
            await _eventually(
              () => runtime.status == RuntimeStatus.terminalError,
            );

            expect(runtime.terminalError, isA<ProtocolException>());
            expect(transport.watchAfter, [0]);
            expect(transport.acknowledged, isEmpty);
            expect(unusedReconnect.cancelled, isFalse);

            await runtime.stop();
          },
        );
      }
    }

    test('reconnects after a transient transport failure', () async {
      final first = _FakeFeedCall.error(
        const TransportException(
          TransportErrorCode.unavailable,
          'Temporary runtime feed failure.',
        ),
      );
      final second = _FakeFeedCall.open();
      final transport = _FakeUiTransport([first, second]);
      final runtime = _runtime(transport);

      await _login(runtime, 'bootstrap-once');
      await _eventually(() => transport.watchAfter.length == 2);

      expect(transport.watchAfter, [0, 0]);
      expect(runtime.status, RuntimeStatus.streaming);

      await runtime.stop();
    });

    test(
      'rotates refresh token after server rejects an access session',
      () async {
        final first = _FakeFeedCall.error(
          const AuthenticationException('Access session expired.'),
        );
        final second = _FakeFeedCall.open();
        final transport = _FakeUiTransport([first, second]);
        final runtime = _runtime(transport);

        await _login(runtime, 'bootstrap-once');
        await _eventually(() => transport.watchAfter.length == 2);

        expect(transport.refreshCount, 1);
        expect(transport.watchAccessTokens, [
          'access-token',
          'access-refreshed',
        ]);
        expect(runtime.status, RuntimeStatus.streaming);

        await runtime.stop();
      },
    );

    test(
      'transient refresh failure after feed authentication rejection reconnects',
      () async {
        final first = _FakeFeedCall.error(
          const AuthenticationException('Access session expired.'),
        );
        final second = _FakeFeedCall.open();
        final transport = _FakeUiTransport(
          [first, second],
          refreshError: const TransportException(
            TransportErrorCode.unavailable,
            'Session refresh is temporarily unavailable.',
          ),
        );
        final runtime = _runtime(transport);

        await _login(runtime, 'bootstrap-once');
        await _eventually(() => transport.watchAfter.length == 2);

        expect(transport.refreshCount, 1);
        expect(runtime.session.isAuthenticated, isTrue);
        expect(runtime.status, RuntimeStatus.streaming);
        expect(runtime.retainAuthenticatedContentForReauthentication, isFalse);
        expect(transport.watchAccessTokens, ['access-token', 'access-token']);

        await runtime.stop();
      },
    );

    test(
      'terminal stream error is never overwritten by subsequent done',
      () async {
        const original = TransportException(
          TransportErrorCode.unavailable,
          'Original authenticated feed error.',
        );
        final transport = _FakeUiTransport([_FakeFeedCall.error(original)]);
        final runtime = _runtime(
          transport,
          reconnectPolicy: const ReconnectPolicy(
            delays: [Duration.zero],
            maxAttempts: 0,
          ),
        );

        await _login(runtime, 'bootstrap-once');
        await _eventually(() => runtime.status == RuntimeStatus.terminalError);

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

        await _login(runtime, 'bootstrap-once');
        await _eventually(() => runtime.status == RuntimeStatus.streaming);
        await runtime.stop();

        expect(call.cancelled, isTrue);
        expect(runtime.status, RuntimeStatus.stopped);
        expect(transport.watchAfter, [0]);
        expect(transport.closed, isTrue);
      },
    );

    test('stop closes transport before awaiting an in-flight ACK', () async {
      final acknowledgementGate = Completer<void>();
      final call = _FakeFeedCall.open();
      final transport = _FakeUiTransport([
        call,
      ], acknowledgementGate: acknowledgementGate);
      final runtime = _runtime(transport);

      await _login(runtime, 'bootstrap-once');
      await _eventually(() => runtime.status == RuntimeStatus.streaming);
      call.add(FeedSurfaceJson(surfaceJsonString(sequence: 1)));
      await _eventually(() => transport.acknowledged.isNotEmpty);

      await runtime.stop().timeout(const Duration(seconds: 1));

      expect(transport.closed, isTrue);
      expect(acknowledgementGate.isCompleted, isTrue);
      expect(runtime.status, RuntimeStatus.stopped);
      expect(transport.watchAfter, [0]);
    });

    test('stop followed by dispose closes the transport once', () async {
      final transport = _FakeUiTransport([]);
      final runtime = _runtime(transport);

      await runtime.stop();
      runtime.dispose();
      await Future<void>.delayed(Duration.zero);

      expect(transport.closeCount, 1);
    });

    test('concurrent stops await one complete shutdown', () async {
      final cancellationGate = Completer<void>();
      final call = _FakeFeedCall.open(cancelCompletionGate: cancellationGate);
      final transport = _FakeUiTransport([call]);
      final runtime = _runtime(transport);

      await _login(runtime, 'bootstrap-once');
      await _eventually(() => runtime.status == RuntimeStatus.streaming);

      var firstCompleted = false;
      var secondCompleted = false;
      final first = runtime.stop().whenComplete(() => firstCompleted = true);
      await _eventually(() => call.cancelled && transport.closeCount == 1);
      final second = runtime.stop().whenComplete(() => secondCompleted = true);
      await Future<void>.delayed(Duration.zero);

      expect(transport.closeCount, 1);
      expect(firstCompleted, isFalse);
      expect(secondCompleted, isFalse);
      expect(runtime.status, isNot(RuntimeStatus.stopped));

      cancellationGate.complete();
      await Future.wait([first, second]);

      expect(transport.closeCount, 1);
      expect(firstCompleted, isTrue);
      expect(secondCompleted, isTrue);
      expect(runtime.status, RuntimeStatus.stopped);
      runtime.dispose();
      await Future<void>.delayed(Duration.zero);
      expect(transport.closeCount, 1);
    });

    test('closing stop upgrades and awaits a restartable stop', () async {
      final cancellationGate = Completer<void>();
      final call = _FakeFeedCall.open(cancelCompletionGate: cancellationGate);
      final transport = _FakeUiTransport([call]);
      final runtime = _runtime(transport);

      await _login(runtime, 'bootstrap-once');
      await _eventually(() => runtime.status == RuntimeStatus.streaming);

      var restartableCompleted = false;
      var closingCompleted = false;
      final restartable = runtime
          .stop(closeTransport: false, invalidateAuthentication: false)
          .whenComplete(() => restartableCompleted = true);
      await _eventually(() => call.cancelled);
      final closing = runtime.stop().whenComplete(
        () => closingCompleted = true,
      );
      await Future<void>.delayed(Duration.zero);

      expect(transport.closeCount, 1);
      expect(restartableCompleted, isFalse);
      expect(closingCompleted, isFalse);
      expect(runtime.status, isNot(RuntimeStatus.stopped));

      cancellationGate.complete();
      await Future.wait([restartable, closing]);

      expect(transport.closeCount, 1);
      expect(restartableCompleted, isTrue);
      expect(closingCompleted, isTrue);
      expect(runtime.status, RuntimeStatus.stopped);
      runtime.dispose();
      await Future<void>.delayed(Duration.zero);
      expect(transport.closeCount, 1);
    });

    test('async close failure still completes shared shutdown', () async {
      final failure = StateError('Async close failed.');
      final cancellationGate = Completer<void>();
      final call = _FakeFeedCall.open(cancelCompletionGate: cancellationGate);
      final transport = _FakeUiTransport([call], closeError: failure);
      final runtime = _runtime(transport);

      await _login(runtime, 'bootstrap-once');
      await _eventually(() => runtime.status == RuntimeStatus.streaming);

      final first = expectLater(runtime.stop(), throwsA(same(failure)));
      final second = expectLater(runtime.stop(), throwsA(same(failure)));
      await _eventually(() => call.cancelled && transport.closeCount == 1);

      expect(runtime.status, isNot(RuntimeStatus.stopped));

      cancellationGate.complete();
      await Future.wait([first, second]);
      runtime.dispose();
      await Future<void>.delayed(Duration.zero);

      expect(call.cancelled, isTrue);
      expect(transport.closeCount, 1);
      expect(runtime.status, RuntimeStatus.stopped);
    });

    test('synchronous close failure is acquired once', () async {
      final failure = StateError('Synchronous close failed.');
      final transport = _FakeUiTransport(
        [],
        closeError: failure,
        closeSynchronously: true,
      );
      final runtime = _runtime(transport);

      final first = expectLater(runtime.stop(), throwsA(same(failure)));
      final second = expectLater(runtime.stop(), throwsA(same(failure)));
      await Future.wait([first, second]);
      runtime.dispose();
      await Future<void>.delayed(Duration.zero);

      expect(transport.closeCount, 1);
      expect(runtime.status, RuntimeStatus.stopped);
    });

    test('terminal shutdown prevents a later start', () async {
      final initialCall = _FakeFeedCall.open();
      final forbiddenCall = _FakeFeedCall.open();
      final transport = _FakeUiTransport([initialCall, forbiddenCall]);
      addTearDown(() async {
        if (transport.watchAfter.length > 1) await forbiddenCall.cancel();
      });
      final runtime = _runtime(
        transport,
        reconnectPolicy: const ReconnectPolicy(
          delays: [Duration.zero],
          maxAttempts: 0,
        ),
      );

      await _login(runtime, 'bootstrap-once');
      await _eventually(() => runtime.status == RuntimeStatus.streaming);
      await runtime.stop();
      await runtime.start();
      await Future<void>.delayed(Duration.zero);

      expect(transport.watchAfter, [0]);
      expect(transport.closeCount, 1);
      expect(runtime.status, RuntimeStatus.stopped);
    });

    test('terminal shutdown prevents a later login', () async {
      final forbiddenCall = _FakeFeedCall.open();
      final transport = _FakeUiTransport([forbiddenCall]);
      addTearDown(() async {
        if (transport.watchAfter.isNotEmpty) await forbiddenCall.cancel();
      });
      final runtime = _runtime(
        transport,
        reconnectPolicy: const ReconnectPolicy(
          delays: [Duration.zero],
          maxAttempts: 0,
        ),
      );

      await runtime.stop();
      await runtime.authenticateWithPassword(
        username: 'local-owner',
        password: 'must-not-run',
      );
      await Future<void>.delayed(Duration.zero);

      expect(transport.loginPasswords, isEmpty);
      expect(transport.watchAfter, isEmpty);
      expect(runtime.session.isAuthenticated, isFalse);
      expect(runtime.status, RuntimeStatus.stopped);
    });

    test('terminal shutdown prevents a later sign out', () async {
      final call = _FakeFeedCall.open();
      final transport = _FakeUiTransport([call]);
      final runtime = _runtime(transport);

      await _login(runtime, 'bootstrap-once');
      await _eventually(() => runtime.status == RuntimeStatus.streaming);
      await runtime.stop();
      await runtime.signOut();

      expect(transport.logoutRefreshTokens, isEmpty);
      expect(runtime.session.isAuthenticated, isFalse);
      expect(runtime.status, RuntimeStatus.stopped);
    });

    test('terminal shutdown invalidates an in-flight login', () async {
      final pendingLogin = Completer<SessionBundle>();
      final transport = _FakeUiTransport(
        [],
        loginResults: [pendingLogin.future],
      );
      final runtime = _runtime(transport);

      final authentication = runtime.authenticateWithPassword(
        username: 'local-owner',
        password: 'pending-login',
      );
      await _eventually(() => transport.loginPasswords.isNotEmpty);
      await runtime.stop();
      pendingLogin.complete(testSession());
      await authentication;

      expect(runtime.session.isAuthenticated, isFalse);
      expect(runtime.status, RuntimeStatus.stopped);
      expect(transport.watchAfter, isEmpty);
      expect(transport.closeCount, 1);
    });

    test('terminal stop cancels a feed acquired after close', () async {
      final pendingFeed = Completer<_FakeFeedCall>();
      final lateCall = _FakeFeedCall.open();
      final transport = _FakeUiTransport([pendingFeed.future]);
      final runtime = _runtime(transport);

      await _login(runtime, 'bootstrap-once');
      await _eventually(() => transport.watchAfter.isNotEmpty);

      var stopCompleted = false;
      final stopping = runtime.stop().whenComplete(() => stopCompleted = true);
      await Future<void>.delayed(Duration.zero);
      expect(stopCompleted, isFalse);

      pendingFeed.complete(lateCall);
      await Future<void>.delayed(Duration.zero);
      final cancelledBeforeCleanup = lateCall.cancelled;
      if (!cancelledBeforeCleanup) await lateCall.cancel();
      await stopping;

      expect(cancelledBeforeCleanup, isTrue);
      expect(transport.closeCount, 1);
      expect(runtime.status, RuntimeStatus.stopped);
    });

    test('superseded run cancels a late feed acquisition', () async {
      final pendingFeed = Completer<_FakeFeedCall>();
      final staleCall = _FakeFeedCall.open();
      final currentCall = _FakeFeedCall.open();
      final transport = _FakeUiTransport(
        [pendingFeed.future, currentCall],
        loginResults: [
          testSession(),
          testSession(
            identity: testIdentity(session: 'session-later'),
            accessToken: 'access-later',
            refreshToken: 'refresh-later',
          ),
        ],
      );
      final runtime = _runtime(
        transport,
        reconnectPolicy: const ReconnectPolicy(
          delays: [Duration.zero],
          maxAttempts: 0,
        ),
      );

      await _login(runtime, 'initial');
      await _eventually(() => transport.watchAfter.length == 1);

      final olderAuthentication = _login(runtime, 'older');
      await Future<void>.delayed(Duration.zero);
      final laterAuthentication = _login(runtime, 'later');
      await laterAuthentication;
      await _eventually(
        () =>
            transport.watchAfter.length == 2 &&
            runtime.status == RuntimeStatus.streaming,
      );

      pendingFeed.complete(staleCall);
      await Future<void>.delayed(Duration.zero);
      final cancelledBeforeCleanup = staleCall.cancelled;
      final currentCancelledBeforeCleanup = currentCall.cancelled;
      if (!cancelledBeforeCleanup) await staleCall.cancel();
      await olderAuthentication;
      if (!currentCall.cancelled) await currentCall.cancel();
      await runtime.stop();

      expect(cancelledBeforeCleanup, isTrue);
      expect(currentCancelledBeforeCleanup, isFalse);
      expect(transport.loginPasswords, ['initial', 'later']);
      expect(transport.watchAccessTokens, ['access-token', 'access-later']);
      expect(transport.closeCount, 1);
    });

    test('superseded run ignores a stale acknowledgement completion', () async {
      final acknowledgementGate = Completer<void>();
      final staleCall = _FakeFeedCall.open();
      final currentCall = _FakeFeedCall.open();
      final feed = _RecordingFeedController();
      final transport = _FakeUiTransport(
        [staleCall, currentCall],
        loginResults: [
          testSession(),
          testSession(
            identity: testIdentity(session: 'session-later'),
            accessToken: 'access-later',
            refreshToken: 'refresh-later',
          ),
        ],
        acknowledgementGate: acknowledgementGate,
      );
      final runtime = _runtime(transport, feed: feed);

      await _login(runtime, 'initial');
      await _eventually(() => runtime.status == RuntimeStatus.streaming);
      staleCall.add(FeedSurfaceJson(surfaceJsonString(sequence: 1)));
      await _eventually(() => transport.acknowledged.length == 1);

      final olderAuthentication = _login(runtime, 'older');
      await _eventually(() => staleCall.cancelled);
      final laterAuthentication = _login(runtime, 'later');
      await laterAuthentication;
      await _eventually(
        () =>
            transport.watchAfter.length == 2 &&
            runtime.status == RuntimeStatus.streaming,
      );

      acknowledgementGate.complete();
      await olderAuthentication;
      final statusBeforeCleanup = runtime.status;
      await runtime.stop();

      expect(feed.acknowledged, isEmpty);
      expect(statusBeforeCleanup, RuntimeStatus.streaming);
    });

    test('superseded run ignores a stale refresh failure', () async {
      final pendingRefresh = Completer<SessionBundle>();
      final currentCall = _FakeFeedCall.open();
      final transport = _FakeUiTransport(
        [
          _FakeFeedCall.error(
            const AuthenticationException('Initial access expired.'),
          ),
          currentCall,
        ],
        loginResults: [
          testSession(),
          testSession(
            identity: testIdentity(session: 'session-later'),
            accessToken: 'access-later',
            refreshToken: 'refresh-later',
          ),
        ],
        refreshResult: pendingRefresh.future,
      );
      final runtime = _runtime(transport);

      await _login(runtime, 'initial');
      await _eventually(() => transport.refreshCount == 1);

      final olderAuthentication = _login(runtime, 'older');
      final laterAuthentication = _login(runtime, 'later');
      await laterAuthentication;
      await _eventually(
        () =>
            transport.watchAfter.length == 2 &&
            runtime.status == RuntimeStatus.streaming,
      );

      pendingRefresh.complete(testSession());
      await olderAuthentication;
      final statusBeforeCleanup = runtime.status;
      final terminalErrorBeforeCleanup = runtime.terminalError;
      final sessionBeforeCleanup = runtime.session.sessionId;
      await runtime.stop();

      expect(statusBeforeCleanup, RuntimeStatus.streaming);
      expect(terminalErrorBeforeCleanup, isNull);
      expect(sessionBeforeCleanup, 'session-later');
    });

    test(
      'submits only the current action binding and its signed token',
      () async {
        final call = _FakeFeedCall.open();
        final transport = _FakeUiTransport([call]);
        final runtime = _runtime(transport);
        final json = surfaceJsonString(
          sequence: 1,
          payload: inoConversationPayload(),
          actions: [testInoActionJson()],
        );

        await _login(runtime, 'bootstrap-once');
        await _eventually(() => runtime.status == RuntimeStatus.streaming);
        call.add(FeedSurfaceJson(json));
        await _eventually(() => runtime.latestSurface != null);
        final surface = runtime.latestSurface!;
        final result = await runtime.submitAction(surface, 'ino.send', const {
          'prompt': 'What can you help me with?',
        }, now: testNow);

        expect(result.operationId, 'operation-a');
        expect(
          transport.submittedAction?.actionToken,
          'signed-ino-action-token',
        );
        expect(transport.submittedInput, {
          'prompt': 'What can you help me with?',
        });
        expect(
          () => runtime.submitAction(
            surface,
            'forged-binding',
            const {},
            now: testNow,
          ),
          throwsA(isA<StateError>()),
        );
        expect(
          () => runtime.submitAction(
            surface,
            'ino.send',
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
          loginResults: [
            testSession(),
            testSession(identity: testIdentity(session: 'session-b')),
          ],
        );
        final runtime = _runtime(transport);

        await _login(runtime, 'bootstrap-a');
        await _eventually(() => runtime.status == RuntimeStatus.streaming);
        first.add(
          FeedSurfaceJson(
            surfaceJsonString(sequence: 1, actions: [testActionJson()]),
          ),
        );
        await _eventually(() => runtime.latestSurface != null);
        final priorSurface = runtime.latestSurface!;
        runtime.lastReset = const FeedReset('prior-scope');
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

        await _login(runtime, 'bootstrap-b');
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
            now: testNow,
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

        await _login(runtime, 'bootstrap-once');
        await _eventually(() => runtime.status == RuntimeStatus.streaming);
        first.add(
          FeedSurfaceJson(
            surfaceJsonString(
              sequence: 1,
              payload: inoConversationPayload(),
              actions: [testInoActionJson()],
            ),
          ),
        );
        await _eventually(() => runtime.latestSurface != null);
        final surface = runtime.latestSurface;
        final epoch = runtime.scopeEpoch;

        first.addError(
          const TransportException(
            TransportErrorCode.unavailable,
            'Temporary runtime feed failure.',
          ),
        );
        await _eventually(() => transport.watchAfter.length == 2);

        expect(runtime.scopeEpoch, epoch);
        expect(runtime.feed.lastSequence, 1);
        expect(runtime.latestSurface, same(surface));
        expect(
          runtime.latestSurface?.payload,
          isA<InoConversationSurfacePayload>(),
        );
        expect(runtime.status, RuntimeStatus.streaming);

        await runtime.stop();
      },
    );

    test('failed login clears protected in-memory scope state', () async {
      final call = _FakeFeedCall.open();
      final transport = _FakeUiTransport(
        [call],
        loginResults: [
          testSession(),
          const AuthenticationException('Bootstrap was denied.'),
        ],
      );
      final runtime = _runtime(transport);

      await _login(runtime, 'bootstrap-once');
      await _eventually(() => runtime.status == RuntimeStatus.streaming);
      call.add(FeedSurfaceJson(surfaceJsonString(sequence: 1)));
      await _eventually(() => runtime.latestSurface != null);
      runtime.lastReset = const FeedReset('prior-scope');
      final epoch = runtime.scopeEpoch;

      await expectLater(
        _login(runtime, 'bootstrap-invalid'),
        throwsA(isA<AuthenticationException>()),
      );

      expect(runtime.scopeEpoch, epoch + 1);
      expect(runtime.status, RuntimeStatus.awaitingSignIn);
      expect(runtime.session.identity, isNull);
      expect(runtime.feed.surfaces, isEmpty);
      expect(runtime.feed.lastSequence, 0);
      expect(runtime.latestSurface, isNull);
      expect(runtime.lastReset, isNull);
      expect(
        () => runtime.feed.accept(testSurface(sequence: 2, revision: 2)),
        throwsA(isA<ScopeViolation>()),
      );

      await runtime.stop();
    });

    test('an older login failure cannot sign out a newer runtime', () async {
      final older = Completer<SessionBundle>();
      final newer = Completer<SessionBundle>();
      final call = _FakeFeedCall.open();
      final transport = _FakeUiTransport(
        [call],
        loginResults: [older.future, newer.future],
      );
      final runtime = _runtime(transport);

      final olderAuthentication = _login(runtime, 'older');
      await _eventually(() => transport.loginPasswords.length == 1);
      final newerAuthentication = _login(runtime, 'newer');
      await _eventually(() => transport.loginPasswords.length == 2);
      newer.complete(
        testSession(
          identity: testIdentity(session: 'session-newer'),
          accessToken: 'access-newer',
          refreshToken: 'refresh-newer',
        ),
      );
      await newerAuthentication;
      await _eventually(() => runtime.status == RuntimeStatus.streaming);

      older.completeError(
        const AuthenticationException('Older login was rejected.'),
      );
      await olderAuthentication;

      expect(runtime.status, RuntimeStatus.streaming);
      expect(runtime.session.sessionId, 'session-newer');
      expect(runtime.transientError, isNull);
      expect(transport.watchAccessTokens, ['access-newer']);
      await runtime.stop();
    });

    test(
      'a later authentication intent wins while an older stop is blocked',
      () async {
        final cancelGate = Completer<void>();
        final firstCall = _FakeFeedCall.open(cancelCompletionGate: cancelGate);
        final laterCall = _FakeFeedCall.open();
        final laterLogin = Completer<SessionBundle>();
        final transport = _FakeUiTransport(
          [firstCall, laterCall],
          loginResults: [testSession(), laterLogin.future],
        );
        final runtime = _runtime(transport);
        await _login(runtime, 'initial');
        await _eventually(() => runtime.status == RuntimeStatus.streaming);

        final olderAuthentication = _login(runtime, 'older');
        await _eventually(() => firstCall.cancelled);
        final laterAuthentication = _login(runtime, 'later');
        await _eventually(() => transport.loginPasswords.length == 2);
        laterLogin.complete(
          testSession(
            identity: testIdentity(session: 'session-later'),
            accessToken: 'access-later',
            refreshToken: 'refresh-later',
          ),
        );
        await laterAuthentication;
        await _eventually(() => runtime.status == RuntimeStatus.streaming);

        cancelGate.complete();
        await olderAuthentication;

        expect(transport.loginPasswords, ['initial', 'later']);
        expect(runtime.session.sessionId, 'session-later');
        expect(transport.watchAccessTokens, ['access-token', 'access-later']);
        await runtime.stop();
      },
    );

    test(
      'authentication expiry clears protected in-memory scope state',
      () async {
        final call = _FakeFeedCall.open();
        final transport = _FakeUiTransport([
          call,
        ], refreshError: const AuthenticationException('Refresh was denied.'));
        final runtime = _runtime(transport);

        await _login(runtime, 'bootstrap-once');
        await _eventually(() => runtime.status == RuntimeStatus.streaming);
        call.add(FeedSurfaceJson(surfaceJsonString(sequence: 1)));
        await _eventually(() => runtime.latestSurface != null);
        runtime.lastReset = const FeedReset('prior-scope');
        final epoch = runtime.scopeEpoch;

        call.addError(const AuthenticationException('Session expired.'));
        await _eventually(() => runtime.status == RuntimeStatus.awaitingSignIn);

        expect(runtime.scopeEpoch, epoch + 1);
        expect(runtime.session.identity, isNull);
        expect(runtime.feed.surfaces, isEmpty);
        expect(runtime.feed.lastSequence, 0);
        expect(runtime.latestSurface, isNull);
        expect(runtime.lastReset, isNull);
        expect(
          () => runtime.feed.accept(testSurface(sequence: 2, revision: 2)),
          throwsA(isA<ScopeViolation>()),
        );

        await runtime.stop();
      },
    );

    test('rejects an action receipt completed after a scope change', () async {
      final first = _FakeFeedCall.open();
      final second = _FakeFeedCall.open();
      final receipt = Completer<ActionResult>();
      final transport = _FakeUiTransport(
        [first, second],
        loginResults: [
          testSession(),
          testSession(
            identity: testIdentity(actor: 'actor-b', session: 'session-b'),
          ),
        ],
        actionResult: receipt.future,
      );
      final runtime = _runtime(transport);

      await _login(runtime, 'bootstrap-a');
      await _eventually(() => runtime.status == RuntimeStatus.streaming);
      first.add(
        FeedSurfaceJson(
          surfaceJsonString(sequence: 1, actions: [testActionJson()]),
        ),
      );
      await _eventually(() => runtime.latestSurface != null);
      final surface = runtime.latestSurface!;
      final submission = runtime.submitAction(
        surface,
        'refresh-binding',
        const <String, Object?>{},
        now: testNow,
      );
      await _eventually(() => transport.submittedAction != null);

      await _login(runtime, 'bootstrap-b');
      await _eventually(() => transport.watchAfter.length == 2);
      receipt.complete(
        const ActionResult(
          operationId: 'operation-a',
          idempotencyKey: 'idempotency-a',
        ),
      );

      await expectLater(submission, throwsA(isA<StateError>()));

      await runtime.stop();
    });
  });
}

RuntimeController _runtime(
  _FakeUiTransport transport, {
  FeedController? feed,
  ReconnectPolicy reconnectPolicy = const ReconnectPolicy(
    delays: [Duration.zero],
    maxAttempts: 3,
  ),
}) => RuntimeController(
  transport: transport,
  feed: feed,
  reconnectPolicy: reconnectPolicy,
  delay: (_) async {},
);

Future<void> _login(RuntimeController runtime, String password) =>
    runtime.authenticateWithPassword(username: 'admin', password: password);

Future<void> _eventually(bool Function() condition) async {
  for (var attempt = 0; attempt < 100; attempt++) {
    if (condition()) return;
    await Future<void>.delayed(const Duration(milliseconds: 1));
  }
  fail('Condition was not reached.');
}

class _FakeUiTransport implements UiTransport, ExternalSessionTransport {
  _FakeUiTransport(
    Iterable<Object> calls, {
    Iterable<Object>? loginResults,
    this.refreshError,
    this.refreshResult,
    this.actionResult,
    this.acknowledgementGate,
    this.logoutError,
    this.closeError,
    this.closeSynchronously = false,
  }) : _calls = Queue.of(calls),
       _loginResults = Queue.of(loginResults ?? <Object>[testSession()]);

  final Queue<Object> _calls;
  final Queue<Object> _loginResults;
  final Object? refreshError;
  final Future<SessionBundle>? refreshResult;
  final Future<ActionResult>? actionResult;
  final Completer<void>? acknowledgementGate;
  final Object? logoutError;
  final Object? closeError;
  final bool closeSynchronously;
  final List<int> watchAfter = [];
  final List<String> watchAccessTokens = [];
  final List<int> acknowledged = [];
  final List<String> loginPasswords = [];
  final List<String> externalIdentityTokens = [];
  final List<String> logoutRefreshTokens = [];
  String? loginPassword;
  UiActionRef? submittedAction;
  Map<String, Object?>? submittedInput;
  bool closed = false;
  int closeCount = 0;
  int refreshCount = 0;

  @override
  Future<SessionBundle> login({
    required String username,
    required String password,
  }) async {
    loginPassword = password;
    loginPasswords.add(password);
    return _nextLoginResult();
  }

  @override
  Future<SessionBundle> loginExternal(String identityToken) async {
    externalIdentityTokens.add(identityToken);
    return _nextLoginResult();
  }

  Future<SessionBundle> _nextLoginResult() async {
    if (_loginResults.isEmpty) {
      throw StateError('No fake login result is available.');
    }
    final result = _loginResults.removeFirst();
    if (result is SessionBundle) return result;
    if (result is Future<SessionBundle>) return await result;
    throw result;
  }

  @override
  Future<SessionBundle> refreshSession({required String refreshToken}) async {
    refreshCount++;
    final error = refreshError;
    if (error != null) throw error;
    final result = refreshResult;
    if (result != null) return result;
    return testSession(
      accessToken: 'access-refreshed',
      refreshToken: 'refresh-rotated',
    );
  }

  @override
  Future<void> logout({required String refreshToken}) async {
    logoutRefreshTokens.add(refreshToken);
    final error = logoutError;
    if (error != null) throw error;
  }

  @override
  Future<FeedCall> watchSurfaceFeed({
    required String accessToken,
    required int afterSequence,
    required FeedAudience audience,
    required Set<String> clientCapabilities,
    required int maxBatchSize,
  }) async {
    watchAfter.add(afterSequence);
    watchAccessTokens.add(accessToken);
    if (_calls.isEmpty) {
      throw const TransportException(
        TransportErrorCode.unavailable,
        'No fake feed is available.',
      );
    }
    final result = _calls.removeFirst();
    if (result is _FakeFeedCall) return result;
    if (result is Future<_FakeFeedCall>) return await result;
    throw result;
  }

  @override
  Future<void> acknowledgeSurfaceFeed({
    required String accessToken,
    required FeedAudience audience,
    required int sequence,
  }) async {
    acknowledged.add(sequence);
    final gate = acknowledgementGate;
    if (gate != null) await gate.future;
  }

  @override
  Future<ActionResult> submitAction({
    required String accessToken,
    required UiActionRef action,
    required Map<String, Object?> input,
  }) async {
    submittedAction = action;
    submittedInput = input;
    final pendingResult = actionResult;
    if (pendingResult != null) return pendingResult;
    return const ActionResult(
      operationId: 'operation-a',
      idempotencyKey: 'idempotency-a',
    );
  }

  @override
  Future<void> close() {
    closeCount++;
    closed = true;
    final gate = acknowledgementGate;
    if (gate != null && !gate.isCompleted) gate.complete();
    final failure = closeError;
    if (failure == null) return Future<void>.value();
    if (closeSynchronously) throw failure;
    return Future<void>.error(failure);
  }
}

class _RecordingFeedController extends FeedController {
  final List<int> acknowledged = [];

  @override
  void acknowledge(int sequence) {
    acknowledged.add(sequence);
    super.acknowledge(sequence);
  }
}

class _FakeFeedCall implements FeedCall {
  _FakeFeedCall._(this._controller, [this._cancelCompletionGate]);

  factory _FakeFeedCall.open({Completer<void>? cancelCompletionGate}) =>
      _FakeFeedCall._(StreamController<FeedEvent>(), cancelCompletionGate);

  factory _FakeFeedCall.fromEvents(Iterable<FeedEvent> events) {
    final controller = StreamController<FeedEvent>();
    scheduleMicrotask(() async {
      for (final event in events) {
        if (!controller.isClosed) controller.add(event);
      }
      if (!controller.isClosed) await controller.close();
    });
    return _FakeFeedCall._(controller);
  }

  factory _FakeFeedCall.error(Object error) {
    final controller = StreamController<FeedEvent>();
    scheduleMicrotask(() async {
      if (!controller.isClosed) controller.addError(error);
      if (!controller.isClosed) await controller.close();
    });
    return _FakeFeedCall._(controller);
  }

  final StreamController<FeedEvent> _controller;
  final Completer<void>? _cancelCompletionGate;
  bool cancelled = false;

  void add(FeedEvent event) => _controller.add(event);

  void addError(Object error) => _controller.addError(error);

  @override
  Stream<FeedEvent> get events => _controller.stream;

  @override
  Future<void> cancel() async {
    cancelled = true;
    if (!_controller.isClosed) {
      final hadListener = _controller.hasListener;
      final closing = _controller.close();
      if (hadListener) await closing;
    }
    final gate = _cancelCompletionGate;
    if (gate != null) await gate.future;
  }
}
