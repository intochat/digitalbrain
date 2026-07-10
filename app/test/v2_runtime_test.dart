import 'package:digitalbrain_flutter/v2/v2_runtime.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2/v2_test_fixtures.dart';

void main() {
  group('V2SessionController', () {
    test(
      'bootstraps an in-memory signed session and clears it on sign out',
      () async {
        final transport = _SessionTransport(testSession());
        final session = V2SessionController(now: () => v2TestNow);

        await session.bootstrap(transport, 'local-bootstrap');

        expect(transport.bootstrapSecret, 'local-bootstrap');
        expect(session.status, V2SessionStatus.authenticated);
        expect(session.tenantId, 'tenant-a');
        expect(session.workspaceId, 'workspace-a');
        expect(session.principalId, 'principal-a');
        expect(
          testSession().credentials.toString(),
          isNot(anyOf(contains('access-token'), contains('refresh-token'))),
        );

        session.signOut();
        expect(session.status, V2SessionStatus.signedOut);
        expect(session.sessionId, isNull);
        expect(session.tenantId, isNull);
        expect(session.workspaceId, isNull);
        expect(session.principalId, isNull);
      },
    );

    test(
      'rotates an expiring access session with the opaque refresh token',
      () async {
        final first = testSession(
          accessToken: 'access-old',
          refreshToken: 'refresh-old',
          accessExpiresAt: v2TestNow.add(const Duration(seconds: 5)),
        );
        final refreshed = testSession(
          accessToken: 'access-new',
          refreshToken: 'refresh-new',
        );
        final transport = _SessionTransport(first, refreshed: refreshed);
        final session = V2SessionController(now: () => v2TestNow);
        session.establish(first);

        final access = await session.accessToken(transport);

        expect(access, 'access-new');
        expect(transport.refreshToken, 'refresh-old');
        expect(session.status, V2SessionStatus.authenticated);
      },
    );

    test('fails closed when refresh changes workspace identity', () async {
      final first = testSession(
        accessExpiresAt: v2TestNow.add(const Duration(seconds: 5)),
      );
      final changed = testSession(
        identity: testIdentity(workspace: 'workspace-b'),
      );
      final transport = _SessionTransport(first, refreshed: changed);
      final session = V2SessionController(now: () => v2TestNow)
        ..establish(first);

      await expectLater(
        session.accessToken(transport),
        throwsA(isA<V2ProtocolException>()),
      );
      expect(session.status, V2SessionStatus.expired);
      expect(session.workspaceId, isNull);
    });
  });

  group('V2FeedController', () {
    test('accepts first surface, ignores duplicate, and detects a gap', () {
      final feed = V2FeedController()..bindIdentity(testIdentity());
      final first = testSurface(sequence: 1);

      expect(feed.accept(first), isA<V2FeedSurface>());
      expect(feed.accept(first), isA<V2FeedDuplicate>());
      expect(feed.lastSequence, 1);
      expect(feed.surfaces, [first]);

      final gap = feed.accept(testSurface(sequence: 3, revision: 2));
      expect(gap, isA<V2FeedReset>());
      expect(feed.needsReset, isTrue);
      expect(feed.lastSequence, 1);
      expect(feed.surfaces, [first]);
    });

    test('rejects an already-expired surface without mutating feed state', () {
      final feed = V2FeedController(now: () => v2TestNow)
        ..bindIdentity(testIdentity());
      final expired = testSurface(
        expiresAt: v2TestNow.subtract(const Duration(seconds: 1)),
      );

      expect(() => feed.accept(expired), throwsA(isA<V2ProtocolException>()));
      expect(feed.lastSequence, 0);
      expect(feed.surfaces, isEmpty);
    });

    test(
      'atomically replaces state from reset snapshot and resumes baseline',
      () {
        final feed = V2FeedController()..bindIdentity(testIdentity());
        feed.accept(testSurface(sequence: 1));
        final reset = V2FeedResetEvent(
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
          isA<V2FeedSurface>(),
        );
      },
    );

    test('rejects wrong tenant, workspace, and principal without mutation', () {
      final feed = V2FeedController()..bindIdentity(testIdentity());

      for (final surface in [
        testSurface(tenant: 'tenant-b'),
        testSurface(workspace: 'workspace-b'),
        testSurface(audienceId: 'principal-b'),
      ]) {
        expect(() => feed.accept(surface), throwsA(isA<V2ScopeViolation>()));
      }

      expect(feed.lastSequence, 0);
      expect(feed.surfaces, isEmpty);
    });

    test('two workspace feeds cannot cross-deliver', () {
      final workspaceA = V2FeedController()
        ..bindIdentity(testIdentity(workspace: 'workspace-a'));
      final workspaceB = V2FeedController()
        ..bindIdentity(testIdentity(workspace: 'workspace-b'));
      final surfaceA = testSurface(workspace: 'workspace-a');
      final surfaceB = testSurface(workspace: 'workspace-b');

      expect(workspaceA.accept(surfaceA), isA<V2FeedSurface>());
      expect(workspaceB.accept(surfaceB), isA<V2FeedSurface>());
      expect(
        () => workspaceA.accept(surfaceB),
        throwsA(isA<V2ScopeViolation>()),
      );
      expect(
        () => workspaceB.accept(surfaceA),
        throwsA(isA<V2ScopeViolation>()),
      );
    });

    test('invalid reset snapshot leaves the current feed untouched', () {
      final original = testSurface(sequence: 1);
      final feed = V2FeedController()..bindIdentity(testIdentity());
      feed.accept(original);

      expect(
        () => feed.applyServerReset(
          const V2FeedResetEvent(reason: 'reset', resumeSequence: 3),
          [testSurface(sequence: 2, workspace: 'workspace-b')],
        ),
        throwsA(isA<V2ScopeViolation>()),
      );

      expect(feed.lastSequence, 1);
      expect(feed.surfaces, [original]);
    });
  });
}

class _SessionTransport implements V2SessionTransport {
  _SessionTransport(this.initial, {V2SessionBundle? refreshed})
    : refreshed = refreshed ?? initial;

  final V2SessionBundle initial;
  final V2SessionBundle refreshed;
  String? bootstrapSecret;
  String? refreshToken;

  @override
  Future<V2SessionBundle> bootstrapSession(String bootstrapSecret) async {
    this.bootstrapSecret = bootstrapSecret;
    return initial;
  }

  @override
  Future<V2SessionBundle> refreshSession({required String refreshToken}) async {
    this.refreshToken = refreshToken;
    return refreshed;
  }
}
