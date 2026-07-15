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

      pendingRefresh.complete(
        testSession(accessToken: 'access-new', refreshToken: 'refresh-new'),
      );

      expect(await Future.wait([first, second]), ['access-new', 'access-new']);
      expect(transport.refreshTokens, ['refresh-old']);
      expect(controller.status, SessionStatus.authenticated);
      expect(controller.lastError, isNull);
    });

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
