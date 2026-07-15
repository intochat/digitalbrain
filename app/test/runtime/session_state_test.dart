import 'dart:async';

import 'package:digitalbrain_flutter/runtime/runtime.dart';
import 'package:flutter_test/flutter_test.dart';

import 'test_fixtures.dart';

void main() {
  group('SessionController login', () {
    test('an older login failure cannot clear a newer session', () async {
      final transport = _PendingLoginTransport();
      final controller = SessionController(now: () => testNow);
      final older = controller.login(
        transport,
        username: 'admin',
        password: 'older',
      );
      final newer = controller.login(
        transport,
        username: 'admin',
        password: 'newer',
      );

      transport.pending['newer']!.complete(
        testSession(accessToken: 'access-newer', refreshToken: 'refresh-newer'),
      );
      expect(await newer, isTrue);
      transport.pending['older']!.completeError(
        const AuthenticationException('Older login was rejected.'),
      );
      expect(await older, isFalse);

      expect(controller.status, SessionStatus.authenticated);
      expect(controller.lastError, isNull);
      expect(await controller.accessToken(transport), 'access-newer');
    });

    test('an older login success cannot replace a newer session', () async {
      final transport = _PendingLoginTransport();
      final controller = SessionController(now: () => testNow);
      final older = controller.login(
        transport,
        username: 'admin',
        password: 'older',
      );
      final newer = controller.login(
        transport,
        username: 'admin',
        password: 'newer',
      );

      transport.pending['newer']!.complete(
        testSession(accessToken: 'access-newer', refreshToken: 'refresh-newer'),
      );
      await newer;
      transport.pending['older']!.complete(
        testSession(accessToken: 'access-older', refreshToken: 'refresh-older'),
      );
      expect(await older, isFalse);

      expect(controller.status, SessionStatus.authenticated);
      expect(await controller.accessToken(transport), 'access-newer');
    });

    test('external login establishes the server-derived session', () async {
      final transport = _ExternalLoginTransport();
      final controller = SessionController(now: () => testNow);

      final established = await controller.loginExternal(
        transport,
        'identityheader.identitypayload.identitysignature',
      );

      expect(established, isTrue);
      expect(transport.identityTokens, [
        'identityheader.identitypayload.identitysignature',
      ]);
      expect(controller.status, SessionStatus.authenticated);
      expect(controller.sessionId, 'session-a');
      expect(controller.ownerId, 'owner-a');
      expect(controller.actorId, 'actor-a');
    });
  });

  group('SessionController refresh', () {
    test('concurrent access-token callers share one refresh', () async {
      final pendingRefresh = Completer<SessionBundle>();
      final transport = _PendingRefreshTransport(pendingRefresh);
      final controller = SessionController(now: () => testNow)
        ..establish(
          testSession(
            accessToken: 'access-old',
            refreshToken: 'refresh-old',
            accessExpiresAt: testNow.add(const Duration(seconds: 5)),
          ),
        );

      final first = controller.accessToken(transport);
      final second = controller.accessToken(transport);

      expect(transport.refreshTokens, ['refresh-old']);
      expect(controller.status, SessionStatus.refreshing);
      expect(controller.isAuthenticated, isTrue);

      pendingRefresh.complete(
        testSession(accessToken: 'access-new', refreshToken: 'refresh-new'),
      );

      expect(await Future.wait([first, second]), ['access-new', 'access-new']);
      expect(transport.refreshTokens, ['refresh-old']);
      expect(controller.status, SessionStatus.authenticated);
      expect(controller.lastError, isNull);
    });

    test('a transient refresh failure keeps the current session', () async {
      final pendingRefresh = Completer<SessionBundle>();
      final transport = _PendingRefreshTransport(pendingRefresh);
      final controller = SessionController(now: () => testNow)
        ..establish(
          testSession(
            accessToken: 'access-current',
            refreshToken: 'refresh-current',
          ),
        );
      final refresh = controller.refreshAccessToken(transport);
      final unavailable = const TransportException(
        TransportErrorCode.unavailable,
        'Session refresh is temporarily unavailable.',
      );

      pendingRefresh.completeError(unavailable);
      await expectLater(refresh, throwsA(same(unavailable)));

      expect(controller.status, SessionStatus.authenticated);
      expect(controller.isAuthenticated, isTrue);
      expect(controller.lastError, same(unavailable));
      expect(await controller.accessToken(transport), 'access-current');
    });

    test(
      'sign out fences token access before cancellation completes',
      () async {
        final transport = _GatedSignOutTransport();
        final controller = SessionController(now: () => testNow)
          ..establish(testSession());

        final signOut = controller.signOut(transport);
        await Future<void>.delayed(Duration.zero);

        expect(controller.isAuthenticated, isFalse);
        await expectLater(
          controller.accessToken(transport),
          throwsA(isA<AuthenticationException>()),
        );
        expect(transport.logoutStarted, isFalse);
        transport.cancellation.complete();
        await Future<void>.delayed(Duration.zero);
        expect(transport.logoutStarted, isTrue);
        transport.logoutCompletion.complete();
        await signOut;
        expect(controller.status, SessionStatus.signedOut);
      },
    );

    test(
      'sign out invalidates a refresh that completes before logout',
      () async {
        final transport = _RefreshDuringSignOutTransport();
        final controller = SessionController(now: () => testNow)
          ..establish(
            testSession(accessToken: 'access-old', refreshToken: 'refresh-old'),
          );
        final refresh = controller.refreshAccessToken(transport);

        final signOut = controller.signOut(transport);
        await Future<void>.delayed(Duration.zero);
        transport.refresh.complete(
          testSession(accessToken: 'access-new', refreshToken: 'refresh-new'),
        );
        await expectLater(refresh, throwsA(isA<AuthenticationException>()));
        transport.logoutCompletion.complete();
        await signOut;

        expect(transport.logoutTokens, ['refresh-old']);
        expect(controller.status, SessionStatus.signedOut);
        expect(controller.isAuthenticated, isFalse);
        await expectLater(
          controller.accessToken(transport),
          throwsA(isA<AuthenticationException>()),
        );
      },
    );

    test('an older refresh failure cannot expire a newer bundle', () async {
      final pendingRefresh = Completer<SessionBundle>();
      final transport = _PendingRefreshTransport(pendingRefresh);
      final controller = SessionController(now: () => testNow)
        ..establish(
          testSession(accessToken: 'access-old', refreshToken: 'refresh-old'),
        );
      final olderRefresh = controller.refreshAccessToken(transport);

      controller.establish(
        testSession(accessToken: 'access-newer', refreshToken: 'refresh-newer'),
      );
      final failure = expectLater(
        olderRefresh,
        throwsA(isA<AuthenticationException>()),
      );
      pendingRefresh.completeError(
        const AuthenticationException('Older refresh was rejected.'),
      );
      await failure;

      expect(controller.status, SessionStatus.authenticated);
      expect(controller.isAuthenticated, isTrue);
      expect(controller.lastError, isNull);
      expect(await controller.accessToken(transport), 'access-newer');
      expect(transport.refreshTokens, ['refresh-old']);
    });

    test(
      'an older refresh cannot replace an explicit reauthentication',
      () async {
        final pendingRefresh = Completer<SessionBundle>();
        final pendingLogin = Completer<SessionBundle>();
        final transport = _PendingRefreshTransport(
          pendingRefresh,
          pendingLogin: pendingLogin,
        );
        final controller = SessionController(now: () => testNow)
          ..establish(
            testSession(accessToken: 'access-old', refreshToken: 'refresh-old'),
          );

        final olderRefresh = controller.refreshAccessToken(transport);
        final newerLogin = controller.login(
          transport,
          username: 'admin',
          password: 'reauthenticate',
        );
        pendingRefresh.complete(
          testSession(
            accessToken: 'access-refreshed-old',
            refreshToken: 'refresh-refreshed-old',
          ),
        );
        await expectLater(
          olderRefresh,
          throwsA(isA<AuthenticationException>()),
        );

        pendingLogin.complete(
          testSession(
            accessToken: 'access-reauthenticated',
            refreshToken: 'refresh-reauthenticated',
          ),
        );
        expect(await newerLogin, isTrue);
        expect(controller.status, SessionStatus.authenticated);
        expect(
          await controller.accessToken(transport),
          'access-reauthenticated',
        );
      },
    );

    test('a refresh cannot start after reauthentication begins', () async {
      final pendingRefresh = Completer<SessionBundle>();
      final pendingLogin = Completer<SessionBundle>();
      final transport = _PendingRefreshTransport(
        pendingRefresh,
        pendingLogin: pendingLogin,
      );
      final controller = SessionController(now: () => testNow)
        ..establish(
          testSession(accessToken: 'access-old', refreshToken: 'refresh-old'),
        );

      final reauthentication = controller.login(
        transport,
        username: 'admin',
        password: 'reauthenticate',
      );

      await expectLater(
        controller.refreshAccessToken(transport),
        throwsA(isA<AuthenticationException>()),
      );
      expect(transport.refreshTokens, isEmpty);
      pendingLogin.complete(
        testSession(
          accessToken: 'access-reauthenticated',
          refreshToken: 'refresh-reauthenticated',
        ),
      );
      expect(await reauthentication, isTrue);
      expect(await controller.accessToken(transport), 'access-reauthenticated');
    });

    final expiredRefreshOperations =
        <String, Future<String> Function(SessionController, SessionTransport)>{
          'access-token acquisition': (controller, transport) =>
              controller.accessToken(transport),
          'explicit token refresh': (controller, transport) =>
              controller.refreshAccessToken(transport),
        };
    for (final operation in expiredRefreshOperations.entries) {
      test(
        '${operation.key} expiry cancellation cannot erase a newer login',
        () async {
          var now = testNow;
          final transport = _GatedRefreshFailureTransport();
          final controller = SessionController(now: () => now)
            ..establish(
              testSession(
                accessToken: 'access-old',
                refreshToken: 'refresh-old',
                accessExpiresAt: testNow.add(const Duration(hours: 1)),
                refreshExpiresAt: testNow.add(const Duration(hours: 2)),
              ),
            );
          now = testNow.add(const Duration(hours: 3));

          final expired = operation.value(controller, transport);
          await transport.cancellationStarted.future;
          final login = controller.login(
            transport,
            username: 'admin',
            password: 'newer',
          );
          transport.loginResult.complete(
            testSession(
              identity: testIdentity(
                owner: 'owner-new',
                actor: 'actor-new',
                session: 'session-new',
              ),
              accessToken: 'access-new',
              refreshToken: 'refresh-new',
              accessExpiresAt: testNow.add(const Duration(hours: 4)),
            ),
          );
          expect(await login, isTrue);
          transport.cancellationCompletion.complete();

          await expectLater(expired, throwsA(isA<AuthenticationException>()));
          expect(controller.status, SessionStatus.authenticated);
          expect(controller.ownerId, 'owner-new');
          expect(await controller.accessToken(transport), 'access-new');
        },
      );
    }

    for (final failure in <Object>[
      const AuthenticationException('Refresh authentication failed.'),
      const ProtocolException('Refresh response was invalid.'),
    ]) {
      test(
        '${failure.runtimeType} refresh cancellation cannot erase a newer login',
        () async {
          final transport = _GatedRefreshFailureTransport();
          final controller = SessionController(now: () => testNow)
            ..establish(
              testSession(
                accessToken: 'access-old',
                refreshToken: 'refresh-old',
              ),
            );

          final refresh = controller.refreshAccessToken(transport);
          transport.refreshResult.completeError(failure);
          await transport.cancellationStarted.future;
          final login = controller.login(
            transport,
            username: 'admin',
            password: 'newer',
          );
          transport.loginResult.complete(
            testSession(
              identity: testIdentity(
                owner: 'owner-new',
                actor: 'actor-new',
                session: 'session-new',
              ),
              accessToken: 'access-new',
              refreshToken: 'refresh-new',
            ),
          );
          expect(await login, isTrue);
          transport.cancellationCompletion.complete();

          await expectLater(refresh, throwsA(same(failure)));
          expect(controller.status, SessionStatus.authenticated);
          expect(controller.ownerId, 'owner-new');
          expect(controller.lastError, isNull);
          expect(await controller.accessToken(transport), 'access-new');
        },
      );
    }
  });
}

