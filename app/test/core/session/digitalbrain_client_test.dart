import 'dart:async';

import 'package:digitalbrain_flutter/core/session/digitalbrain_client.dart';
import 'package:digitalbrain_flutter/grpc/ui.pb.dart' as wire;
import 'package:digitalbrain_flutter/runtime/protocol/surface_protocol.dart';
import 'package:digitalbrain_flutter/runtime/runtime.dart';
import 'package:digitalbrain_flutter/runtime/runtime_configuration.dart';
import 'package:digitalbrain_flutter/runtime/runtime_session_owner.dart';
import 'package:fixnum/fixnum.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test(
    'feature draft calls acquire the current token on the shared transport',
    () async {
      final session = SessionController(now: () => DateTime.utc(2026, 7, 15));
      session.establish(
        SessionBundle(
          identity: const SessionIdentity(
            sessionId: 'session-a',
            ownerId: 'owner-a',
            actorId: 'actor-a',
          ),
          credentials: SessionCredentials(
            accessToken: 'access-a',
            refreshToken: 'refresh-a',
            accessExpiresAt: DateTime.utc(2026, 7, 15, 1),
            refreshExpiresAt: DateTime.utc(2026, 7, 16),
          ),
        ),
      );
      final transport = _RecordingDigitalBrainTransport();
      final client = DigitalBrainClient(session: session, transport: transport);
      final request = wire.GetFeatureDraftRequest(draftId: 'draft-a');

      final reply = await client.getFeatureDraft(request);

      expect(reply, same(transport.draftReply));
      expect(transport.accessToken, 'access-a');
      expect(transport.getRequest, same(request));
    },
  );

  test(
    'unauthenticated product call refreshes and replays the same request once',
    () async {
      final session = _authenticatedSession();
      final transport = _RecordingDigitalBrainTransport();
      final request = wire.ReviseFeatureDraftRequest(
        draftId: 'draft-a',
        expectedRevision: Int64(4),
        idempotencyId: 'mutation-a',
      );
      var attempts = 0;
      transport.onRevise = (accessToken, sentRequest) async {
        attempts++;
        if (attempts == 1) throw const AuthenticationException();
        return transport.draftReply;
      };
      var authenticationRequiredCalls = 0;
      final client = DigitalBrainClient(
        session: session,
        transport: transport,
        onAuthenticationRequired: () async {
          authenticationRequiredCalls++;
        },
      );

      final reply = await client.reviseFeatureDraft(request);

      expect(reply, same(transport.draftReply));
      expect(transport.refreshTokens, ['refresh-a']);
      expect(transport.productAccessTokens, ['access-a', 'access-b']);
      expect(transport.reviseRequests, [same(request), same(request)]);
      expect(request.idempotencyId, 'mutation-a');
      expect(authenticationRequiredCalls, 0);
      expect(session.isAuthenticated, isTrue);
    },
  );

  test('concurrent unauthenticated product calls share one refresh', () async {
    final session = _authenticatedSession();
    final transport = _RecordingDigitalBrainTransport();
    final firstGet = Completer<wire.FeatureDraftReply>();
    final firstVerify = Completer<wire.FeatureReleaseReviewReply>();
    final refresh = Completer<SessionBundle>();
    transport.refreshResult = refresh.future;
    var getAttempts = 0;
    var verifyAttempts = 0;
    transport.onGet = (accessToken, request) {
      getAttempts++;
      return getAttempts == 1
          ? firstGet.future
          : Future.value(transport.draftReply);
    };
    transport.onVerify = (accessToken, request) {
      verifyAttempts++;
      return verifyAttempts == 1
          ? firstVerify.future
          : Future.value(transport.reviewReply);
    };
    final client = DigitalBrainClient(session: session, transport: transport);
    final getRequest = wire.GetFeatureDraftRequest(draftId: 'draft-a');
    final verifyRequest = wire.VerifyFeatureDraftRequest(
      draftId: 'draft-a',
      expectedRevision: Int64(4),
      idempotencyId: 'verification-a',
    );

    final get = client.getFeatureDraft(getRequest);
    final verify = client.verifyFeatureDraft(verifyRequest);
    await Future<void>.delayed(Duration.zero);
    firstGet.completeError(const AuthenticationException());
    firstVerify.completeError(const AuthenticationException());
    await Future<void>.delayed(Duration.zero);

    expect(transport.refreshTokens, ['refresh-a']);
    refresh.complete(_refreshedSession());
    await Future.wait([get, verify]);

    expect(transport.refreshTokens, ['refresh-a']);
    expect(transport.getRequests, [same(getRequest), same(getRequest)]);
    expect(transport.verifyRequests, [
      same(verifyRequest),
      same(verifyRequest),
    ]);
    expect(transport.productAccessTokens, [
      'access-a',
      'access-a',
      'access-b',
      'access-b',
    ]);
  });

  test(
    'sign out during rejected-token refresh blocks the retry send',
    () async {
      final session = _authenticatedSession();
      final transport = _RecordingDigitalBrainTransport();
      final firstAttempt = Completer<wire.FeatureDraftReply>();
      final refresh = Completer<SessionBundle>();
      final productCancellation = Completer<void>();
      transport
        ..refreshResult = refresh.future
        ..productCancellationGate = productCancellation;
      transport.onRevise = (accessToken, request) => firstAttempt.future;
      var authenticationRequiredCalls = 0;
      final client = DigitalBrainClient(
        session: session,
        transport: transport,
        onAuthenticationRequired: () async {
          authenticationRequiredCalls++;
        },
      );

      final product = client.reviseFeatureDraft(
        wire.ReviseFeatureDraftRequest(
          draftId: 'draft-a',
          expectedRevision: Int64(4),
          idempotencyId: 'mutation-a',
        ),
      );
      firstAttempt.completeError(const AuthenticationException());
      await _eventually(() => transport.refreshTokens.isNotEmpty);
      final signOut = session.signOut(transport);
      refresh.complete(_refreshedSession());
      final productExpectation = expectLater(
        product,
        throwsA(isA<AuthenticationException>()),
      );
      await Future<void>.delayed(Duration.zero);

      expect(transport.reviseRequests, hasLength(1));
      expect(authenticationRequiredCalls, 0);
      productCancellation.complete();
      await signOut;
      await productExpectation;
      expect(authenticationRequiredCalls, 0);
      expect(session.status, SessionStatus.signedOut);
    },
  );

  test(
    'late rejected-token refresh cannot reopen completed sign out',
    () async {
      final session = _authenticatedSession();
      final transport = _RecordingDigitalBrainTransport();
      final firstAttempt = Completer<wire.FeatureDraftReply>();
      final refresh = Completer<SessionBundle>();
      transport
        ..refreshResult = refresh.future
        ..onRevise = (accessToken, request) => firstAttempt.future;
      var authenticationRequiredCalls = 0;
      final client = DigitalBrainClient(
        session: session,
        transport: transport,
        onAuthenticationRequired: () async {
          authenticationRequiredCalls++;
        },
      );

      final product = client.reviseFeatureDraft(
        wire.ReviseFeatureDraftRequest(
          draftId: 'draft-a',
          expectedRevision: Int64(4),
          idempotencyId: 'mutation-a',
        ),
      );
      firstAttempt.completeError(const AuthenticationException());
      await _eventually(() => transport.refreshTokens.isNotEmpty);
      await session.signOut(transport);
      expect(session.status, SessionStatus.signedOut);
      refresh.complete(_refreshedSession());

      await expectLater(product, throwsA(isA<AuthenticationException>()));
      expect(transport.reviseRequests, hasLength(1));
      expect(authenticationRequiredCalls, 0);
      expect(session.status, SessionStatus.signedOut);
    },
  );

  test('late rejected-token refresh cannot expire a newer identity', () async {
    final session = _authenticatedSession();
    final transport = _RecordingDigitalBrainTransport();
    final firstAttempt = Completer<wire.FeatureDraftReply>();
    final refresh = Completer<SessionBundle>();
    transport
      ..refreshResult = refresh.future
      ..onRevise = (accessToken, request) => firstAttempt.future;
    var authenticationRequiredCalls = 0;
    final client = DigitalBrainClient(
      session: session,
      transport: transport,
      onAuthenticationRequired: () async {
        authenticationRequiredCalls++;
      },
    );

    final product = client.reviseFeatureDraft(
      wire.ReviseFeatureDraftRequest(
        draftId: 'draft-a',
        expectedRevision: Int64(4),
        idempotencyId: 'mutation-a',
      ),
    );
    firstAttempt.completeError(const AuthenticationException());
    await _eventually(() => transport.refreshTokens.isNotEmpty);
    await session.signOut(transport);
    session.establish(_newIdentitySession());
    refresh.complete(_refreshedSession());

    await expectLater(product, throwsA(isA<AuthenticationException>()));
    expect(transport.reviseRequests, hasLength(1));
    expect(authenticationRequiredCalls, 0);
    expect(session.status, SessionStatus.authenticated);
    expect(session.identity?.sessionId, 'session-new');
    expect(session.ownerId, 'owner-new');
  });

  test('late initial token refresh cannot expire a newer identity', () async {
    final session = _expiringAccessSession();
    final refresh = Completer<SessionBundle>();
    final transport = _RecordingDigitalBrainTransport()
      ..refreshResult = refresh.future;
    var authenticationRequiredCalls = 0;
    final client = DigitalBrainClient(
      session: session,
      transport: transport,
      onAuthenticationRequired: () async {
        authenticationRequiredCalls++;
      },
    );

    final product = client.getFeatureDraft(
      wire.GetFeatureDraftRequest(draftId: 'draft-a'),
    );
    await _eventually(() => transport.refreshTokens.isNotEmpty);
    await session.signOut(transport);
    session.establish(_newIdentitySession());
    refresh.complete(_refreshedSession());

    await expectLater(product, throwsA(isA<AuthenticationException>()));
    expect(transport.getRequests, isEmpty);
    expect(authenticationRequiredCalls, 0);
    expect(session.status, SessionStatus.authenticated);
    expect(session.identity?.sessionId, 'session-new');
    expect(session.ownerId, 'owner-new');
  });

  test(
    'late product authentication failure cannot cancel a newer login',
    () async {
      final session = _authenticatedSession();
      final firstAttempt = Completer<wire.FeatureDraftReply>();
      final loginResult = Completer<SessionBundle>();
      final transport = _RecordingDigitalBrainTransport()
        ..loginResult = loginResult.future
        ..onRevise = (accessToken, request) => firstAttempt.future;
      var authenticationRequiredCalls = 0;
      final client = DigitalBrainClient(
        session: session,
        transport: transport,
        onAuthenticationRequired: () async {
          authenticationRequiredCalls++;
        },
      );

      final product = client.reviseFeatureDraft(
        wire.ReviseFeatureDraftRequest(
          draftId: 'draft-a',
          expectedRevision: Int64(4),
          idempotencyId: 'mutation-a',
        ),
      );
      await _eventually(() => transport.reviseRequests.isNotEmpty);
      final login = session.login(
        transport,
        username: 'owner-new',
        password: 'correct horse battery staple',
      );
      expect(session.status, SessionStatus.authenticating);
      firstAttempt.completeError(const AuthenticationException());

      await expectLater(product, throwsA(isA<AuthenticationException>()));
      expect(authenticationRequiredCalls, 0);
      expect(session.status, SessionStatus.authenticating);
      loginResult.complete(_newIdentitySession());
      expect(await login, isTrue);
      expect(session.status, SessionStatus.authenticated);
      expect(session.identity?.sessionId, 'session-new');
      expect(session.ownerId, 'owner-new');
    },
  );

  test(
    'authentication cancellation cannot expire a login that finishes later',
    () async {
      final session = _authenticatedSession();
      final productCancellation = Completer<void>();
      final loginResult = Completer<SessionBundle>();
      final transport = _RecordingDigitalBrainTransport()
        ..productCancellationGate = productCancellation
        ..loginResult = loginResult.future
        ..onRevise = (accessToken, request) async {
          throw const AuthenticationException();
        };
      var authenticationRequiredCalls = 0;
      final client = DigitalBrainClient(
        session: session,
        transport: transport,
        onAuthenticationRequired: () async {
          authenticationRequiredCalls++;
        },
      );

      final product = client.reviseFeatureDraft(
        wire.ReviseFeatureDraftRequest(
          draftId: 'draft-a',
          expectedRevision: Int64(4),
          idempotencyId: 'mutation-a',
        ),
      );
      await _eventually(() => transport.productCancellationCalls > 0);
      final login = session.login(
        transport,
        username: 'owner-new',
        password: 'correct horse battery staple',
      );
      loginResult.complete(_newIdentitySession());
      expect(await login, isTrue);
      expect(session.status, SessionStatus.authenticated);
      productCancellation.complete();

      await expectLater(product, throwsA(isA<AuthenticationException>()));
      expect(authenticationRequiredCalls, 0);
      expect(session.status, SessionStatus.authenticated);
      expect(session.identity?.sessionId, 'session-new');
      expect(session.ownerId, 'owner-new');
    },
  );

  test('older challenge cannot finish a newer expiration generation', () async {
    final session = _authenticatedSession();
    final expirationA = Completer<void>();
    final expirationB = Completer<void>();
    final refreshBResult = Completer<SessionBundle>();
    final transport = _RecordingDigitalBrainTransport()
      ..productCancellationGates.addAll([expirationA, expirationB])
      ..loginResult = Future.value(_newIdentitySession())
      ..onRevise = (accessToken, request) async {
        throw const AuthenticationException();
      };
    var authenticationRequiredCalls = 0;
    final client = DigitalBrainClient(
      session: session,
      transport: transport,
      onAuthenticationRequired: () async {
        authenticationRequiredCalls++;
      },
    );

    final product = client.reviseFeatureDraft(
      wire.ReviseFeatureDraftRequest(
        draftId: 'draft-a',
        expectedRevision: Int64(4),
        idempotencyId: 'mutation-a',
      ),
    );
    await _eventually(() => transport.productCancellationCalls == 1);
    expect(
      await session.login(
        transport,
        username: 'owner-new',
        password: 'correct horse battery staple',
      ),
      isTrue,
    );
    transport.refreshResult = refreshBResult.future;
    final refreshB = session.refreshAccessToken(transport);
    refreshBResult.completeError(
      const AuthenticationException('New session refresh was rejected.'),
    );
    await _eventually(() => transport.productCancellationCalls == 2);

    expirationA.complete();
    await expectLater(product, throwsA(isA<AuthenticationException>()));
    final statusAfterA = session.status;
    final authenticationRequiredCallsAfterA = authenticationRequiredCalls;
    expirationB.complete();
    await expectLater(refreshB, throwsA(isA<AuthenticationException>()));

    expect(statusAfterA, SessionStatus.expiring);
    expect(authenticationRequiredCallsAfterA, 0);
    expect(authenticationRequiredCalls, 0);
    expect(session.status, SessionStatus.expired);
  });

  test(
    'non-authentication retry failure keeps the refreshed session',
    () async {
      final session = _authenticatedSession();
      final transport = _RecordingDigitalBrainTransport();
      final request = wire.ReviseFeatureDraftRequest(
        draftId: 'draft-a',
        expectedRevision: Int64(4),
        idempotencyId: 'mutation-a',
      );
      var attempts = 0;
      transport.onRevise = (accessToken, sentRequest) async {
        attempts++;
        if (attempts == 1) throw const AuthenticationException();
        throw const TransportException(
          TransportErrorCode.unavailable,
          'Feature service is temporarily unavailable.',
        );
      };
      var authenticationRequiredCalls = 0;
      final client = DigitalBrainClient(
        session: session,
        transport: transport,
        onAuthenticationRequired: () async {
          authenticationRequiredCalls++;
        },
      );

      await expectLater(
        client.reviseFeatureDraft(request),
        throwsA(
          isA<TransportException>().having(
            (error) => error.code,
            'code',
            TransportErrorCode.unavailable,
          ),
        ),
      );

      expect(transport.refreshTokens, ['refresh-a']);
      expect(transport.reviseRequests, [same(request), same(request)]);
      expect(authenticationRequiredCalls, 0);
      expect(session.isAuthenticated, isTrue);
      expect(session.status, SessionStatus.authenticated);
    },
  );

  test('refresh identity mismatch requires authentication', () async {
    final session = _authenticatedSession();
    final transport = _RecordingDigitalBrainTransport();
    transport.refreshResult = Future.value(
      SessionBundle(
        identity: const SessionIdentity(
          sessionId: 'session-b',
          ownerId: 'owner-b',
          actorId: 'actor-b',
        ),
        credentials: SessionCredentials(
          accessToken: 'access-b',
          refreshToken: 'refresh-b',
          accessExpiresAt: DateTime.utc(2026, 7, 15, 2),
          refreshExpiresAt: DateTime.utc(2026, 7, 16),
        ),
      ),
    );
    transport.onRevise = (accessToken, sentRequest) async {
      throw const AuthenticationException();
    };
    var authenticationRequiredCalls = 0;
    final client = DigitalBrainClient(
      session: session,
      transport: transport,
      onAuthenticationRequired: () async {
        authenticationRequiredCalls++;
      },
    );

    await expectLater(
      client.reviseFeatureDraft(
        wire.ReviseFeatureDraftRequest(
          draftId: 'draft-a',
          expectedRevision: Int64(4),
          idempotencyId: 'mutation-a',
        ),
      ),
      throwsA(isA<ProtocolException>()),
    );

    expect(session.status, SessionStatus.expired);
    expect(authenticationRequiredCalls, 1);
  });

  test(
    'initial token refresh identity mismatch requires authentication',
    () async {
      final session = _expiringAccessSession();
      final transport = _RecordingDigitalBrainTransport()
        ..refreshResult = Future.value(_newIdentitySession());
      var authenticationRequiredCalls = 0;
      final client = DigitalBrainClient(
        session: session,
        transport: transport,
        onAuthenticationRequired: () async {
          authenticationRequiredCalls++;
        },
      );

      await expectLater(
        client.getFeatureDraft(wire.GetFeatureDraftRequest(draftId: 'draft-a')),
        throwsA(isA<ProtocolException>()),
      );

      expect(transport.getRequests, isEmpty);
      expect(session.status, SessionStatus.expired);
      expect(authenticationRequiredCalls, 1);
    },
  );

  test(
    'runtime sign out fences product calls before cancellation settles',
    () async {
      final session = _authenticatedSession();
      final transport = _RecordingDigitalBrainTransport();
      final feedCancellation = Completer<void>();
      final productCancellation = Completer<void>();
      transport
        ..feedCall = _GatedFeedCall(feedCancellation)
        ..productCancellationGate = productCancellation;
      final runtime = RuntimeController(
        transport: transport,
        session: session,
        delay: (_) async {},
      );
      final client = DigitalBrainClient(session: session, transport: transport);
      await runtime.start();
      await _eventually(() => runtime.status == RuntimeStatus.streaming);

      final signOut = runtime.signOut();
      final product = client.getFeatureDraft(
        wire.GetFeatureDraftRequest(draftId: 'draft-after-sign-out'),
      );
      final productExpectation = expectLater(
        product,
        throwsA(isA<AuthenticationException>()),
      );
      productCancellation.complete();
      feedCancellation.complete();
      await signOut;

      await productExpectation;
      expect(transport.getRequests, isEmpty);
      expect(session.status, SessionStatus.signedOut);
      await runtime.stop();
    },
  );

  test(
    'sign out during token acquisition blocks the late product send',
    () async {
      final session = _authenticatedSession();
      final transport = _RecordingDigitalBrainTransport();
      final productCancellation = Completer<void>();
      transport.productCancellationGate = productCancellation;
      final client = DigitalBrainClient(session: session, transport: transport);

      final product = client.getFeatureDraft(
        wire.GetFeatureDraftRequest(draftId: 'draft-before-sign-out'),
      );
      final signOut = session.signOut(transport);
      final productExpectation = expectLater(
        product,
        throwsA(isA<AuthenticationException>()),
      );
      await Future<void>.delayed(Duration.zero);

      expect(transport.getRequests, isEmpty);
      productCancellation.complete();
      await signOut;
      await productExpectation;
      expect(session.status, SessionStatus.signedOut);
    },
  );

  test(
    'authentication challenge fences product calls before cancellation settles',
    () async {
      final session = _authenticatedSession();
      final transport = _RecordingDigitalBrainTransport();
      final feedCancellation = Completer<void>();
      final productCancellation = Completer<void>();
      transport
        ..feedCall = _GatedFeedCall(feedCancellation)
        ..productCancellationGate = productCancellation;
      final runtime = RuntimeController(
        transport: transport,
        session: session,
        delay: (_) async {},
      );
      final client = DigitalBrainClient(session: session, transport: transport);
      await runtime.start();
      await _eventually(() => runtime.status == RuntimeStatus.streaming);

      final challenge = runtime.requireAuthentication();
      final product = client.getFeatureDraft(
        wire.GetFeatureDraftRequest(draftId: 'draft-after-challenge'),
      );
      final productExpectation = expectLater(
        product,
        throwsA(isA<AuthenticationException>()),
      );
      productCancellation.complete();
      feedCancellation.complete();
      await challenge;

      await productExpectation;
      expect(transport.getRequests, isEmpty);
      expect(session.status, SessionStatus.expired);
      await runtime.stop();
    },
  );

  test('runtime owner exposes one app-lifetime DigitalBrain client', () async {
    final transport = _RecordingDigitalBrainTransport();
    final controller = RuntimeController(transport: transport);
    final owner = RuntimeSessionOwner(
      configuration: RuntimeConfiguration(
        endpoint: Uri.parse('https://localhost:7443'),
      ),
      controller: controller,
      transportFactory: (_) => transport,
      autoStart: false,
    );

    owner.initialize();
    final first = owner.digitalBrainClient;
    owner.initialize();

    expect(first, isNotNull);
    expect(owner.digitalBrainClient, same(first));
    await owner.close();
  });
}

