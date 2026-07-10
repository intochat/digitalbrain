import 'dart:async';

import 'package:flutter/foundation.dart';

import 'protocol/surface_protocol.dart';

enum V2SessionStatus {
  signedOut,
  authenticating,
  authenticated,
  refreshing,
  expired,
}

/// Server-derived V2 identity. None of these values are accepted from a
/// Flutter request body; they arrive only in a successful signed-session
/// response and are used here as a client-side isolation check.
class V2SessionIdentity {
  const V2SessionIdentity({
    required this.sessionId,
    required this.tenantId,
    required this.workspaceId,
    required this.principalId,
  });

  final String sessionId;
  final String tenantId;
  final String workspaceId;
  final String principalId;

  @override
  String toString() => 'V2SessionIdentity([private])';
}

/// Opaque, in-memory-only credentials returned by the V2 session service.
///
/// Deliberately has a redacted [toString] so an exception, debugger message,
/// or test failure cannot accidentally print either token.
class V2SessionCredentials {
  const V2SessionCredentials({
    required this.accessToken,
    required this.refreshToken,
    required this.accessExpiresAt,
    required this.refreshExpiresAt,
  });

  final String accessToken;
  final String refreshToken;
  final DateTime accessExpiresAt;
  final DateTime refreshExpiresAt;

  @override
  String toString() => 'V2SessionCredentials([REDACTED])';
}

class V2SessionBundle {
  const V2SessionBundle({required this.identity, required this.credentials});

  final V2SessionIdentity identity;
  final V2SessionCredentials credentials;

  @override
  String toString() => 'V2SessionBundle([private])';
}

abstract interface class V2SessionTransport {
  Future<V2SessionBundle> bootstrapSession(String bootstrapSecret);

  Future<V2SessionBundle> refreshSession({required String refreshToken});
}

class V2SessionController {
  V2SessionController({DateTime Function()? now})
    : _now = now ?? (() => DateTime.now().toUtc());

  final DateTime Function() _now;
  V2SessionBundle? _bundle;

  V2SessionStatus status = V2SessionStatus.signedOut;
  Object? lastError;

  V2SessionIdentity? get identity => _bundle?.identity;
  String? get sessionId => identity?.sessionId;
  String? get tenantId => identity?.tenantId;
  String? get workspaceId => identity?.workspaceId;
  String? get principalId => identity?.principalId;
  bool get isAuthenticated =>
      status == V2SessionStatus.authenticated && _bundle != null;

  void begin() {
    lastError = null;
    status = V2SessionStatus.authenticating;
  }

  void establish(V2SessionBundle bundle) {
    _validate(bundle);
    _bundle = bundle;
    lastError = null;
    status = V2SessionStatus.authenticated;
  }

  Future<void> bootstrap(
    V2SessionTransport transport,
    String bootstrapSecret,
  ) async {
    if (bootstrapSecret.trim().isEmpty) {
      throw ArgumentError.value(
        bootstrapSecret,
        'bootstrapSecret',
        'A bootstrap secret is required.',
      );
    }
    begin();
    try {
      establish(await transport.bootstrapSession(bootstrapSecret));
    } catch (error) {
      _bundle = null;
      lastError = error;
      status = V2SessionStatus.signedOut;
      rethrow;
    }
  }

  /// Returns a non-expiring-soon access token, rotating the opaque refresh
  /// token first when necessary. The refresh token never leaves this method
  /// except as a typed transport argument.
  Future<String> accessToken(
    V2SessionTransport transport, {
    Duration refreshSkew = const Duration(seconds: 30),
  }) async {
    var bundle = _bundle;
    if (bundle == null) throw const V2AuthenticationException();
    final now = _now().toUtc();
    if (bundle.credentials.refreshExpiresAt.isBefore(now) ||
        bundle.credentials.refreshExpiresAt.isAtSameMomentAs(now)) {
      expire();
      throw const V2AuthenticationException('V2 session refresh expired.');
    }
    if (bundle.credentials.accessExpiresAt.isAfter(now.add(refreshSkew))) {
      return bundle.credentials.accessToken;
    }

    return refreshAccessToken(transport);
  }