class _ExternalLoginTransport implements ExternalSessionTransport {
  final List<String> identityTokens = [];

  @override
  Future<SessionBundle> loginExternal(String identityToken) async {
    identityTokens.add(identityToken);
    return testSession();
  }
}

class _PendingLoginTransport implements SessionTransport {
  final Map<String, Completer<SessionBundle>> pending = {};

  @override
  Future<SessionBundle> login({
    required String username,
    required String password,
  }) => (pending[password] ??= Completer<SessionBundle>()).future;

  @override
  Future<SessionBundle> refreshSession({required String refreshToken}) =>
      throw UnimplementedError();

  @override
  Future<void> logout({required String refreshToken}) async {}
}

class _PendingRefreshTransport implements SessionTransport {
  _PendingRefreshTransport(this.pendingRefresh, {this.pendingLogin});

  final Completer<SessionBundle> pendingRefresh;
  final Completer<SessionBundle>? pendingLogin;
  final List<String> refreshTokens = [];

  @override
  Future<SessionBundle> login({
    required String username,
    required String password,
  }) => pendingLogin?.future ?? (throw UnimplementedError());

  @override
  Future<SessionBundle> refreshSession({required String refreshToken}) {
    refreshTokens.add(refreshToken);
    return pendingRefresh.future;
  }

