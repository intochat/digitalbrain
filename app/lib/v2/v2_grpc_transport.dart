import 'dart:async';
import 'dart:convert';

import 'package:fixnum/fixnum.dart' show Int64;
import 'package:grpc/grpc_or_grpcweb.dart';

import '../grpc/v2_ui.pb.dart' as wire;
import '../grpc/v2_ui.pbgrpc.dart';
import 'protocol/surface_protocol.dart';
import 'v2_config.dart';
import 'v2_runtime.dart';

/// Small generated-client seam used by transport tests. Production always
/// uses [_GeneratedV2GrpcClientPort].
abstract interface class V2GrpcClientPort {
  Future<wire.SessionReply> bootstrapSession(
    wire.BootstrapSessionRequest request,
    CallOptions options,
  );

  Future<wire.SessionReply> refreshSession(
    wire.RefreshSessionRequest request,
    CallOptions options,
  );

  V2GrpcFeedResponse watchSurfaceFeed(
    wire.WatchSurfaceFeedRequest request,
    CallOptions options,
  );

  Future<wire.AcknowledgeSurfaceFeedReply> acknowledgeSurfaceFeed(
    wire.AcknowledgeSurfaceFeedRequest request,
    CallOptions options,
  );

  Future<wire.SubmitActionReply> submitAction(
    wire.SubmitActionRequest request,
    CallOptions options,
  );
}

abstract interface class V2GrpcFeedResponse {
  Stream<wire.SurfaceFeedEvent> get events;
  Future<void> cancel();
}

class V2GrpcUiTransport implements V2UiTransport {
  V2GrpcUiTransport.forTesting({
    required V2GrpcClientPort client,
    Future<void> Function()? close,
  }) : _client = client,
       _close = close ?? _noClose;

  factory V2GrpcUiTransport.connect(Uri endpoint) {
    if (endpoint.scheme != 'https' || endpoint.host.isEmpty) {
      throw ArgumentError.value(
        endpoint,
        'endpoint',
        'DigitalBrain transport requires an HTTPS endpoint.',
      );
    }
    // grpc-dart's optional timeline profiler includes call metadata. V2 call
    // metadata contains the signed session, so it must stay disabled.
    isTimelineLoggingEnabled = false;
    final port = endpoint.hasPort ? endpoint.port : 443;
    final channel = GrpcOrGrpcWebClientChannel.toSingleEndpoint(
      host: endpoint.host,
      port: port,
      transportSecure: true,
    );
    return V2GrpcUiTransport.forTesting(
      client: _GeneratedV2GrpcClientPort(DigitalBrainV2UiClient(channel)),
      close: channel.shutdown,
    );
  }

  final V2GrpcClientPort _client;
  final Future<void> Function() _close;
  bool _closed = false;