  /// Forces one-use refresh rotation after the server rejects an access
  /// session, even when the local expiry clock still considers it current.
  Future<String> refreshAccessToken(V2SessionTransport transport) async {
    var bundle = _bundle;
    if (bundle == null) throw const V2AuthenticationException();
    final now = _now().toUtc();
    if (!bundle.credentials.refreshExpiresAt.isAfter(now)) {
      expire();
      throw const V2AuthenticationException('V2 session refresh expired.');
    }

    status = V2SessionStatus.refreshing;
    try {
      final refreshed = await transport.refreshSession(
        refreshToken: bundle.credentials.refreshToken,
      );
      if (!_sameIdentity(bundle.identity, refreshed.identity)) {
        throw const V2ProtocolException(
          'Session refresh changed the authenticated identity.',
        );
      }
      establish(refreshed);
      bundle = refreshed;
      return bundle.credentials.accessToken;
    } catch (error) {
      lastError = error;
      expire();
      rethrow;
    }
  }

  void expire() {
    _bundle = null;
    status = V2SessionStatus.expired;
  }

  void signOut() {
    _bundle = null;
    lastError = null;
    status = V2SessionStatus.signedOut;
  }

  void _validate(V2SessionBundle bundle) {
    final identity = bundle.identity;
    final credentials = bundle.credentials;
    if (identity.sessionId.trim().isEmpty ||
        identity.tenantId.trim().isEmpty ||
        identity.workspaceId.trim().isEmpty ||
        identity.principalId.trim().isEmpty ||
        credentials.accessToken.trim().isEmpty ||
        credentials.refreshToken.trim().isEmpty) {
      throw const V2ProtocolException('Session response is incomplete.');
    }
    if (!credentials.accessExpiresAt.isAfter(_now().toUtc()) ||
        !credentials.refreshExpiresAt.isAfter(credentials.accessExpiresAt)) {
      throw const V2ProtocolException('Session response has invalid expiry.');
    }
  }

  static bool _sameIdentity(V2SessionIdentity left, V2SessionIdentity right) =>
      left.sessionId == right.sessionId &&
      left.tenantId == right.tenantId &&
      left.workspaceId == right.workspaceId &&
      left.principalId == right.principalId;
}

enum V2FeedAudience { principal, workspace, public }

sealed class V2FeedEvent {
  const V2FeedEvent();
}

class V2FeedSurfaceJson extends V2FeedEvent {
  const V2FeedSurfaceJson(this.surfaceJson);
  final String surfaceJson;
}

class V2FeedResetEvent extends V2FeedEvent {
  const V2FeedResetEvent({
    required this.reason,
    required this.resumeSequence,
    this.snapshotJson = const [],
  });
  final String reason;
  final int resumeSequence;
  final List<String> snapshotJson;
}

abstract interface class V2FeedCall {
  Stream<V2FeedEvent> get events;
  Future<void> cancel();
}

abstract interface class V2FeedTransport {
  Future<V2FeedCall> watchSurfaceFeed({
    required String accessToken,
    required int afterSequence,
    required V2FeedAudience audience,
    required Set<String> clientCapabilities,
    required int maxBatchSize,
  });

  Future<void> acknowledgeSurfaceFeed({
    required String accessToken,
    required V2FeedAudience audience,
    required int sequence,
  });
}

class V2ActionResult {
  const V2ActionResult({
    required this.operationId,
    required this.idempotencyKey,
  });

  final String operationId;
  final String idempotencyKey;

  @override
  String toString() => 'V2ActionResult(operation accepted)';
}

abstract interface class V2ActionTransport {
  Future<V2ActionResult> submitAction({
    required String accessToken,
    required UiActionRef action,
    required Map<String, Object?> input,
  });
}

