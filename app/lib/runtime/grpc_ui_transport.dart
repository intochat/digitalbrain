import 'dart:async';
import 'dart:convert';

import 'package:fixnum/fixnum.dart' show Int64;
import 'package:grpc/grpc_or_grpcweb.dart';

import '../grpc/ui.pb.dart' as wire;
import '../grpc/ui.pbgrpc.dart';
import 'protocol/surface_protocol.dart';
import 'runtime_configuration.dart';
import 'runtime.dart';

const Duration unaryRequestTimeout = Duration(seconds: 10);

abstract interface class GrpcClientPort {
  GrpcUnaryResponse<wire.SessionReply> bootstrapSession(
    wire.BootstrapSessionRequest request,
    CallOptions options,
  );

  GrpcUnaryResponse<wire.SessionReply> refreshSession(
    wire.RefreshSessionRequest request,
    CallOptions options,
  );

  GrpcUnaryResponse<wire.LogoutSessionReply> logoutSession(
    wire.LogoutSessionRequest request,
    CallOptions options,
  );

  GrpcFeedResponse watchSurfaceFeed(
    wire.WatchSurfaceFeedRequest request,
    CallOptions options,
  );

  GrpcUnaryResponse<wire.AcknowledgeSurfaceFeedReply> acknowledgeSurfaceFeed(
    wire.AcknowledgeSurfaceFeedRequest request,
    CallOptions options,
  );

  GrpcUnaryResponse<wire.SubmitActionReply> submitAction(
    wire.SubmitActionRequest request,
    CallOptions options,
  );
}

abstract interface class GrpcUnaryResponse<T> {
  Future<T> get response;
  Future<void> cancel();
}

abstract interface class GrpcFeedResponse {
  Stream<wire.SurfaceFeedEvent> get events;
  Future<void> cancel();
}

class GrpcUiTransport implements UiTransport, ExternalSessionTransport {
  GrpcUiTransport.forTesting({
    required GrpcClientPort client,
    Future<void> Function()? close,
  }) : _client = client,
       _close = close ?? _noClose;

  factory GrpcUiTransport.connect(Uri endpoint) {
    if (endpoint.scheme != 'https' || endpoint.host.isEmpty) {
      throw ArgumentError.value(
        endpoint,
        'endpoint',
        'DigitalBrain transport requires an HTTPS endpoint.',
      );
    }

    isTimelineLoggingEnabled = false;
    final port = endpoint.hasPort ? endpoint.port : 443;
    final channel = GrpcOrGrpcWebClientChannel.toSingleEndpoint(
      host: endpoint.host,
      port: port,
      transportSecure: true,
    );
    return GrpcUiTransport.forTesting(
      client: _GeneratedGrpcClientPort(DigitalBrainV2UiClient(channel)),
      close: channel.shutdown,
    );
  }

  final GrpcClientPort _client;
  final Future<void> Function() _close;
  final Set<GrpcUnaryResponse<dynamic>> _activeUnaryResponses = {};
  bool _closed = false;