  @override
  Future<void> logout({required String refreshToken}) async {}
}

class _GatedSignOutTransport
    implements SessionTransport, SessionProductCallCancellation {
  final cancellation = Completer<void>();
  final logoutCompletion = Completer<void>();
  bool logoutStarted = false;

  @override
  Future<void> cancelProductCalls() => cancellation.future;

  @override
  Future<SessionBundle> login({
    required String username,
    required String password,
  }) => throw UnimplementedError();

  @override
  Future<void> logout({required String refreshToken}) {
    logoutStarted = true;
    return logoutCompletion.future;
  }

  @override
  Future<SessionBundle> refreshSession({required String refreshToken}) =>
      throw UnimplementedError();
}

class _RefreshDuringSignOutTransport implements SessionTransport {
  final refresh = Completer<SessionBundle>();
  final logoutCompletion = Completer<void>();
  final List<String> logoutTokens = [];

  @override
  Future<SessionBundle> login({
    required String username,
    required String password,
  }) => throw UnimplementedError();

  @override
  Future<void> logout({required String refreshToken}) {
    logoutTokens.add(refreshToken);
    return logoutCompletion.future;
  }

  @override
  Future<SessionBundle> refreshSession({required String refreshToken}) =>
      refresh.future;
}

class _GatedRefreshFailureTransport
    implements SessionTransport, SessionProductCallCancellation {
  final refreshResult = Completer<SessionBundle>();
  final loginResult = Completer<SessionBundle>();
  final cancellationStarted = Completer<void>();
  final cancellationCompletion = Completer<void>();

  @override
  Future<void> cancelProductCalls() {
    if (!cancellationStarted.isCompleted) cancellationStarted.complete();
    return cancellationCompletion.future;
  }

  @override
  Future<SessionBundle> login({
    required String username,
    required String password,
  }) => loginResult.future;

  @override
  Future<void> logout({required String refreshToken}) async {}

  @override
  Future<SessionBundle> refreshSession({required String refreshToken}) =>
      refreshResult.future;
}