class _RecordingDigitalBrainTransport
    implements
        DigitalBrainTransport,
        UiTransport,
        SessionProductCallCancellation {
  final draftReply = wire.FeatureDraftReply();
  final reviewReply = wire.FeatureReleaseReviewReply();
  String? accessToken;
  wire.GetFeatureDraftRequest? getRequest;
  final List<String> productAccessTokens = [];
  final List<String> refreshTokens = [];
  final List<wire.GetFeatureDraftRequest> getRequests = [];
  final List<wire.ReviseFeatureDraftRequest> reviseRequests = [];
  final List<wire.VerifyFeatureDraftRequest> verifyRequests = [];
  Future<SessionBundle>? refreshResult;
  Future<SessionBundle>? loginResult;
  FeedCall? feedCall;
  Completer<void>? productCancellationGate;
  final List<Completer<void>> productCancellationGates = [];
  int productCancellationCalls = 0;
  Future<wire.FeatureDraftReply> Function(
    String accessToken,
    wire.GetFeatureDraftRequest request,
  )?
  onGet;
  Future<wire.FeatureDraftReply> Function(
    String accessToken,
    wire.ReviseFeatureDraftRequest request,
  )?
  onRevise;
  Future<wire.FeatureReleaseReviewReply> Function(
    String accessToken,
    wire.VerifyFeatureDraftRequest request,
  )?
  onVerify;

  @override
  Future<wire.FeatureDraftReply> getFeatureDraft({
    required String accessToken,
    required wire.GetFeatureDraftRequest request,
  }) async {
    this.accessToken = accessToken;
    getRequest = request;
    productAccessTokens.add(accessToken);
    getRequests.add(request);
    final callback = onGet;
    if (callback != null) return callback(accessToken, request);
    return draftReply;
  }

  @override
  Future<wire.FeatureDraftReply> reviseFeatureDraft({
    required String accessToken,
    required wire.ReviseFeatureDraftRequest request,
  }) async {
    productAccessTokens.add(accessToken);
    reviseRequests.add(request);
    final callback = onRevise;
    if (callback != null) return callback(accessToken, request);
    return draftReply;
  }

  @override
  Future<wire.FeatureDraftPatchReply> suggestFeatureChange({
    required String accessToken,
    required wire.SuggestFeatureChangeRequest request,
  }) async => wire.FeatureDraftPatchReply();

  @override
  Future<wire.FeatureReleaseReviewReply> verifyFeatureDraft({
    required String accessToken,
    required wire.VerifyFeatureDraftRequest request,
  }) async {
    productAccessTokens.add(accessToken);
    verifyRequests.add(request);
    final callback = onVerify;
    if (callback != null) return callback(accessToken, request);
    return reviewReply;
  }

  @override
  Future<SessionBundle> login({
    required String username,
    required String password,
  }) => loginResult ?? (throw UnimplementedError());

  @override
  Future<void> logout({required String refreshToken}) async {}

  @override
  Future<SessionBundle> refreshSession({required String refreshToken}) {
    refreshTokens.add(refreshToken);
    return refreshResult ?? Future.value(_refreshedSession());
  }

  @override
  Future<void> acknowledgeSurfaceFeed({
    required String accessToken,
    required FeedAudience audience,
    required int sequence,
  }) async {}

  @override
  Future<void> cancelProductCalls() {
    productCancellationCalls++;
    if (productCancellationGates.isNotEmpty) {
      return productCancellationGates.removeAt(0).future;
    }
    return productCancellationGate?.future ?? Future<void>.value();
  }

  @override
  Future<void> close() async {}

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
  Future<FeedCall> watchSurfaceFeed({
    required String accessToken,
    required int afterSequence,
    required FeedAudience audience,
    required Set<String> clientCapabilities,
    required int maxBatchSize,
  }) async => feedCall ?? _EmptyFeedCall();
}

