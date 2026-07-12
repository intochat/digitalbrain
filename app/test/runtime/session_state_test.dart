import 'dart:async';

import 'package:digitalbrain_flutter/runtime/runtime.dart';
import 'package:flutter_test/flutter_test.dart';

import 'test_fixtures.dart';

void main() {
  group('SessionController bootstrap', () {
    test('an older bootstrap failure cannot clear a newer session', () async {
      final transport = _PendingBootstrapTransport();
      final controller = SessionController(now: () => testNow);
      final older = controller.bootstrap(transport, 'older');
      final newer = controller.bootstrap(transport, 'newer');

      transport.pending['newer']!.complete(
        testSession(accessToken: 'access-newer', refreshToken: 'refresh-newer'),
      );
      expect(await newer, isTrue);
      transport.pending['older']!.completeError(
        const AuthenticationException('Older bootstrap was rejected.'),
      );
      expect(await older, isFalse);

      expect(controller.status, SessionStatus.authenticated);
      expect(controller.lastError, isNull);
      expect(await controller.accessToken(transport), 'access-newer');
    });

    test('an older bootstrap success cannot replace a newer session', () async {
      final transport = _PendingBootstrapTransport();
      final controller = SessionController(now: () => testNow);
      final older = controller.bootstrap(transport, 'older');
      final newer = controller.bootstrap(transport, 'newer');

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
        final pendingBootstrap = Completer<SessionBundle>();
        final transport = _PendingRefreshTransport(
          pendingRefresh,
          pendingBootstrap: pendingBootstrap,
        );
        final controller = SessionController(now: () => testNow)
          ..establish(
            testSession(accessToken: 'access-old', refreshToken: 'refresh-old'),
          );

        final olderRefresh = controller.refreshAccessToken(transport);
        final newerBootstrap = controller.bootstrap(
          transport,
          'reauthenticate',
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

        pendingBootstrap.complete(
          testSession(
            accessToken: 'access-reauthenticated',
            refreshToken: 'refresh-reauthenticated',
          ),
        );
        expect(await newerBootstrap, isTrue);
        expect(controller.status, SessionStatus.authenticated);
        expect(
          await controller.accessToken(transport),
          'access-reauthenticated',
        );
      },
    );

    test('a refresh cannot start after reauthentication begins', () async {
      final pendingRefresh = Completer<SessionBundle>();
      final pendingBootstrap = Completer<SessionBundle>();
      final transport = _PendingRefreshTransport(
        pendingRefresh,
        pendingBootstrap: pendingBootstrap,
      );
      final controller = SessionController(now: () => testNow)
        ..establish(
          testSession(accessToken: 'access-old', refreshToken: 'refresh-old'),
        );

      final reauthentication = controller.bootstrap(
        transport,
        'reauthenticate',
      );

      await expectLater(
        controller.refreshAccessToken(transport),
        throwsA(isA<AuthenticationException>()),
      );
      expect(transport.refreshTokens, isEmpty);
      pendingBootstrap.complete(
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

class _PendingBootstrapTransport implements SessionTransport {
  final Map<String, Completer<SessionBundle>> pending = {};

  @override
  Future<SessionBundle> bootstrapSession(String bootstrapSecret) =>
      (pending[bootstrapSecret] ??= Completer<SessionBundle>()).future;

  @override
  Future<SessionBundle> refreshSession({required String refreshToken}) =>
      throw UnimplementedError();
}

class _PendingRefreshTransport implements SessionTransport {
  _PendingRefreshTransport(this.pendingRefresh, {this.pendingBootstrap});

  final Completer<SessionBundle> pendingRefresh;
  final Completer<SessionBundle>? pendingBootstrap;
  final List<String> refreshTokens = [];

  @override
  Future<SessionBundle> bootstrapSession(String bootstrapSecret) =>
      pendingBootstrap?.future ?? (throw UnimplementedError());

  @override
  Future<SessionBundle> refreshSession({required String refreshToken}) {
    refreshTokens.add(refreshToken);
    return pendingRefresh.future;
  }
}