  @override
  Future<SessionBundle> bootstrapSession(String bootstrapSecret) async {
    try {
      final reply = await _awaitUnary(
        _client.bootstrapSession(
          wire.BootstrapSessionRequest(secret: bootstrapSecret),
          _audienceOnlyOptions(),
        ),
      );
      return _session(reply);
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<SessionBundle> bootstrapExternalSession(String identityToken) async {
    _validateIdentityToken(identityToken);
    try {
      final reply = await _awaitUnary(
        _client.bootstrapSession(
          wire.BootstrapSessionRequest(),
          CallOptions(
            metadata: {
              'authorization': 'Bearer $identityToken',
              'x-v2-audience': digitalBrainUiAudience,
            },
            timeout: unaryRequestTimeout,
          ),
        ),
      );
      return _session(reply);
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<SessionBundle> refreshSession({required String refreshToken}) async {
    try {
      final reply = await _awaitUnary(
        _client.refreshSession(
          wire.RefreshSessionRequest(refreshToken: refreshToken),
          _audienceOnlyOptions(),
        ),
      );
      return _session(reply);
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<void> logout({required String refreshToken}) async {
    try {
      await _awaitUnary(
        _client.logoutSession(
          wire.LogoutSessionRequest(refreshToken: refreshToken),
          _audienceOnlyOptions(),
        ),
      );
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<FeedCall> watchSurfaceFeed({
    required String accessToken,
    required int afterSequence,
    required FeedAudience audience,
    required Set<String> clientCapabilities,
    required int maxBatchSize,
  }) async {
    if (afterSequence < 0) {
      throw ArgumentError.value(afterSequence, 'afterSequence');
    }
    if (maxBatchSize < 1 || maxBatchSize > 100) {
      throw ArgumentError.value(maxBatchSize, 'maxBatchSize');
    }
    try {
      final response = _client.watchSurfaceFeed(
        wire.WatchSurfaceFeedRequest(
          afterSequence: Int64(afterSequence),
          audience: _wireAudience(audience),
          clientCapabilities: clientCapabilities.toList()..sort(),
          maxBatchSize: maxBatchSize,
        ),
        _authenticatedOptions(accessToken),
      );
      return _GrpcFeedCall(response);
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<void> acknowledgeSurfaceFeed({
    required String accessToken,
    required FeedAudience audience,
    required int sequence,
  }) async {
    if (sequence <= 0) throw ArgumentError.value(sequence, 'sequence');
    try {
      final reply = await _awaitUnary(
        _client.acknowledgeSurfaceFeed(
          wire.AcknowledgeSurfaceFeedRequest(
            audience: _wireAudience(audience),
            sequence: Int64(sequence),
          ),
          _authenticatedOptions(accessToken, timeout: unaryRequestTimeout),
        ),
      );
      if (reply.acknowledgedSequence != sequence) {
        throw const ProtocolException(
          'Feed acknowledgement sequence did not match.',
        );
      }
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<ActionResult> submitAction({
    required String accessToken,
    required UiActionRef action,
    required Map<String, Object?> input,
  }) async {
    _validateActionInput(input);
    try {
      final reply = await _awaitUnary(
        _client.submitAction(
          wire.SubmitActionRequest(
            bindingId: action.bindingId,
            actionToken: action.actionToken,
            surfaceId: action.surfaceId,
            surfaceRevision: action.surfaceRevision,
            inputJson: jsonEncode(input),
          ),
          _authenticatedOptions(accessToken, timeout: unaryRequestTimeout),
        ),
      );
      if (reply.operationId.trim().isEmpty ||
          reply.idempotencyKey.trim().isEmpty) {
        throw const ProtocolException('Action response is incomplete.');
      }
      return ActionResult(
        operationId: reply.operationId,
        idempotencyKey: reply.idempotencyKey,
      );
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<void> close() async {
    if (_closed) return;
    _closed = true;
    final pending = _activeUnaryResponses.toList(growable: false);
    for (final response in pending) {
      try {
        await response.cancel();
      } catch (_) {}
    }
    await _close();
  }

  Future<T> _awaitUnary<T>(GrpcUnaryResponse<T> call) async {
    if (_closed) {
      await call.cancel();
      throw const TransportException(
        TransportErrorCode.cancelled,
        'UI request was cancelled.',
      );
    }
    _activeUnaryResponses.add(call);
    try {
      return await call.response;
    } finally {
      _activeUnaryResponses.remove(call);
    }
  }

  static SessionBundle _session(wire.SessionReply reply) {
    final accessExpiry = reply.accessExpiresAtUnixMs.toInt();
    final refreshExpiry = reply.refreshExpiresAtUnixMs.toInt();
    if (accessExpiry <= 0 || refreshExpiry <= 0) {
      throw const ProtocolException('Session response has invalid expiry.');
    }
    return SessionBundle(
      identity: SessionIdentity(
        sessionId: reply.sessionId,
        ownerId: reply.ownerId,
        actorId: reply.actorId,
      ),
      credentials: SessionCredentials(
        accessToken: reply.accessToken,
        refreshToken: reply.refreshToken,
        accessExpiresAt: DateTime.fromMillisecondsSinceEpoch(
          accessExpiry,
          isUtc: true,
        ),
        refreshExpiresAt: DateTime.fromMillisecondsSinceEpoch(
          refreshExpiry,
          isUtc: true,
        ),
      ),
    );
  }

  static CallOptions _audienceOnlyOptions() => CallOptions(
    metadata: const {'x-v2-audience': digitalBrainUiAudience},
    timeout: unaryRequestTimeout,
  );

  static CallOptions _authenticatedOptions(
    String accessToken, {
    Duration? timeout,
  }) {
    if (accessToken.trim().isEmpty) throw const AuthenticationException();
    return CallOptions(
      metadata: {
        'x-v2-session': accessToken,
        'x-v2-audience': digitalBrainUiAudience,
      },
      timeout: timeout,
    );
  }

  static wire.FeedAudienceKind _wireAudience(FeedAudience audience) =>
      switch (audience) {
        FeedAudience.actor => wire.FeedAudienceKind.FEED_AUDIENCE_KIND_ACTOR,
        FeedAudience.owner => wire.FeedAudienceKind.FEED_AUDIENCE_KIND_OWNER,
        FeedAudience.public => wire.FeedAudienceKind.FEED_AUDIENCE_KIND_PUBLIC,
      };

  static Future<void> _noClose() async {}
}

void _validateIdentityToken(String token) {
  if (token.length < 32 ||
      token.length > 8 * 1024 - 7 ||
      token.trim() != token ||
      !RegExp(
        r'^[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$',
      ).hasMatch(token)) {
    throw const AuthenticationException('External identity was rejected.');
  }
}

class _GeneratedGrpcClientPort implements GrpcClientPort {
  const _GeneratedGrpcClientPort(this.client);

  final DigitalBrainV2UiClient client;

  @override
  GrpcUnaryResponse<wire.SessionReply> bootstrapSession(
    wire.BootstrapSessionRequest request,
    CallOptions options,
  ) => _GeneratedGrpcUnaryResponse(
    client.bootstrapSession(request, options: options),
  );

  @override
  GrpcUnaryResponse<wire.SessionReply> refreshSession(
    wire.RefreshSessionRequest request,
    CallOptions options,
  ) => _GeneratedGrpcUnaryResponse(
    client.refreshSession(request, options: options),
  );

  @override
  GrpcUnaryResponse<wire.LogoutSessionReply> logoutSession(
    wire.LogoutSessionRequest request,
    CallOptions options,
  ) => _GeneratedGrpcUnaryResponse(
    client.logoutSession(request, options: options),
  );

  @override
  GrpcFeedResponse watchSurfaceFeed(
    wire.WatchSurfaceFeedRequest request,
    CallOptions options,
  ) => _GeneratedGrpcFeedResponse(
    client.watchSurfaceFeed(request, options: options),
  );

  @override
  GrpcUnaryResponse<wire.AcknowledgeSurfaceFeedReply> acknowledgeSurfaceFeed(
    wire.AcknowledgeSurfaceFeedRequest request,
    CallOptions options,
  ) => _GeneratedGrpcUnaryResponse(
    client.acknowledgeSurfaceFeed(request, options: options),
  );

  @override
  GrpcUnaryResponse<wire.SubmitActionReply> submitAction(
    wire.SubmitActionRequest request,
    CallOptions options,
  ) => _GeneratedGrpcUnaryResponse(
    client.submitAction(request, options: options),
  );
}

class _GeneratedGrpcUnaryResponse<T> implements GrpcUnaryResponse<T> {
  const _GeneratedGrpcUnaryResponse(this._response);

  final ResponseFuture<T> _response;

  @override
  Future<T> get response => _response;

  @override
  Future<void> cancel() => _response.cancel();
}

class _GeneratedGrpcFeedResponse implements GrpcFeedResponse {
  const _GeneratedGrpcFeedResponse(this.response);

  final ResponseStream<wire.SurfaceFeedEvent> response;

  @override
  Stream<wire.SurfaceFeedEvent> get events => response;

  @override
  Future<void> cancel() => response.cancel();
}

class _GrpcFeedCall implements FeedCall {
  _GrpcFeedCall(this._response);

  final GrpcFeedResponse _response;

  @override
  late final Stream<FeedEvent> events = _response.events.transform(
    StreamTransformer<wire.SurfaceFeedEvent, FeedEvent>.fromHandlers(
      handleData: (event, sink) {
        try {
          sink.add(switch (event.whichEvent()) {
            wire.SurfaceFeedEvent_Event.surfaceJson => FeedSurfaceJson(
              event.surfaceJson,
            ),
            wire.SurfaceFeedEvent_Event.reset => FeedResetEvent(
              reason: event.reset.reason,
              resumeSequence: event.reset.resumeSequence.toInt(),
              snapshotJson: List<String>.unmodifiable(event.reset.snapshotJson),
            ),
            wire.SurfaceFeedEvent_Event.notSet => throw const ProtocolException(
              'Feed event is empty.',
            ),
          });
        } catch (error, stackTrace) {
          sink.addError(_safeTransportError(error), stackTrace);
        }
      },
      handleError: (error, stackTrace, sink) {
        sink.addError(_safeTransportError(error), stackTrace);
      },
    ),
  );

  @override
  Future<void> cancel() async {
    try {
      await _response.cancel();
    } on GrpcError catch (error) {
      if (error.code != StatusCode.cancelled) throw _safeTransportError(error);
    }
  }
}

TransportException _safeTransportError(Object error) {
  if (error is TransportException) return error;
  if (error is GrpcError) {
    return switch (error.code) {
      StatusCode.cancelled => const TransportException(
        TransportErrorCode.cancelled,
        'UI request was cancelled.',
      ),
      StatusCode.unauthenticated => const AuthenticationException(
        'UI session is invalid or expired.',
      ),
      StatusCode.permissionDenied => const TransportException(
        TransportErrorCode.permissionDenied,
        'UI request was denied.',
      ),
      StatusCode.invalidArgument => const TransportException(
        TransportErrorCode.invalidArgument,
        'UI request was invalid.',
      ),
      StatusCode.alreadyExists => const TransportException(
        TransportErrorCode.permissionDenied,
        'UI action is no longer available.',
      ),
      StatusCode.failedPrecondition => const PreconditionException(),
      StatusCode.resourceExhausted => const TransportException(
        TransportErrorCode.invalidArgument,
        'UI request exceeded its size limit.',
      ),
      StatusCode.unavailable ||
      StatusCode.deadlineExceeded => const TransportException(
        TransportErrorCode.unavailable,
        'UI service is temporarily unavailable.',
      ),
      _ => const TransportException(
        TransportErrorCode.unknown,
        'UI transport failed.',
      ),
    };
  }
  return const TransportException(
    TransportErrorCode.unknown,
    'UI transport failed.',
  );
}

const Set<String> _forbiddenActionInputKeys = {
  'accesstoken',
  'actiontoken',
  'authorization',
  'clientsecret',
  'actorid',
  'ownerid',
  'refreshtoken',
  'sessionid',
};

void _validateActionInput(Object? value, [int depth = 0]) {
  if (depth > 32) {
    throw const ProtocolException('Action input is too deeply nested.');
  }
  if (value == null || value is String || value is num || value is bool) {
    return;
  }
  if (value is List) {
    for (final item in value) {
      _validateActionInput(item, depth + 1);
    }
    return;
  }
  if (value is Map) {
    for (final entry in value.entries) {
      if (entry.key is! String) {
        throw const ProtocolException(
          'Action input contains a non-string key.',
        );
      }
      final normalized = (entry.key as String)
          .replaceAll(RegExp('[^A-Za-z0-9]'), '')
          .toLowerCase();
      if (_forbiddenActionInputKeys.contains(normalized)) {
        throw const ProtocolException(
          'Action input contains a forbidden private field.',
        );
      }
      _validateActionInput(entry.value, depth + 1);
    }
    return;
  }
  throw const ProtocolException('Action input is not JSON-safe.');
}