SessionController _authenticatedSession() {
  final session = SessionController(now: () => DateTime.utc(2026, 7, 15));
  session.establish(
    SessionBundle(
      identity: const SessionIdentity(
        sessionId: 'session-a',
        ownerId: 'owner-a',
        actorId: 'actor-a',
      ),
      credentials: SessionCredentials(
        accessToken: 'access-a',
        refreshToken: 'refresh-a',
        accessExpiresAt: DateTime.utc(2026, 7, 15, 1),
        refreshExpiresAt: DateTime.utc(2026, 7, 16),
      ),
    ),
  );
  return session;
}

SessionController _expiringAccessSession() {
  final session = SessionController(now: () => DateTime.utc(2026, 7, 15));
  session.establish(
    SessionBundle(
      identity: const SessionIdentity(
        sessionId: 'session-a',
        ownerId: 'owner-a',
        actorId: 'actor-a',
      ),
      credentials: SessionCredentials(
        accessToken: 'access-a',
        refreshToken: 'refresh-a',
        accessExpiresAt: DateTime.utc(2026, 7, 15, 0, 0, 10),
        refreshExpiresAt: DateTime.utc(2026, 7, 16),
      ),
    ),
  );
  return session;
}

SessionBundle _refreshedSession() => SessionBundle(
  identity: const SessionIdentity(
    sessionId: 'session-a',
    ownerId: 'owner-a',
    actorId: 'actor-a',
  ),
  credentials: SessionCredentials(
    accessToken: 'access-b',
    refreshToken: 'refresh-b',
    accessExpiresAt: DateTime.utc(2026, 7, 15, 2),
    refreshExpiresAt: DateTime.utc(2026, 7, 16),
  ),
);

