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

final class _StopWork {
  const _StopWork({
    required this.generation,
    required this.cancellation,
    required this.loop,
  });

  final int generation;
  final Future<void>? cancellation;
  final Future<void>? loop;
}

final class _CloseFailure {
  const _CloseFailure(this.error, this.stackTrace);

  final Object error;
  final StackTrace stackTrace;
}

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
  final Set<Future<void>> _restartableStops = {};
  Future<void>? _terminalShutdownFuture;
  Future<_CloseFailure?>? _transportCloseOutcome;
  bool _stopRequested = false;
  bool _forceSnapshot = false;
  bool _disposed = false;
  bool _retainAuthenticatedContentForReauthentication = false;
  int _generation = 0;
  int _scopeEpoch = 0;
  int _authenticationGeneration = 0;

  bool get hasSurface => latestSurface != null;
  int get scopeEpoch => _scopeEpoch;
  bool get retainAuthenticatedContentForReauthentication =>
      _retainAuthenticatedContentForReauthentication;
  bool get _isTerminated => _disposed || _terminalShutdownFuture != null;

  bool canSubmitActionsFrom(SurfaceEnvelope surface) =>
      status == RuntimeStatus.streaming &&
      identical(feed.surface(surface.surfaceId), surface);

  Future<void> start() async {
    if (_isTerminated ||
        _loop != null ||
        status == RuntimeStatus.authenticating) {
      return;
    }
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
    if (_isTerminated) return;
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
    if (_isTerminated) return;
    final authenticationGeneration = ++_authenticationGeneration;
    await stop(closeTransport: false, invalidateAuthentication: false);
    if (_isTerminated ||
        authenticationGeneration != _authenticationGeneration) {
      return;
    }
    _stopRequested = false;
    terminalError = null;
    transientError = null;
    _clearProtectedState(clearFeedIdentity: true);
    _setStatus(RuntimeStatus.authenticating);
    try {
      final applied = await establishSession();
      if (_isTerminated ||
          !applied ||
          authenticationGeneration != _authenticationGeneration) {
        return;
      }
      final identity = session.identity!;
      feed.bindIdentity(identity);
      _retainAuthenticatedContentForReauthentication = false;
      _launchLoop();
    } catch (error) {
      if (_isTerminated ||
          authenticationGeneration != _authenticationGeneration) {
        return;
      }
      transientError = error;
      _setStatus(RuntimeStatus.awaitingSignIn);
      rethrow;
    }
  }

  Future<void> signOut() async {
    if (_isTerminated) return;
    _retainAuthenticatedContentForReauthentication = false;
    final authenticationGeneration = ++_authenticationGeneration;
    final revocation = session.signOut(transport);
    final stopping = stop(
      closeTransport: false,
      invalidateAuthentication: false,
    );
    try {
      await Future.wait([revocation, stopping]);
      if (_isTerminated ||
          authenticationGeneration != _authenticationGeneration) {
        return;
      }
      terminalError = null;
      transientError = null;
      _clearProtectedState(clearFeedIdentity: true);
      _setStatus(RuntimeStatus.awaitingSignIn);
    } catch (error) {
      if (_isTerminated ||
          authenticationGeneration != _authenticationGeneration) {
        return;
      }
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

  Future<void> requireAuthentication() async {
    if (_isTerminated) return;
    _retainAuthenticatedContentForReauthentication = true;
    final authenticationGeneration = ++_authenticationGeneration;
    session.beginExpiration();
    await _cancelProductCalls();
    await stop(closeTransport: false, invalidateAuthentication: false);
    if (_isTerminated ||
        authenticationGeneration != _authenticationGeneration) {
      return;
    }
    session.expire();
    terminalError = null;
    transientError = null;
    _clearProtectedState(clearFeedIdentity: true);
    _setStatus(RuntimeStatus.awaitingSignIn);
  }

  void _launchLoop() {
    if (_isTerminated) return;
    final generation = ++_generation;
    _loop = _run(generation).whenComplete(() {
      if (_generation == generation) _loop = null;
    });
  }

  bool _isCurrentRun(int generation) =>
      !_isTerminated && !_stopRequested && generation == _generation;

  Future<void> _run(int generation) async {
    var reconnectAttempt = 0;
    var firstConnection = true;
    while (_isCurrentRun(generation)) {
      _setStatus(
        firstConnection ? RuntimeStatus.connecting : RuntimeStatus.reconnecting,
      );
      firstConnection = false;
      Object? connectionError;
      var reconnectImmediately = false;
      FeedCall? ownedCall;
      try {
        final accessToken = await session.accessToken(transport);
        if (!_isCurrentRun(generation)) return;
        final call = await transport.watchSurfaceFeed(
          accessToken: accessToken,
          afterSequence: _forceSnapshot ? 0 : feed.lastSequence,
          audience: audience,
          clientCapabilities: capabilities.names,
          maxBatchSize: maxBatchSize,
        );
        if (!_isCurrentRun(generation)) {
          try {
            await call.cancel();
          } catch (_) {}
          return;
        }
        ownedCall = call;
        _forceSnapshot = false;
        _activeCall = call;
        _setStatus(RuntimeStatus.streaming);
        await for (final event in call.events) {
          if (!_isCurrentRun(generation)) break;
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
              if (!_isCurrentRun(generation)) return;
              await transport.acknowledgeSurfaceFeed(
                accessToken: freshToken,
                audience: audience,
                sequence: event.resumeSequence,
              );
              if (!_isCurrentRun(generation)) return;
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
            if (!_isCurrentRun(generation)) return;
            await transport.acknowledgeSurfaceFeed(
              accessToken: freshToken,
              audience: audience,
              sequence: envelope.feedSequence,
            );
            if (!_isCurrentRun(generation)) return;
            feed.acknowledge(envelope.feedSequence);
          }
          transientError = null;
          reconnectAttempt = 0;
          _notifyListeners();
        }
      } catch (error) {
        if (_isCurrentRun(generation)) {
          connectionError = error;
          transientError = error;
          _notifyListeners();
        }
      } finally {
        final call = ownedCall;
        if (call != null && identical(_activeCall, call)) {
          _activeCall = null;
          try {
            await call.cancel();
          } catch (_) {}
        }
      }

      if (!_isCurrentRun(generation)) return;
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
          if (!_isCurrentRun(generation)) return;
          transientError = null;
          reconnectImmediately = true;
        } catch (error) {
          if (!_isCurrentRun(generation)) return;
          if (session.isAuthenticated) {
            transientError = error;
            reconnectImmediately = false;
          } else {
            _retainAuthenticatedContentForReauthentication = true;
            terminalError ??= connectionError;
            _clearProtectedState(clearFeedIdentity: true);
            _setStatus(RuntimeStatus.awaitingSignIn);
            return;
          }
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
  }) {
    if (invalidateAuthentication) _authenticationGeneration++;
    final terminalShutdown = _terminalShutdownFuture;
    if (terminalShutdown != null) return terminalShutdown;
    if (!closeTransport) return _startRestartableStop();
    return _startTerminalShutdown();
  }

  Future<void> _startRestartableStop() {
    final operation = _completeStopWork(
      _captureStopWork(),
      publishStopped: true,
    );
    _restartableStops.add(operation);
    unawaited(
      operation.then<void>(
        (_) => _restartableStops.remove(operation),
        onError: (Object _, StackTrace _) {
          _restartableStops.remove(operation);
        },
      ),
    );
    return operation;
  }

  Future<void> _startTerminalShutdown() {
    final completion = Completer<void>();
    _terminalShutdownFuture = completion.future;
    session.beginExpiration();
    unawaited(_cancelProductCalls());
    session.expire();
    final restartableStops = List<Future<void>>.of(_restartableStops);
    final work = _captureStopWork();
    final closeOutcome = _transportCloseOutcome ??= _acquireTransportClose();
    final operation = _completeTerminalShutdown(
      work,
      restartableStops,
      closeOutcome,
    );
    unawaited(
      operation.then<void>(
        (_) => completion.complete(),
        onError: (Object error, StackTrace stackTrace) {
          completion.completeError(error, stackTrace);
        },
      ),
    );
    return completion.future;
  }

  Future<void> _cancelProductCalls() async {
    if (transport case final SessionProductCallCancellation cancellation) {
      await cancellation.cancelProductCalls();
    }
  }

  _StopWork _captureStopWork() {
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
    return _StopWork(
      generation: stopGeneration,
      cancellation: cancellation,
      loop: loop,
    );
  }

  Future<_CloseFailure?> _acquireTransportClose() =>
      Future<void>.sync(transport.close).then<_CloseFailure?>(
        (_) => null,
        onError: (Object error, StackTrace stackTrace) =>
            _CloseFailure(error, stackTrace),
      );

  Future<void> _completeStopWork(
    _StopWork work, {
    required bool publishStopped,
  }) async {
    try {
      final cancellation = work.cancellation;
      if (cancellation != null) {
        try {
          await cancellation;
        } catch (_) {}
      }
      final loop = work.loop;
      if (loop != null) {
        try {
          await loop;
        } catch (_) {}
      }
    } finally {
      if (publishStopped &&
          _generation == work.generation &&
          status != RuntimeStatus.awaitingSignIn) {
        _setStatus(RuntimeStatus.stopped);
      }
    }
  }

  Future<void> _completeTerminalShutdown(
    _StopWork work,
    List<Future<void>> restartableStops,
    Future<_CloseFailure?> closeOutcome,
  ) async {
    _CloseFailure? closeFailure;
    try {
      await Future.wait<void>([
        ...restartableStops,
        _completeStopWork(work, publishStopped: false),
      ]);
      closeFailure = await closeOutcome;
    } finally {
      if (_generation == work.generation &&
          status != RuntimeStatus.awaitingSignIn) {
        _setStatus(RuntimeStatus.stopped);
      }
    }
    if (closeFailure != null) {
      Error.throwWithStackTrace(closeFailure.error, closeFailure.stackTrace);
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
    unawaited(_stopForDispose());
    super.dispose();
  }

  Future<void> _stopForDispose() async {
    try {
      await stop();
    } catch (_) {}
  }
}