abstract interface class V2UiTransport
    implements V2SessionTransport, V2FeedTransport, V2ActionTransport {
  Future<void> close();
}

sealed class V2FeedMessage {
  const V2FeedMessage();
}

class V2FeedSurface extends V2FeedMessage {
  const V2FeedSurface(this.envelope);
  final SurfaceEnvelope envelope;
}

class V2FeedDuplicate extends V2FeedMessage {
  const V2FeedDuplicate(this.reason);
  final String reason;
}

class V2FeedReset extends V2FeedMessage {
  const V2FeedReset(this.reason, {this.resumeSequence = 0});
  final String reason;
  final int resumeSequence;
}

class V2FeedController {
  V2FeedController({DateTime Function()? now})
    : _now = now ?? (() => DateTime.now().toUtc());

  final DateTime Function() _now;
  int lastSequence = 0;
  bool needsReset = false;
  V2SessionIdentity? _identity;
  final Map<String, SurfaceEnvelope> _surfaces = {};

  Iterable<SurfaceEnvelope> get surfaces {
    final result = _surfaces.values.toList()
      ..sort((left, right) => left.feedSequence.compareTo(right.feedSequence));
    return result;
  }

  SurfaceEnvelope? surface(String surfaceId) => _surfaces[surfaceId];

  void bindIdentity(V2SessionIdentity identity) {
    if (_identity case final current? when !_sameScope(current, identity)) {
      reset();
    }
    _identity = identity;
  }

  V2FeedMessage accept(SurfaceEnvelope envelope) {
    _demandScope(envelope);
    _demandFresh(envelope);
    if (envelope.feedSequence <= lastSequence) {
      return const V2FeedDuplicate('duplicate-sequence');
    }
    if (needsReset ||
        (lastSequence != 0 && envelope.feedSequence != lastSequence + 1)) {
      needsReset = true;
      return const V2FeedReset('sequence-gap');
    }

    lastSequence = envelope.feedSequence;
    final current = _surfaces[envelope.surfaceId];
    if (current != null && envelope.revision <= current.revision) {
      return const V2FeedDuplicate('stale-revision');
    }
    _surfaces[envelope.surfaceId] = envelope;
    return V2FeedSurface(envelope);
  }

  void applyServerReset(
    V2FeedResetEvent resetEvent,
    Iterable<SurfaceEnvelope> snapshots,
  ) {
    if (resetEvent.resumeSequence < 0) {
      throw const V2ProtocolException(
        'Feed reset sequence cannot be negative.',
      );
    }
    final replacement = <String, SurfaceEnvelope>{};
    for (final envelope in snapshots) {
      _demandScope(envelope);
      _demandFresh(envelope);
      if (envelope.feedSequence > resetEvent.resumeSequence) {
        throw const V2ProtocolException(
          'Feed reset snapshot is newer than its resume sequence.',
        );
      }
      if (replacement.containsKey(envelope.surfaceId)) {
        throw const V2ProtocolException(
          'Feed reset snapshot contains a duplicate surface.',
        );
      }
      replacement[envelope.surfaceId] = envelope;
    }
    _surfaces
      ..clear()
      ..addAll(replacement);
    lastSequence = resetEvent.resumeSequence;
    needsReset = false;
  }

  void reset() {
    lastSequence = 0;
    needsReset = false;
    _surfaces.clear();
  }

  void acknowledge(int sequence) {
    if (sequence <= 0 || sequence > lastSequence) {
      throw StateError('Cannot acknowledge an unseen feed sequence.');
    }
  }

  void _demandScope(SurfaceEnvelope envelope) {
    final identity = _identity;
    if (identity == null) {
      throw const V2ScopeViolation('Feed identity is not established.');
    }
    if (envelope.tenantId != identity.tenantId ||
        envelope.workspaceId != identity.workspaceId) {
      throw const V2ScopeViolation('Surface is outside the signed workspace.');
    }
    final audience = envelope.audience;
    final validAudience = switch (audience.kind) {
      'principal' => audience.id == identity.principalId,
      'workspace' => audience.id == identity.workspaceId,
      'public' => audience.id.isEmpty,
      _ => false,
    };
    if (!validAudience) {
      throw const V2ScopeViolation('Surface audience does not match session.');
    }
  }

