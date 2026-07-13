import 'package:digitalbrain_flutter/runtime/runtime.dart';
import 'package:flutter_test/flutter_test.dart';

import 'runtime/test_fixtures.dart';

void main() {
  group('SessionController', () {
    test(
      'bootstraps an in-memory signed session and clears it on sign out',
      () async {
        final transport = _SessionTransport(testSession());
        final session = SessionController(now: () => testNow);

        await session.bootstrap(transport, 'local-bootstrap');

        expect(transport.bootstrapSecret, 'local-bootstrap');
        expect(session.status, SessionStatus.authenticated);
        expect(session.tenantId, 'tenant-a');
        expect(session.workspaceId, 'workspace-a');
        expect(session.principalId, 'principal-a');
        expect(
          testSession().credentials.toString(),
          isNot(anyOf(contains('access-token'), contains('refresh-token'))),
        );

        await session.signOut(transport);
        expect(transport.logoutRefreshToken, 'refresh-token');
        expect(session.status, SessionStatus.signedOut);
        expect(session.sessionId, isNull);
        expect(session.tenantId, isNull);
        expect(session.workspaceId, isNull);
        expect(session.principalId, isNull);
      },
    );

    test('server logout failure retains the authenticated session', () async {
      final transport = _SessionTransport(
        testSession(),
        logoutError: const TransportException(
          TransportErrorCode.unavailable,
          'Logout unavailable.',
        ),
      );
      final session = SessionController(now: () => testNow)
        ..establish(testSession());

      await expectLater(
        session.signOut(transport),
        throwsA(isA<TransportException>()),
      );

      expect(transport.logoutRefreshToken, 'refresh-token');
      expect(session.status, SessionStatus.authenticated);
      expect(session.isAuthenticated, isTrue);
      expect(session.sessionId, 'session-a');
      expect(session.tenantId, 'tenant-a');
      expect(session.workspaceId, 'workspace-a');
      expect(session.principalId, 'principal-a');
      expect(session.lastError, isA<TransportException>());
    });

    test(
      'rotates an expiring access session with the opaque refresh token',
      () async {
        final first = testSession(
          accessToken: 'access-old',
          refreshToken: 'refresh-old',
          accessExpiresAt: testNow.add(const Duration(seconds: 5)),
        );
        final refreshed = testSession(
          accessToken: 'access-new',
          refreshToken: 'refresh-new',
        );
        final transport = _SessionTransport(first, refreshed: refreshed);
        final session = SessionController(now: () => testNow);
        session.establish(first);

        final access = await session.accessToken(transport);

        expect(access, 'access-new');
        expect(transport.refreshToken, 'refresh-old');
        expect(session.status, SessionStatus.authenticated);
      },
    );

    test('fails closed when refresh changes workspace identity', () async {
      final first = testSession(
        accessExpiresAt: testNow.add(const Duration(seconds: 5)),
      );
      final changed = testSession(
        identity: testIdentity(workspace: 'workspace-b'),
      );
      final transport = _SessionTransport(first, refreshed: changed);
      final session = SessionController(now: () => testNow)..establish(first);

      await expectLater(
        session.accessToken(transport),
        throwsA(isA<ProtocolException>()),
      );
      expect(session.status, SessionStatus.expired);
      expect(session.workspaceId, isNull);
    });
  });

  group('FeedController', () {
    test('accepts first surface, ignores duplicate, and detects a gap', () {
      final feed = FeedController()..bindIdentity(testIdentity());
      final first = testSurface(sequence: 1);

      expect(feed.accept(first), isA<FeedSurface>());
      expect(feed.accept(first), isA<FeedDuplicate>());
      expect(feed.lastSequence, 1);
      expect(feed.surfaces, [first]);

      final gap = feed.accept(testSurface(sequence: 3, revision: 2));
      expect(gap, isA<FeedReset>());
      expect(feed.needsReset, isTrue);
      expect(feed.lastSequence, 1);
      expect(feed.surfaces, [first]);
    });

    test('rejects an already-expired surface without mutating feed state', () {
      final feed = FeedController(now: () => testNow)
        ..bindIdentity(testIdentity());
      final expired = testSurface(
        expiresAt: testNow.subtract(const Duration(seconds: 1)),
      );

      expect(() => feed.accept(expired), throwsA(isA<ProtocolException>()));
      expect(feed.lastSequence, 0);
      expect(feed.surfaces, isEmpty);
    });

    test(
      'atomically replaces state from reset snapshot and resumes baseline',
      () {
        final feed = FeedController()..bindIdentity(testIdentity());
        feed.accept(testSurface(sequence: 1));
        final reset = FeedResetEvent(
          reason: 'retention-gap',
          resumeSequence: 10,
          snapshotJson: const [],
        );
        final snapshotA = testSurface(
          sequence: 4,
          revision: 2,
          surfaceId: 'surface-a',
        );
        final snapshotB = testSurface(
          sequence: 9,
          revision: 3,
          surfaceId: 'surface-b',
        );

        feed.applyServerReset(reset, [snapshotA, snapshotB]);

        expect(feed.lastSequence, 10);
        expect(feed.needsReset, isFalse);
        expect(feed.surfaces, [snapshotA, snapshotB]);
        expect(
          feed.accept(
            testSurface(sequence: 11, revision: 3, surfaceId: 'surface-a'),
          ),
          isA<FeedSurface>(),
        );
      },
    );

    test('rejects wrong tenant, workspace, and principal without mutation', () {
      final feed = FeedController()..bindIdentity(testIdentity());

      for (final surface in [
        testSurface(tenant: 'tenant-b'),
        testSurface(workspace: 'workspace-b'),
        testSurface(audienceId: 'principal-b'),
      ]) {
        expect(() => feed.accept(surface), throwsA(isA<ScopeViolation>()));
      }

      expect(feed.lastSequence, 0);
      expect(feed.surfaces, isEmpty);
    });

    test('two workspace feeds cannot cross-deliver', () {
      final workspaceA = FeedController()
        ..bindIdentity(testIdentity(workspace: 'workspace-a'));
      final workspaceB = FeedController()
        ..bindIdentity(testIdentity(workspace: 'workspace-b'));
      final surfaceA = testSurface(workspace: 'workspace-a');
      final surfaceB = testSurface(workspace: 'workspace-b');

      expect(workspaceA.accept(surfaceA), isA<FeedSurface>());
      expect(workspaceB.accept(surfaceB), isA<FeedSurface>());
      expect(() => workspaceA.accept(surfaceB), throwsA(isA<ScopeViolation>()));
      expect(() => workspaceB.accept(surfaceA), throwsA(isA<ScopeViolation>()));
    });

    test('invalid reset snapshot leaves the current feed untouched', () {
      final original = testSurface(sequence: 1);
      final feed = FeedController()..bindIdentity(testIdentity());
      feed.accept(original);

      expect(
        () => feed.applyServerReset(
          const FeedResetEvent(reason: 'reset', resumeSequence: 3),
          [testSurface(sequence: 2, workspace: 'workspace-b')],
        ),
        throwsA(isA<ScopeViolation>()),
      );

      expect(feed.lastSequence, 1);
      expect(feed.surfaces, [original]);
    });
  });
}

class _SessionTransport implements SessionTransport {
  _SessionTransport(this.initial, {SessionBundle? refreshed, this.logoutError})
    : refreshed = refreshed ?? initial;

  final SessionBundle initial;
  final SessionBundle refreshed;
  final Object? logoutError;
  String? bootstrapSecret;
  String? refreshToken;
  String? logoutRefreshToken;

  @override
  Future<SessionBundle> bootstrapSession(String bootstrapSecret) async {
    this.bootstrapSecret = bootstrapSecret;
    return initial;
  }

  @override
  Future<SessionBundle> refreshSession({required String refreshToken}) async {
    this.refreshToken = refreshToken;
    return refreshed;
  }

  @override
  Future<void> logout({required String refreshToken}) async {
    logoutRefreshToken = refreshToken;
    final error = logoutError;
    if (error != null) throw error;
  }
}