SessionBundle _newIdentitySession() => SessionBundle(
  identity: const SessionIdentity(
    sessionId: 'session-new',
    ownerId: 'owner-new',
    actorId: 'actor-new',
  ),
  credentials: SessionCredentials(
    accessToken: 'access-new',
    refreshToken: 'refresh-new',
    accessExpiresAt: DateTime.utc(2026, 7, 15, 3),
    refreshExpiresAt: DateTime.utc(2026, 7, 16),
  ),
);

class _EmptyFeedCall implements FeedCall {
  @override
  Future<void> cancel() async {}

  @override
  Stream<FeedEvent> get events => const Stream.empty();
}

class _GatedFeedCall implements FeedCall {
  _GatedFeedCall(this.cancellation);

  final Completer<void> cancellation;
  final StreamController<FeedEvent> _events = StreamController<FeedEvent>();

  @override
  Stream<FeedEvent> get events => _events.stream;

  @override
  Future<void> cancel() async {
    if (!_events.isClosed) await _events.close();
    await cancellation.future;
  }
}

Future<void> _eventually(bool Function() condition) async {
  for (var attempt = 0; attempt < 100; attempt++) {
    if (condition()) return;
    await Future<void>.delayed(const Duration(milliseconds: 1));
  }
  fail('Condition was not reached.');
}