  void _demandFresh(SurfaceEnvelope envelope) {
    if (envelope.isExpired(_now().toUtc())) {
      throw const V2ProtocolException('V2 surface is expired.');
    }
  }

  static bool _sameScope(V2SessionIdentity left, V2SessionIdentity right) =>
      left.tenantId == right.tenantId &&
      left.workspaceId == right.workspaceId &&
      left.principalId == right.principalId;
}

enum V2RuntimeStatus {
  idle,
  authenticating,
  awaitingSignIn,
  connecting,
  streaming,
  reconnecting,
  stopped,
  terminalError,
}

class V2ReconnectPolicy {
  const V2ReconnectPolicy({
    this.delays = const [
      Duration(milliseconds: 250),
      Duration(seconds: 1),
      Duration(seconds: 2),
      Duration(seconds: 5),
    ],
    this.maxAttempts,
  });

  final List<Duration> delays;
  final int? maxAttempts;

  Duration delayFor(int attempt) {
    if (delays.isEmpty) return Duration.zero;
    return delays[attempt.clamp(0, delays.length - 1).toInt()];
  }
}

typedef V2Delay = Future<void> Function(Duration duration);

/// Owns V2 authentication, feed resume/reset/reconnect, acknowledgement, and
/// action submission. Stream completion is handled in one async control path,
/// so a later onDone can never overwrite the error that ended the stream.
class V2RuntimeController extends ChangeNotifier {
  V2RuntimeController({
    required this.transport,
    V2SessionController? session,
    V2FeedController? feed,
    SurfaceEnvelopeDecoder? decoder,
    this.capabilities = const V2ClientCapabilities(supportsBinaryRfw: false),
    this.audience = V2FeedAudience.principal,
    this.maxBatchSize = 50,
    this.reconnectPolicy = const V2ReconnectPolicy(),
    V2Delay? delay,
  }) : session = session ?? V2SessionController(),
       feed = feed ?? V2FeedController(),
       decoder =
           decoder ??
           SurfaceEnvelopeDecoder(
             capabilities: const V2ClientCapabilities(supportsBinaryRfw: false),
           ),
       _delay = delay ?? Future<void>.delayed;

  final V2UiTransport transport;
  final V2SessionController session;
  final V2FeedController feed;
  final SurfaceEnvelopeDecoder decoder;
  final V2ClientCapabilities capabilities;
  final V2FeedAudience audience;
  final int maxBatchSize;
  final V2ReconnectPolicy reconnectPolicy;
  final V2Delay _delay;

  V2RuntimeStatus status = V2RuntimeStatus.idle;
  V2FeedReset? lastReset;
  Object? terminalError;
  Object? transientError;
  SurfaceEnvelope? latestSurface;

  V2FeedCall? _activeCall;
  Future<void>? _loop;
  bool _stopRequested = false;
  bool _forceSnapshot = false;
  bool _disposed = false;
  int _generation = 0;

  bool get hasSurface => latestSurface != null;

  Future<void> start({String? bootstrapSecret}) async {
    if (_loop != null || status == V2RuntimeStatus.authenticating) return;
    _stopRequested = false;
    terminalError = null;
    transientError = null;
    if (!session.isAuthenticated) {
      if (bootstrapSecret == null || bootstrapSecret.trim().isEmpty) {
        _setStatus(V2RuntimeStatus.awaitingSignIn);
        return;
      }
      await authenticateWithBootstrap(bootstrapSecret);
      return;
    }
    feed.bindIdentity(session.identity!);
    _launchLoop();
  }