  @override
  Future<V2SessionBundle> bootstrapSession(String bootstrapSecret) async {
    try {
      final reply = await _client.bootstrapSession(
        wire.BootstrapSessionRequest(secret: bootstrapSecret),
        _audienceOnlyOptions(),
      );
      return _session(reply);
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<V2SessionBundle> refreshSession({required String refreshToken}) async {
    try {
      final reply = await _client.refreshSession(
        wire.RefreshSessionRequest(refreshToken: refreshToken),
        _audienceOnlyOptions(),
      );
      return _session(reply);
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<V2FeedCall> watchSurfaceFeed({
    required String accessToken,
    required int afterSequence,
    required V2FeedAudience audience,
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
      return _V2GrpcFeedCall(response);
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<void> acknowledgeSurfaceFeed({
    required String accessToken,
    required V2FeedAudience audience,
    required int sequence,
  }) async {
    if (sequence <= 0) throw ArgumentError.value(sequence, 'sequence');
    try {
      final reply = await _client.acknowledgeSurfaceFeed(
        wire.AcknowledgeSurfaceFeedRequest(
          audience: _wireAudience(audience),
          sequence: Int64(sequence),
        ),
        _authenticatedOptions(accessToken),
      );
      if (reply.acknowledgedSequence != sequence) {
        throw const V2ProtocolException(
          'Feed acknowledgement sequence did not match.',
        );
      }
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<V2ActionResult> submitAction({
    required String accessToken,
    required UiActionRef action,
    required Map<String, Object?> input,
  }) async {
    _validateActionInput(input);
    try {
      final reply = await _client.submitAction(
        wire.SubmitActionRequest(
          bindingId: action.bindingId,
          actionToken: action.actionToken,
          surfaceId: action.surfaceId,
          surfaceRevision: action.surfaceRevision,
          inputJson: jsonEncode(input),
        ),
        _authenticatedOptions(accessToken),
      );
      if (reply.operationId.trim().isEmpty ||
          reply.idempotencyKey.trim().isEmpty) {
        throw const V2ProtocolException('Action response is incomplete.');
      }
      return V2ActionResult(
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
    await _close();
  }

  static V2SessionBundle _session(wire.SessionReply reply) {
    final accessExpiry = reply.accessExpiresAtUnixMs.toInt();
    final refreshExpiry = reply.refreshExpiresAtUnixMs.toInt();
    if (accessExpiry <= 0 || refreshExpiry <= 0) {
      throw const V2ProtocolException('Session response has invalid expiry.');
    }
    return V2SessionBundle(
      identity: V2SessionIdentity(
        sessionId: reply.sessionId,
        tenantId: reply.tenantId,
        workspaceId: reply.workspaceId,
        principalId: reply.principalId,
      ),
      credentials: V2SessionCredentials(
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

  static CallOptions _audienceOnlyOptions() =>
      CallOptions(metadata: const {'x-v2-audience': digitalBrainV2UiAudience});

  static CallOptions _authenticatedOptions(String accessToken) {
    if (accessToken.trim().isEmpty) throw const V2AuthenticationException();
    return CallOptions(
      metadata: {
        'x-v2-session': accessToken,
        'x-v2-audience': digitalBrainV2UiAudience,
      },
    );
  }

  static wire.FeedAudienceKind _wireAudience(V2FeedAudience audience) =>
      switch (audience) {
        V2FeedAudience.principal =>
          wire.FeedAudienceKind.FEED_AUDIENCE_KIND_PRINCIPAL,
        V2FeedAudience.workspace =>
          wire.FeedAudienceKind.FEED_AUDIENCE_KIND_WORKSPACE,
        V2FeedAudience.public =>
          wire.FeedAudienceKind.FEED_AUDIENCE_KIND_PUBLIC,
      };

  static Future<void> _noClose() async {}
}

class _GeneratedV2GrpcClientPort implements V2GrpcClientPort {
  const _GeneratedV2GrpcClientPort(this.client);

  final DigitalBrainV2UiClient client;

  @override
  Future<wire.SessionReply> bootstrapSession(
    wire.BootstrapSessionRequest request,
    CallOptions options,
  ) => client.bootstrapSession(request, options: options);

  @override
  Future<wire.SessionReply> refreshSession(
    wire.RefreshSessionRequest request,
    CallOptions options,
  ) => client.refreshSession(request, options: options);

  @override
  V2GrpcFeedResponse watchSurfaceFeed(
    wire.WatchSurfaceFeedRequest request,
    CallOptions options,
  ) => _GeneratedV2GrpcFeedResponse(
    client.watchSurfaceFeed(request, options: options),
  );

  @override
  Future<wire.AcknowledgeSurfaceFeedReply> acknowledgeSurfaceFeed(
    wire.AcknowledgeSurfaceFeedRequest request,
    CallOptions options,
  ) => client.acknowledgeSurfaceFeed(request, options: options);

  @override
  Future<wire.SubmitActionReply> submitAction(
    wire.SubmitActionRequest request,
    CallOptions options,
  ) => client.submitAction(request, options: options);
}

class _GeneratedV2GrpcFeedResponse implements V2GrpcFeedResponse {
  const _GeneratedV2GrpcFeedResponse(this.response);

  final ResponseStream<wire.SurfaceFeedEvent> response;

  @override
  Stream<wire.SurfaceFeedEvent> get events => response;

  @override
  Future<void> cancel() => response.cancel();
}

class _V2GrpcFeedCall implements V2FeedCall {
  _V2GrpcFeedCall(this._response);

  final V2GrpcFeedResponse _response;

  @override
  late final Stream<V2FeedEvent> events = _response.events.transform(
    StreamTransformer<wire.SurfaceFeedEvent, V2FeedEvent>.fromHandlers(
      handleData: (event, sink) {
        try {
          sink.add(switch (event.whichEvent()) {
            wire.SurfaceFeedEvent_Event.surfaceJson => V2FeedSurfaceJson(
              event.surfaceJson,
            ),
            wire.SurfaceFeedEvent_Event.reset => V2FeedResetEvent(
              reason: event.reset.reason,
              resumeSequence: event.reset.resumeSequence.toInt(),
              snapshotJson: List<String>.unmodifiable(event.reset.snapshotJson),
            ),
            wire.SurfaceFeedEvent_Event.notSet =>
              throw const V2ProtocolException('V2 feed event is empty.'),
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

V2TransportException _safeTransportError(Object error) {
  if (error is V2TransportException) return error;
  if (error is GrpcError) {
    return switch (error.code) {
      StatusCode.cancelled => const V2TransportException(
        V2TransportErrorCode.cancelled,
        'V2 UI request was cancelled.',
      ),
      StatusCode.unauthenticated => const V2AuthenticationException(
        'V2 UI session is invalid or expired.',
      ),
      StatusCode.permissionDenied => const V2TransportException(
        V2TransportErrorCode.permissionDenied,
        'V2 UI request was denied.',
      ),
      StatusCode.invalidArgument => const V2TransportException(
        V2TransportErrorCode.invalidArgument,
        'V2 UI request was invalid.',
      ),
      StatusCode.alreadyExists => const V2TransportException(
        V2TransportErrorCode.permissionDenied,
        'V2 UI action is no longer available.',
      ),
      StatusCode.failedPrecondition => const V2ProtocolException(
        'V2 UI feed state was rejected.',
      ),
      StatusCode.resourceExhausted => const V2TransportException(
        V2TransportErrorCode.invalidArgument,
        'V2 UI request exceeded its size limit.',
      ),
      StatusCode.unavailable ||
      StatusCode.deadlineExceeded => const V2TransportException(
        V2TransportErrorCode.unavailable,
        'V2 UI service is temporarily unavailable.',
      ),
      _ => const V2TransportException(
        V2TransportErrorCode.unknown,
        'V2 UI transport failed.',
      ),
    };
  }
  return const V2TransportException(
    V2TransportErrorCode.unknown,
    'V2 UI transport failed.',
  );
}

const Set<String> _forbiddenActionInputKeys = {
  'accesstoken',
  'actiontoken',
  'authorization',
  'clientsecret',
  'principalid',
  'refreshtoken',
  'sessionid',
  'tenantid',
  'workspaceid',
};

void _validateActionInput(Object? value, [int depth = 0]) {
  if (depth > 32) {
    throw const V2ProtocolException('V2 action input is too deeply nested.');
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
        throw const V2ProtocolException(
          'V2 action input contains a non-string key.',
        );
      }
      final normalized = (entry.key as String)
          .replaceAll(RegExp('[^A-Za-z0-9]'), '')
          .toLowerCase();
      if (_forbiddenActionInputKeys.contains(normalized)) {
        throw const V2ProtocolException(
          'V2 action input contains a forbidden private field.',
        );
      }
      _validateActionInput(entry.value, depth + 1);
    }
    return;
  }
  throw const V2ProtocolException('V2 action input is not JSON-safe.');
}
