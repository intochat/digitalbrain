import 'dart:async';
import 'dart:convert';

import 'package:fixnum/fixnum.dart' show Int64;
import 'package:grpc/grpc_or_grpcweb.dart';

import '../core/session/digitalbrain_client.dart';
import '../grpc/ui.pb.dart' as wire;
import '../grpc/ui.pbgrpc.dart';
import 'protocol/surface_protocol.dart';
import 'runtime_configuration.dart';
import 'runtime.dart';

const Duration unaryRequestTimeout = Duration(seconds: 10);
const Duration featureSuggestionRequestTimeout = Duration(seconds: 65);
const Duration featureVerificationRequestTimeout = Duration(seconds: 65);
const Duration featureAuthorityRequestTimeout = Duration(seconds: 30);

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

  GrpcUnaryResponse<wire.ListFeaturesReply> listFeatures(
    wire.ListFeaturesRequest request,
    CallOptions options,
  );

  GrpcUnaryResponse<wire.FeatureDraftReply> getFeatureDraft(
    wire.GetFeatureDraftRequest request,
    CallOptions options,
  );

  GrpcUnaryResponse<wire.FeatureDraftReply> resetFeatureDraftInstallation(
    wire.ResetFeatureDraftInstallationRequest request,
    CallOptions options,
  );

  GrpcUnaryResponse<wire.FeatureDraftReply> reviseFeatureDraft(
    wire.ReviseFeatureDraftRequest request,
    CallOptions options,
  );

  GrpcUnaryResponse<wire.FeatureDraftPatchReply> suggestFeatureChange(
    wire.SuggestFeatureChangeRequest request,
    CallOptions options,
  );

  GrpcUnaryResponse<wire.FeatureReleaseReviewReply> verifyFeatureDraft(
    wire.VerifyFeatureDraftRequest request,
    CallOptions options,
  );

  GrpcUnaryResponse<wire.FeatureAccessReviewReply> reviewFeatureAccess(
    wire.ReviewFeatureAccessRequest request,
    CallOptions options,
  );

  GrpcUnaryResponse<wire.FeatureInstallReply> installFeatureVersion(
    wire.InstallFeatureVersionRequest request,
    CallOptions options,
  );

  GrpcUnaryResponse<wire.ResumeOriginatingRequestReply>
  resumeOriginatingRequest(
    wire.ResumeOriginatingRequestRequest request,
    CallOptions options,
  );

  GrpcUnaryResponse<wire.FeatureReply> getFeature(
    wire.GetFeatureRequest request,
    CallOptions options,
  );

  GrpcUnaryResponse<wire.FeatureReleaseSourceReply> getFeatureReleaseSource(
    wire.GetFeatureReleaseSourceRequest request,
    CallOptions options,
  );

  GrpcUnaryResponse<wire.FeatureReply> rollbackFeatureVersion(
    wire.RollbackFeatureVersionRequest request,
    CallOptions options,
  );

  GrpcUnaryResponse<wire.ListActivityReply> listActivity(
    wire.ListActivityRequest request,
    CallOptions options,
  );

  GrpcUnaryResponse<wire.RunReply> getRun(
    wire.GetRunRequest request,
    CallOptions options,
  );

  GrpcUnaryResponse<wire.GetConversationContextReply> getConversationContext(
    wire.GetConversationContextRequest request,
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

class GrpcUiTransport
    implements
        UiTransport,
        ExternalSessionTransport,
        DigitalBrainTransport,
        SessionProductCallCancellation {
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
  final Set<GrpcUnaryResponse<dynamic>> _activeProductUnaryResponses = {};
  int _productCallEpoch = 0;
  bool _closed = false;

  @override
  Future<SessionBundle> login({
    required String username,
    required String password,
  }) async {
    try {
      final reply = await _awaitUnary(
        _client.bootstrapSession(
          wire.BootstrapSessionRequest(username: username, password: password),
          _audienceOnlyOptions(),
        ),
      );
      return _session(reply);
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<SessionBundle> loginExternal(String identityToken) async {
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
  Future<wire.FeatureDraftReply> getFeatureDraft({
    required String accessToken,
    required wire.GetFeatureDraftRequest request,
  }) async {
    try {
      return await _awaitProductUnary(
        _client.getFeatureDraft(
          request,
          _authenticatedOptions(accessToken, timeout: unaryRequestTimeout),
        ),
      );
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<wire.ListFeaturesReply> listFeatures({
    required String accessToken,
    required wire.ListFeaturesRequest request,
  }) async {
    try {
      return await _awaitProductUnary(
        _client.listFeatures(
          request,
          _authenticatedOptions(accessToken, timeout: unaryRequestTimeout),
        ),
      );
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<wire.FeatureDraftReply> resetFeatureDraftInstallation({
    required String accessToken,
    required wire.ResetFeatureDraftInstallationRequest request,
  }) async {
    try {
      return await _awaitProductUnary(
        _client.resetFeatureDraftInstallation(
          request,
          _authenticatedOptions(
            accessToken,
            timeout: featureAuthorityRequestTimeout,
          ),
        ),
      );
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<wire.FeatureDraftReply> reviseFeatureDraft({
    required String accessToken,
    required wire.ReviseFeatureDraftRequest request,
  }) async {
    try {
      return await _awaitProductUnary(
        _client.reviseFeatureDraft(
          request,
          _authenticatedOptions(accessToken, timeout: unaryRequestTimeout),
        ),
      );
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<wire.FeatureDraftPatchReply> suggestFeatureChange({
    required String accessToken,
    required wire.SuggestFeatureChangeRequest request,
  }) async {
    try {
      return await _awaitProductUnary(
        _client.suggestFeatureChange(
          request,
          _authenticatedOptions(
            accessToken,
            timeout: featureSuggestionRequestTimeout,
          ),
        ),
      );
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<wire.FeatureReleaseReviewReply> verifyFeatureDraft({
    required String accessToken,
    required wire.VerifyFeatureDraftRequest request,
  }) async {
    try {
      return await _awaitProductUnary(
        _client.verifyFeatureDraft(
          request,
          _authenticatedOptions(
            accessToken,
            timeout: featureVerificationRequestTimeout,
          ),
        ),
      );
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<wire.FeatureAccessReviewReply> reviewFeatureAccess({
    required String accessToken,
    required wire.ReviewFeatureAccessRequest request,
  }) async {
    try {
      return await _awaitProductUnary(
        _client.reviewFeatureAccess(
          request,
          _authenticatedOptions(accessToken, timeout: unaryRequestTimeout),
        ),
      );
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<wire.FeatureInstallReply> installFeatureVersion({
    required String accessToken,
    required wire.InstallFeatureVersionRequest request,
  }) async {
    try {
      return await _awaitProductUnary(
        _client.installFeatureVersion(
          request,
          _authenticatedOptions(
            accessToken,
            timeout: featureAuthorityRequestTimeout,
          ),
        ),
      );
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<wire.ResumeOriginatingRequestReply> resumeOriginatingRequest({
    required String accessToken,
    required wire.ResumeOriginatingRequestRequest request,
  }) async {
    try {
      return await _awaitProductUnary(
        _client.resumeOriginatingRequest(
          request,
          _authenticatedOptions(accessToken, timeout: unaryRequestTimeout),
        ),
      );
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<wire.FeatureReply> getFeature({
    required String accessToken,
    required wire.GetFeatureRequest request,
  }) async {
    try {
      return await _awaitProductUnary(
        _client.getFeature(
          request,
          _authenticatedOptions(accessToken, timeout: unaryRequestTimeout),
        ),
      );
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<wire.FeatureReleaseSourceReply> getFeatureReleaseSource({
    required String accessToken,
    required wire.GetFeatureReleaseSourceRequest request,
  }) async {
    try {
      return await _awaitProductUnary(
        _client.getFeatureReleaseSource(
          request,
          _authenticatedOptions(accessToken, timeout: unaryRequestTimeout),
        ),
      );
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<wire.FeatureReply> rollbackFeatureVersion({
    required String accessToken,
    required wire.RollbackFeatureVersionRequest request,
  }) async {
    try {
      return await _awaitProductUnary(
        _client.rollbackFeatureVersion(
          request,
          _authenticatedOptions(
            accessToken,
            timeout: featureAuthorityRequestTimeout,
          ),
        ),
      );
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<wire.ListActivityReply> listActivity({
    required String accessToken,
    required wire.ListActivityRequest request,
  }) async {
    try {
      return await _awaitProductUnary(
        _client.listActivity(
          request,
          _authenticatedOptions(accessToken, timeout: unaryRequestTimeout),
        ),
      );
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<wire.RunReply> getRun({
    required String accessToken,
    required wire.GetRunRequest request,
  }) async {
    try {
      return await _awaitProductUnary(
        _client.getRun(
          request,
          _authenticatedOptions(accessToken, timeout: unaryRequestTimeout),
        ),
      );
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<wire.GetConversationContextReply> getConversationContext({
    required String accessToken,
    required wire.GetConversationContextRequest request,
  }) async {
    try {
      return await _awaitProductUnary(
        _client.getConversationContext(
          request,
          _authenticatedOptions(accessToken, timeout: unaryRequestTimeout),
        ),
      );
    } catch (error) {
      throw _safeTransportError(error);
    }
  }

  @override
  Future<void> close() async {
    if (_closed) return;
    _closed = true;
    await cancelProductCalls();
    final pending = _activeUnaryResponses
        .where((response) => !_activeProductUnaryResponses.contains(response))
        .toList(growable: false);
    for (final response in pending) {
      try {
        await response.cancel();
      } catch (_) {}
    }
    await _close();
  }

  @override
  Future<void> cancelProductCalls() async {
    _productCallEpoch++;
    final pending = _activeProductUnaryResponses.toList(growable: false);
    for (final response in pending) {
      try {
        await response.cancel();
      } catch (_) {}
    }
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

  Future<T> _awaitProductUnary<T>(GrpcUnaryResponse<T> call) async {
    if (_closed) {
      await call.cancel();
      throw const TransportException(
        TransportErrorCode.cancelled,
        'UI request was cancelled.',
      );
    }
    final epoch = _productCallEpoch;
    _activeUnaryResponses.add(call);
    _activeProductUnaryResponses.add(call);
    try {
      late final T result;
      try {
        result = await call.response;
      } catch (_) {
        if (epoch != _productCallEpoch) {
          throw const TransportException(
            TransportErrorCode.cancelled,
            'UI request was cancelled.',
          );
        }
        rethrow;
      }
      if (epoch != _productCallEpoch) {
        throw const TransportException(
          TransportErrorCode.cancelled,
          'UI request was cancelled.',
        );
      }
      return result;
    } finally {
      _activeProductUnaryResponses.remove(call);
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
  GrpcUnaryResponse<wire.ListFeaturesReply> listFeatures(
    wire.ListFeaturesRequest request,
    CallOptions options,
  ) => _GeneratedGrpcUnaryResponse(
    client.listFeatures(request, options: options),
  );

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

  @override
  GrpcUnaryResponse<wire.FeatureDraftReply> getFeatureDraft(
    wire.GetFeatureDraftRequest request,
    CallOptions options,
  ) => _GeneratedGrpcUnaryResponse(
    client.getFeatureDraft(request, options: options),
  );

  @override
  GrpcUnaryResponse<wire.FeatureDraftReply> resetFeatureDraftInstallation(
    wire.ResetFeatureDraftInstallationRequest request,
    CallOptions options,
  ) => _GeneratedGrpcUnaryResponse(
    client.resetFeatureDraftInstallation(request, options: options),
  );

  @override
  GrpcUnaryResponse<wire.FeatureDraftReply> reviseFeatureDraft(
    wire.ReviseFeatureDraftRequest request,
    CallOptions options,
  ) => _GeneratedGrpcUnaryResponse(
    client.reviseFeatureDraft(request, options: options),
  );

  @override
  GrpcUnaryResponse<wire.FeatureDraftPatchReply> suggestFeatureChange(
    wire.SuggestFeatureChangeRequest request,
    CallOptions options,
  ) => _GeneratedGrpcUnaryResponse(
    client.suggestFeatureChange(request, options: options),
  );

  @override
  GrpcUnaryResponse<wire.FeatureReleaseReviewReply> verifyFeatureDraft(
    wire.VerifyFeatureDraftRequest request,
    CallOptions options,
  ) => _GeneratedGrpcUnaryResponse(
    client.verifyFeatureDraft(request, options: options),
  );

  @override
  GrpcUnaryResponse<wire.FeatureAccessReviewReply> reviewFeatureAccess(
    wire.ReviewFeatureAccessRequest request,
    CallOptions options,
  ) => _GeneratedGrpcUnaryResponse(
    client.reviewFeatureAccess(request, options: options),
  );

  @override
  GrpcUnaryResponse<wire.FeatureInstallReply> installFeatureVersion(
    wire.InstallFeatureVersionRequest request,
    CallOptions options,
  ) => _GeneratedGrpcUnaryResponse(
    client.installFeatureVersion(request, options: options),
  );

  @override
  GrpcUnaryResponse<wire.ResumeOriginatingRequestReply>
  resumeOriginatingRequest(
    wire.ResumeOriginatingRequestRequest request,
    CallOptions options,
  ) => _GeneratedGrpcUnaryResponse(
    client.resumeOriginatingRequest(request, options: options),
  );

  @override
  GrpcUnaryResponse<wire.FeatureReply> getFeature(
    wire.GetFeatureRequest request,
    CallOptions options,
  ) =>
      _GeneratedGrpcUnaryResponse(client.getFeature(request, options: options));

  @override
  GrpcUnaryResponse<wire.FeatureReleaseSourceReply> getFeatureReleaseSource(
    wire.GetFeatureReleaseSourceRequest request,
    CallOptions options,
  ) => _GeneratedGrpcUnaryResponse(
    client.getFeatureReleaseSource(request, options: options),
  );

  @override
  GrpcUnaryResponse<wire.FeatureReply> rollbackFeatureVersion(
    wire.RollbackFeatureVersionRequest request,
    CallOptions options,
  ) => _GeneratedGrpcUnaryResponse(
    client.rollbackFeatureVersion(request, options: options),
  );

  @override
  GrpcUnaryResponse<wire.ListActivityReply> listActivity(
    wire.ListActivityRequest request,
    CallOptions options,
  ) => _GeneratedGrpcUnaryResponse(
    client.listActivity(request, options: options),
  );

  @override
  GrpcUnaryResponse<wire.RunReply> getRun(
    wire.GetRunRequest request,
    CallOptions options,
  ) => _GeneratedGrpcUnaryResponse(client.getRun(request, options: options));

  @override
  GrpcUnaryResponse<wire.GetConversationContextReply> getConversationContext(
    wire.GetConversationContextRequest request,
    CallOptions options,
  ) => _GeneratedGrpcUnaryResponse(
    client.getConversationContext(request, options: options),
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
      StatusCode.notFound => const TransportException(
        TransportErrorCode.notFound,
        'Draft was not found.',
      ),
      StatusCode.aborted => const TransportException(
        TransportErrorCode.aborted,
        'Draft changed on the server.',
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