  Future<void> authenticateWithBootstrap(String secret) async {
    await stop(closeTransport: false);
    _stopRequested = false;
    terminalError = null;
    transientError = null;
    _setStatus(V2RuntimeStatus.authenticating);
    try {
      await session.bootstrap(transport, secret);
      final identity = session.identity!;
      feed.bindIdentity(identity);
      _launchLoop();
    } catch (error) {
      transientError = error;
      _setStatus(V2RuntimeStatus.awaitingSignIn);
      rethrow;
    }
  }

  void _launchLoop() {
    final generation = ++_generation;
    _loop = _run(generation).whenComplete(() {
      if (_generation == generation) _loop = null;
    });
  }

  Future<void> _run(int generation) async {
    var reconnectAttempt = 0;
    var firstConnection = true;
    while (!_stopRequested && generation == _generation) {
      _setStatus(
        firstConnection
            ? V2RuntimeStatus.connecting
            : V2RuntimeStatus.reconnecting,
      );
      firstConnection = false;
      Object? connectionError;
      var reconnectImmediately = false;
      try {
        final accessToken = await session.accessToken(transport);
        final call = await transport.watchSurfaceFeed(
          accessToken: accessToken,
          afterSequence: _forceSnapshot ? 0 : feed.lastSequence,
          audience: audience,
          clientCapabilities: capabilities.names,
          maxBatchSize: maxBatchSize,
        );
        _forceSnapshot = false;
        _activeCall = call;
        _setStatus(V2RuntimeStatus.streaming);
        await for (final event in call.events) {
          if (_stopRequested || generation != _generation) break;
          if (event is V2FeedResetEvent) {
            final snapshots = event.snapshotJson.map(decoder.decode).toList();
            feed.applyServerReset(event, snapshots);
            latestSurface = snapshots.isEmpty
                ? null
                : snapshots.reduce(
                    (left, right) =>
                        left.feedSequence >= right.feedSequence ? left : right,
                  );
            lastReset = V2FeedReset(
              event.reason,
              resumeSequence: event.resumeSequence,
            );
            if (event.resumeSequence > 0) {
              final freshToken = await session.accessToken(transport);
              await transport.acknowledgeSurfaceFeed(
                accessToken: freshToken,
                audience: audience,
                sequence: event.resumeSequence,
              );
              feed.acknowledge(event.resumeSequence);
            }
            transientError = null;
            _notifyListeners();
            continue;
          }
          if (event is! V2FeedSurfaceJson) {
            throw const V2ProtocolException('Unknown V2 feed event.');
          }
          final envelope = decoder.decode(event.surfaceJson);
          final result = feed.accept(envelope);
          if (result is V2FeedReset) {
            lastReset = result;
            // after_sequence=0 can legitimately replay ordinary 1..N events
            // when the server still retains the complete history. Clear both
            // sequence and rendered state so that replay converges and no
            // stale action remains clickable.
            feed.reset();
            latestSurface = null;
            _forceSnapshot = true;
            reconnectImmediately = true;
            _notifyListeners();
            await call.cancel();
            break;
          }
          if (result is V2FeedSurface) latestSurface = result.envelope;
          if (envelope.feedSequence == feed.lastSequence) {
            final freshToken = await session.accessToken(transport);
            await transport.acknowledgeSurfaceFeed(
              accessToken: freshToken,
              audience: audience,
              sequence: envelope.feedSequence,
            );
            feed.acknowledge(envelope.feedSequence);
          }
          transientError = null;
          reconnectAttempt = 0;
          _notifyListeners();
        }
      } catch (error) {
        if (!_stopRequested && generation == _generation) {
          connectionError = error;
          transientError = error;
          _notifyListeners();
        }
      } finally {
        final call = _activeCall;
        _activeCall = null;
        if (call != null) {
          try {
            await call.cancel();
          } catch (_) {
            // Cancellation is best-effort and never replaces the stream error.
          }
        }
      }

      if (_stopRequested || generation != _generation) return;
      if (connectionError is V2TransportException &&
          connectionError.isTerminal) {
        terminalError ??= connectionError;
        _setStatus(V2RuntimeStatus.terminalError);
        return;
      }
      if (connectionError is V2AuthenticationException) {
        try {
          await session.refreshAccessToken(transport);
          transientError = null;
          reconnectImmediately = true;
        } catch (_) {
          terminalError ??= connectionError;
          _setStatus(V2RuntimeStatus.awaitingSignIn);
          return;
        }
      }
      if (connectionError == null && !reconnectImmediately) {
        // Preserve any prior stream error; synthesize a close error only when
        // the stream actually ended cleanly and unexpectedly.
        transientError ??= const V2TransportException(
          V2TransportErrorCode.unavailable,
          'V2 UI feed closed unexpectedly.',
        );
      }

      final maxAttempts = reconnectPolicy.maxAttempts;
      if (maxAttempts != null && reconnectAttempt >= maxAttempts) {
        terminalError ??= transientError;
        _setStatus(V2RuntimeStatus.terminalError);
        return;
      }
      _setStatus(V2RuntimeStatus.reconnecting);
      if (!reconnectImmediately) {
        await _delay(reconnectPolicy.delayFor(reconnectAttempt));
      }
      reconnectAttempt++;
    }
  }

