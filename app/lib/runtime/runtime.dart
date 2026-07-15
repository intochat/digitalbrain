import 'dart:async';

import 'package:flutter/foundation.dart';

import 'feed_state.dart';
import 'protocol/surface_protocol.dart';
import 'runtime_errors.dart';
import 'session_state.dart';

export 'feed_state.dart';
export 'runtime_errors.dart';
export 'session_state.dart';

enum RuntimeStatus {
  idle,
  authenticating,
  awaitingSignIn,
  connecting,
  streaming,
  reconnecting,
  stopped,
  terminalError,
}

class ReconnectPolicy {
  const ReconnectPolicy({
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

typedef Delay = Future<void> Function(Duration duration);

class RuntimeController extends ChangeNotifier {
  RuntimeController({
    required this.transport,
    SessionController? session,
    FeedController? feed,
    SurfaceEnvelopeDecoder? decoder,
    this.capabilities = const ClientCapabilities(supportsBinaryRfw: false),
    this.audience = FeedAudience.actor,
    this.maxBatchSize = 50,
    this.reconnectPolicy = const ReconnectPolicy(),
    Delay? delay,
  }) : session = session ?? SessionController(),
       feed = feed ?? FeedController(),
       decoder =
           decoder ??
           SurfaceEnvelopeDecoder(
             capabilities: const ClientCapabilities(supportsBinaryRfw: false),
           ),
       _delay = delay ?? Future<void>.delayed;

  final UiTransport transport;
  final SessionController session;
  final FeedController feed;
  final SurfaceEnvelopeDecoder decoder;
  final ClientCapabilities capabilities;
  final FeedAudience audience;
  final int maxBatchSize;
  final ReconnectPolicy reconnectPolicy;
  final Delay _delay;

  RuntimeStatus status = RuntimeStatus.idle;
  FeedReset? lastReset;
  Object? terminalError;
  Object? transientError;
  SurfaceEnvelope? latestSurface;

  FeedCall? _activeCall;
  Future<void>? _loop;
  bool _stopRequested = false;
  bool _forceSnapshot = false;
  bool _disposed = false;
  int _generation = 0;
  int _scopeEpoch = 0;
  int _authenticationGeneration = 0;

  bool get hasSurface => latestSurface != null;
  int get scopeEpoch => _scopeEpoch;

  bool canSubmitActionsFrom(SurfaceEnvelope surface) =>
      status == RuntimeStatus.streaming &&
      identical(feed.surface(surface.surfaceId), surface);

  Future<void> start() async {
    if (_loop != null || status == RuntimeStatus.authenticating) return;
    _stopRequested = false;
    terminalError = null;
    transientError = null;
    if (!session.isAuthenticated) {
      _setStatus(RuntimeStatus.awaitingSignIn);
      return;
    }
    _bindIdentity(session.identity!);
    _launchLoop();
  }

  Future<void> authenticateWithPassword({
    required String username,
    required String password,
  }) async {
    await _authenticate(
      () => session.login(transport, username: username, password: password),
    );
  }

  Future<void> authenticateWithExternalIdentityToken(String token) async {
    final externalTransport = transport;
    if (externalTransport is! ExternalSessionTransport) {
      throw const AuthenticationException(
        'External identity is not supported by this runtime transport.',
      );
    }
    final typedTransport = externalTransport as ExternalSessionTransport;
    await _authenticate(() => session.loginExternal(typedTransport, token));
  }

  Future<void> _authenticate(Future<bool> Function() establishSession) async {
    final authenticationGeneration = ++_authenticationGeneration;
    await stop(closeTransport: false, invalidateAuthentication: false);
    if (authenticationGeneration != _authenticationGeneration) return;
    _stopRequested = false;
    terminalError = null;
    transientError = null;
    _clearProtectedState(clearFeedIdentity: true);
    _setStatus(RuntimeStatus.authenticating);
    try {
      final applied = await establishSession();
      if (!applied || authenticationGeneration != _authenticationGeneration) {
        return;
      }
      final identity = session.identity!;
      feed.bindIdentity(identity);
      _launchLoop();
    } catch (error) {
      if (authenticationGeneration != _authenticationGeneration) return;
      transientError = error;
      _setStatus(RuntimeStatus.awaitingSignIn);
      rethrow;
    }
  }

  Future<void> signOut() async {
    final authenticationGeneration = ++_authenticationGeneration;
    await stop(closeTransport: false, invalidateAuthentication: false);
    if (authenticationGeneration != _authenticationGeneration) return;
    try {
      await session.signOut(transport);
      if (authenticationGeneration != _authenticationGeneration) return;
      terminalError = null;
      transientError = null;
      _clearProtectedState(clearFeedIdentity: true);
      _setStatus(RuntimeStatus.awaitingSignIn);
    } catch (error) {
      if (authenticationGeneration != _authenticationGeneration) return;
      transientError = error;
      if (session.isAuthenticated) {
        _bindIdentity(session.identity!);
        _stopRequested = false;
        _launchLoop();
      } else {
        _setStatus(RuntimeStatus.awaitingSignIn);
      }
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
        firstConnection ? RuntimeStatus.connecting : RuntimeStatus.reconnecting,
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
        _setStatus(RuntimeStatus.streaming);
        await for (final event in call.events) {
          if (_stopRequested || generation != _generation) break;
          if (event is FeedResetEvent) {
            final snapshots = event.snapshotJson.map(_decodeSurface).toList();
            feed.applyServerReset(event, snapshots);
            latestSurface = snapshots.isEmpty
                ? null
                : snapshots.reduce(
                    (left, right) =>
                        left.feedSequence >= right.feedSequence ? left : right,
                  );
            lastReset = FeedReset(
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
          if (event is! FeedSurfaceJson) {
            throw const ProtocolException('Unknown surface feed event.');
          }
          final envelope = _decodeSurface(event.surfaceJson);
          final result = feed.accept(envelope);
          if (result is FeedReset) {
            lastReset = result;

            feed.reset();
            _forceSnapshot = true;
            reconnectImmediately = true;
            _notifyListeners();
            await call.cancel();
            break;
          }
          if (result is FeedSurface) latestSurface = result.envelope;
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
          } catch (_) {}
        }
      }

      if (_stopRequested || generation != _generation) return;
      if (!session.isAuthenticated) {
        terminalError ??= connectionError;
        _clearProtectedState(clearFeedIdentity: true);
        _setStatus(RuntimeStatus.awaitingSignIn);
        return;
      }
      if (connectionError is TransportException && connectionError.isTerminal) {
        terminalError ??= connectionError;
        _setStatus(RuntimeStatus.terminalError);
        return;
      }
      if (connectionError is AuthenticationException) {
        try {
          await session.refreshAccessToken(transport);
          transientError = null;
          reconnectImmediately = true;
        } catch (_) {
          terminalError ??= connectionError;
          _clearProtectedState(clearFeedIdentity: true);
          _setStatus(RuntimeStatus.awaitingSignIn);
          return;
        }
      }
      if (connectionError == null && !reconnectImmediately) {
        transientError ??= const TransportException(
          TransportErrorCode.unavailable,
          'Surface feed closed unexpectedly.',
        );
      }

      final maxAttempts = reconnectPolicy.maxAttempts;
      if (maxAttempts != null && reconnectAttempt >= maxAttempts) {
        terminalError ??= transientError;
        _setStatus(RuntimeStatus.terminalError);
        return;
      }
      _setStatus(RuntimeStatus.reconnecting);
      if (!reconnectImmediately) {
        await _delay(reconnectPolicy.delayFor(reconnectAttempt));
      }
      reconnectAttempt++;
    }
  }

  Future<ActionResult> submitAction(
    SurfaceEnvelope surface,
    String bindingId,
    Map<String, Object?> input, {
    DateTime? now,
  }) async {
    final current = feed.surface(surface.surfaceId);
    if (!identical(current, surface) || current?.revision != surface.revision) {
      throw StateError('Surface action revision is no longer current.');
    }
    final action = surface.actionByBindingId(bindingId);
    if (action == null) throw StateError('Unknown surface action binding.');
    final timestamp = (now ?? DateTime.now()).toUtc();
    if (action.isExpired(timestamp) || surface.isExpired(timestamp)) {
      throw StateError('Surface action expired.');
    }
    final submissionEpoch = _scopeEpoch;
    final accessToken = await session.accessToken(transport);
    if (submissionEpoch != _scopeEpoch) {
      throw StateError('Surface action scope changed before acceptance.');
    }
    final result = await transport.submitAction(
      accessToken: accessToken,
      action: action,
      input: Map<String, Object?>.unmodifiable(input),
    );
    if (submissionEpoch != _scopeEpoch) {
      throw StateError('Surface action result belongs to a prior scope.');
    }
    return result;
  }

  SurfaceEnvelope _decodeSurface(String source) {
    try {
      return decoder.decode(source);
    } on FormatException {
      throw const ProtocolException('Surface payload is invalid.');
    } on UnsupportedSurfaceCapability {
      throw const ProtocolException(
        'Surface requires an unsupported client capability.',
      );
    }
  }

  void _bindIdentity(SessionIdentity identity) {
    if (!feed.bindIdentity(identity)) return;
    _clearProtectedState(clearFeedIdentity: false);
  }

  void _clearProtectedState({required bool clearFeedIdentity}) {
    if (clearFeedIdentity) {
      feed.clearIdentity();
    } else {
      feed.reset();
    }
    latestSurface = null;
    lastReset = null;
    _forceSnapshot = false;
    _scopeEpoch++;
    _notifyListeners();
  }

  Future<void> stop({
    bool closeTransport = true,
    bool invalidateAuthentication = true,
  }) async {
    if (invalidateAuthentication) _authenticationGeneration++;
    _stopRequested = true;
    final stopGeneration = ++_generation;
    final call = _activeCall;
    _activeCall = null;
    final loop = _loop;
    _loop = null;
    Future<void>? cancellation;
    if (call != null) {
      try {
        cancellation = call.cancel();
      } catch (_) {}
    }
    if (closeTransport) await transport.close();
    if (cancellation != null) {
      try {
        await cancellation;
      } catch (_) {}
    }
    if (loop != null) {
      try {
        await loop;
      } catch (_) {}
    }
    if (_generation == stopGeneration &&
        status != RuntimeStatus.awaitingSignIn) {
      _setStatus(RuntimeStatus.stopped);
    }
  }

  void _setStatus(RuntimeStatus value) {
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