  Future<V2ActionResult> submitAction(
    SurfaceEnvelope surface,
    String bindingId,
    Map<String, Object?> input, {
    DateTime? now,
  }) async {
    final current = feed.surface(surface.surfaceId);
    if (!identical(current, surface) || current?.revision != surface.revision) {
      throw StateError('V2 action surface revision is no longer current.');
    }
    final action = surface.actionByBindingId(bindingId);
    if (action == null) throw StateError('Unknown V2 action binding.');
    final timestamp = (now ?? DateTime.now()).toUtc();
    if (action.isExpired(timestamp) || surface.isExpired(timestamp)) {
      throw StateError('V2 action expired.');
    }
    final accessToken = await session.accessToken(transport);
    return transport.submitAction(
      accessToken: accessToken,
      action: action,
      input: Map<String, Object?>.unmodifiable(input),
    );
  }

  Future<void> stop({bool closeTransport = true}) async {
    _stopRequested = true;
    _generation++;
    final call = _activeCall;
    _activeCall = null;
    if (call != null) {
      try {
        await call.cancel();
      } catch (_) {}
    }
    final loop = _loop;
    _loop = null;
    if (loop != null) {
      try {
        await loop;
      } catch (_) {}
    }
    if (closeTransport) await transport.close();
    if (status != V2RuntimeStatus.awaitingSignIn) {
      _setStatus(V2RuntimeStatus.stopped);
    }
  }

  void _setStatus(V2RuntimeStatus value) {
    if (status == value) return;
    status = value;
    _notifyListeners();
  }

  void _notifyListeners() {
    if (!_disposed) notifyListeners();
  }

  @override
  void dispose() {
    _disposed = true;
    unawaited(stop());
    super.dispose();
  }
}

enum V2TransportErrorCode {
  cancelled,
  unauthenticated,
  permissionDenied,
  invalidArgument,
  unavailable,
  protocol,
  unknown,
}

class V2TransportException implements Exception {
  const V2TransportException(this.code, this.safeMessage);

  final V2TransportErrorCode code;
  final String safeMessage;

  bool get isTerminal => switch (code) {
    V2TransportErrorCode.permissionDenied ||
    V2TransportErrorCode.invalidArgument ||
    V2TransportErrorCode.protocol => true,
    _ => false,
  };

  @override
  String toString() => safeMessage;
}

class V2AuthenticationException extends V2TransportException {
  const V2AuthenticationException([
    String message = 'Authenticated V2 session required.',
  ]) : super(V2TransportErrorCode.unauthenticated, message);
}

class V2ProtocolException extends V2TransportException {
  const V2ProtocolException(String message)
    : super(V2TransportErrorCode.protocol, message);
}

class V2ScopeViolation extends V2ProtocolException {
  const V2ScopeViolation(super.message);
}
