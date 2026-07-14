import 'dart:core' as $core;

import 'package:fixnum/fixnum.dart' as $fixnum;
import 'package:protobuf/protobuf.dart' as $pb;

import 'ui.pbenum.dart';

export 'package:protobuf/protobuf.dart' show GeneratedMessageGenericExtensions;

export 'ui.pbenum.dart';

class BootstrapSessionRequest extends $pb.GeneratedMessage {
  factory BootstrapSessionRequest({$core.String? secret}) {
    final result = create();
    if (secret != null) result.secret = secret;
    return result;
  }

  BootstrapSessionRequest._();

  factory BootstrapSessionRequest.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory BootstrapSessionRequest.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'BootstrapSessionRequest',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOS(1, _omitFieldNames ? '' : 'secret')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  BootstrapSessionRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  BootstrapSessionRequest copyWith(
    void Function(BootstrapSessionRequest) updates,
  ) =>
      super.copyWith((message) => updates(message as BootstrapSessionRequest))
          as BootstrapSessionRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static BootstrapSessionRequest create() => BootstrapSessionRequest._();
  @$core.override
  BootstrapSessionRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static BootstrapSessionRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<BootstrapSessionRequest>(create);
  static BootstrapSessionRequest? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get secret => $_getSZ(0);
  @$pb.TagNumber(1)
  set secret($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasSecret() => $_has(0);
  @$pb.TagNumber(1)
  void clearSecret() => $_clearField(1);
}

class RefreshSessionRequest extends $pb.GeneratedMessage {
  factory RefreshSessionRequest({$core.String? refreshToken}) {
    final result = create();
    if (refreshToken != null) result.refreshToken = refreshToken;
    return result;
  }

  RefreshSessionRequest._();

  factory RefreshSessionRequest.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory RefreshSessionRequest.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'RefreshSessionRequest',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOS(1, _omitFieldNames ? '' : 'refreshToken')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  RefreshSessionRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  RefreshSessionRequest copyWith(
    void Function(RefreshSessionRequest) updates,
  ) =>
      super.copyWith((message) => updates(message as RefreshSessionRequest))
          as RefreshSessionRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static RefreshSessionRequest create() => RefreshSessionRequest._();
  @$core.override
  RefreshSessionRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static RefreshSessionRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<RefreshSessionRequest>(create);
  static RefreshSessionRequest? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get refreshToken => $_getSZ(0);
  @$pb.TagNumber(1)
  set refreshToken($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasRefreshToken() => $_has(0);
  @$pb.TagNumber(1)
  void clearRefreshToken() => $_clearField(1);
}

class SessionReply extends $pb.GeneratedMessage {
  factory SessionReply({
    $core.String? accessToken,
    $core.String? refreshToken,
    $fixnum.Int64? accessExpiresAtUnixMs,
    $fixnum.Int64? refreshExpiresAtUnixMs,
    $core.String? sessionId,
    $core.String? ownerId,
    $core.String? actorId,
  }) {
    final result = create();
    if (accessToken != null) result.accessToken = accessToken;
    if (refreshToken != null) result.refreshToken = refreshToken;
    if (accessExpiresAtUnixMs != null)
      result.accessExpiresAtUnixMs = accessExpiresAtUnixMs;
    if (refreshExpiresAtUnixMs != null)
      result.refreshExpiresAtUnixMs = refreshExpiresAtUnixMs;
    if (sessionId != null) result.sessionId = sessionId;
    if (ownerId != null) result.ownerId = ownerId;
    if (actorId != null) result.actorId = actorId;
    return result;
  }

  SessionReply._();

  factory SessionReply.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory SessionReply.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'SessionReply',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOS(1, _omitFieldNames ? '' : 'accessToken')
        ..aOS(2, _omitFieldNames ? '' : 'refreshToken')
        ..aInt64(3, _omitFieldNames ? '' : 'accessExpiresAtUnixMs')
        ..aInt64(4, _omitFieldNames ? '' : 'refreshExpiresAtUnixMs')
        ..aOS(5, _omitFieldNames ? '' : 'sessionId')
        ..aOS(6, _omitFieldNames ? '' : 'ownerId')
        ..aOS(7, _omitFieldNames ? '' : 'actorId')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SessionReply clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SessionReply copyWith(void Function(SessionReply) updates) =>
      super.copyWith((message) => updates(message as SessionReply))
          as SessionReply;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static SessionReply create() => SessionReply._();
  @$core.override
  SessionReply createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static SessionReply getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<SessionReply>(create);
  static SessionReply? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get accessToken => $_getSZ(0);
  @$pb.TagNumber(1)
  set accessToken($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasAccessToken() => $_has(0);
  @$pb.TagNumber(1)
  void clearAccessToken() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get refreshToken => $_getSZ(1);
  @$pb.TagNumber(2)
  set refreshToken($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasRefreshToken() => $_has(1);
  @$pb.TagNumber(2)
  void clearRefreshToken() => $_clearField(2);

  @$pb.TagNumber(3)
  $fixnum.Int64 get accessExpiresAtUnixMs => $_getI64(2);
  @$pb.TagNumber(3)
  set accessExpiresAtUnixMs($fixnum.Int64 value) => $_setInt64(2, value);
  @$pb.TagNumber(3)
  $core.bool hasAccessExpiresAtUnixMs() => $_has(2);
  @$pb.TagNumber(3)
  void clearAccessExpiresAtUnixMs() => $_clearField(3);

  @$pb.TagNumber(4)
  $fixnum.Int64 get refreshExpiresAtUnixMs => $_getI64(3);
  @$pb.TagNumber(4)
  set refreshExpiresAtUnixMs($fixnum.Int64 value) => $_setInt64(3, value);
  @$pb.TagNumber(4)
  $core.bool hasRefreshExpiresAtUnixMs() => $_has(3);
  @$pb.TagNumber(4)
  void clearRefreshExpiresAtUnixMs() => $_clearField(4);

  @$pb.TagNumber(5)
  $core.String get sessionId => $_getSZ(4);
  @$pb.TagNumber(5)
  set sessionId($core.String value) => $_setString(4, value);
  @$pb.TagNumber(5)
  $core.bool hasSessionId() => $_has(4);
  @$pb.TagNumber(5)
  void clearSessionId() => $_clearField(5);

  @$pb.TagNumber(6)
  $core.String get ownerId => $_getSZ(5);
  @$pb.TagNumber(6)
  set ownerId($core.String value) => $_setString(5, value);
  @$pb.TagNumber(6)
  $core.bool hasOwnerId() => $_has(5);
  @$pb.TagNumber(6)
  void clearOwnerId() => $_clearField(6);

  @$pb.TagNumber(7)
  $core.String get actorId => $_getSZ(6);
  @$pb.TagNumber(7)
  set actorId($core.String value) => $_setString(6, value);
  @$pb.TagNumber(7)
  $core.bool hasActorId() => $_has(6);
  @$pb.TagNumber(7)
  void clearActorId() => $_clearField(7);
}

class WatchSurfaceFeedRequest extends $pb.GeneratedMessage {
  factory WatchSurfaceFeedRequest({
    $fixnum.Int64? afterSequence,
    FeedAudienceKind? audience,
    $core.Iterable<$core.String>? clientCapabilities,
    $core.int? maxBatchSize,
  }) {
    final result = create();
    if (afterSequence != null) result.afterSequence = afterSequence;
    if (audience != null) result.audience = audience;
    if (clientCapabilities != null)
      result.clientCapabilities.addAll(clientCapabilities);
    if (maxBatchSize != null) result.maxBatchSize = maxBatchSize;
    return result;
  }

  WatchSurfaceFeedRequest._();

  factory WatchSurfaceFeedRequest.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory WatchSurfaceFeedRequest.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'WatchSurfaceFeedRequest',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aInt64(1, _omitFieldNames ? '' : 'afterSequence')
        ..aE<FeedAudienceKind>(
          2,
          _omitFieldNames ? '' : 'audience',
          enumValues: FeedAudienceKind.values,
        )
        ..pPS(3, _omitFieldNames ? '' : 'clientCapabilities')
        ..aI(4, _omitFieldNames ? '' : 'maxBatchSize')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  WatchSurfaceFeedRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  WatchSurfaceFeedRequest copyWith(
    void Function(WatchSurfaceFeedRequest) updates,
  ) =>
      super.copyWith((message) => updates(message as WatchSurfaceFeedRequest))
          as WatchSurfaceFeedRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static WatchSurfaceFeedRequest create() => WatchSurfaceFeedRequest._();
  @$core.override
  WatchSurfaceFeedRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static WatchSurfaceFeedRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<WatchSurfaceFeedRequest>(create);
  static WatchSurfaceFeedRequest? _defaultInstance;

  @$pb.TagNumber(1)
  $fixnum.Int64 get afterSequence => $_getI64(0);
  @$pb.TagNumber(1)
  set afterSequence($fixnum.Int64 value) => $_setInt64(0, value);
  @$pb.TagNumber(1)
  $core.bool hasAfterSequence() => $_has(0);
  @$pb.TagNumber(1)
  void clearAfterSequence() => $_clearField(1);

  @$pb.TagNumber(2)
  FeedAudienceKind get audience => $_getN(1);
  @$pb.TagNumber(2)
  set audience(FeedAudienceKind value) => $_setField(2, value);
  @$pb.TagNumber(2)
  $core.bool hasAudience() => $_has(1);
  @$pb.TagNumber(2)
  void clearAudience() => $_clearField(2);

  @$pb.TagNumber(3)
  $pb.PbList<$core.String> get clientCapabilities => $_getList(2);

  @$pb.TagNumber(4)
  $core.int get maxBatchSize => $_getIZ(3);
  @$pb.TagNumber(4)
  set maxBatchSize($core.int value) => $_setSignedInt32(3, value);
  @$pb.TagNumber(4)
  $core.bool hasMaxBatchSize() => $_has(3);
  @$pb.TagNumber(4)
  void clearMaxBatchSize() => $_clearField(4);
}

enum SurfaceFeedEvent_Event { surfaceJson, reset, notSet }

class SurfaceFeedEvent extends $pb.GeneratedMessage {
  factory SurfaceFeedEvent({
    $core.String? surfaceJson,
    SurfaceFeedReset? reset,
  }) {
    final result = create();
    if (surfaceJson != null) result.surfaceJson = surfaceJson;
    if (reset != null) result.reset = reset;
    return result;
  }

  SurfaceFeedEvent._();

  factory SurfaceFeedEvent.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory SurfaceFeedEvent.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static const $core.Map<$core.int, SurfaceFeedEvent_Event>
  _SurfaceFeedEvent_EventByTag = {
    1: SurfaceFeedEvent_Event.surfaceJson,
    2: SurfaceFeedEvent_Event.reset,
    0: SurfaceFeedEvent_Event.notSet,
  };
  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'SurfaceFeedEvent',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..oo(0, [1, 2])
        ..aOS(1, _omitFieldNames ? '' : 'surfaceJson')
        ..aOM<SurfaceFeedReset>(
          2,
          _omitFieldNames ? '' : 'reset',
          subBuilder: SurfaceFeedReset.create,
        )
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SurfaceFeedEvent clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SurfaceFeedEvent copyWith(void Function(SurfaceFeedEvent) updates) =>
      super.copyWith((message) => updates(message as SurfaceFeedEvent))
          as SurfaceFeedEvent;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static SurfaceFeedEvent create() => SurfaceFeedEvent._();
  @$core.override
  SurfaceFeedEvent createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static SurfaceFeedEvent getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<SurfaceFeedEvent>(create);
  static SurfaceFeedEvent? _defaultInstance;

  @$pb.TagNumber(1)
  @$pb.TagNumber(2)
  SurfaceFeedEvent_Event whichEvent() =>
      _SurfaceFeedEvent_EventByTag[$_whichOneof(0)]!;
  @$pb.TagNumber(1)
  @$pb.TagNumber(2)
  void clearEvent() => $_clearField($_whichOneof(0));

  @$pb.TagNumber(1)
  $core.String get surfaceJson => $_getSZ(0);
  @$pb.TagNumber(1)
  set surfaceJson($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasSurfaceJson() => $_has(0);
  @$pb.TagNumber(1)
  void clearSurfaceJson() => $_clearField(1);

  @$pb.TagNumber(2)
  SurfaceFeedReset get reset => $_getN(1);
  @$pb.TagNumber(2)
  set reset(SurfaceFeedReset value) => $_setField(2, value);
  @$pb.TagNumber(2)
  $core.bool hasReset() => $_has(1);
  @$pb.TagNumber(2)
  void clearReset() => $_clearField(2);
  @$pb.TagNumber(2)
  SurfaceFeedReset ensureReset() => $_ensure(1);
}

class SurfaceFeedReset extends $pb.GeneratedMessage {
  factory SurfaceFeedReset({
    $core.String? reason,
    $fixnum.Int64? resumeSequence,
    $core.Iterable<$core.String>? snapshotJson,
  }) {
    final result = create();
    if (reason != null) result.reason = reason;
    if (resumeSequence != null) result.resumeSequence = resumeSequence;
    if (snapshotJson != null) result.snapshotJson.addAll(snapshotJson);
    return result;
  }

  SurfaceFeedReset._();

  factory SurfaceFeedReset.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory SurfaceFeedReset.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'SurfaceFeedReset',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOS(1, _omitFieldNames ? '' : 'reason')
        ..aInt64(2, _omitFieldNames ? '' : 'resumeSequence')
        ..pPS(3, _omitFieldNames ? '' : 'snapshotJson')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SurfaceFeedReset clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SurfaceFeedReset copyWith(void Function(SurfaceFeedReset) updates) =>
      super.copyWith((message) => updates(message as SurfaceFeedReset))
          as SurfaceFeedReset;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static SurfaceFeedReset create() => SurfaceFeedReset._();
  @$core.override
  SurfaceFeedReset createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static SurfaceFeedReset getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<SurfaceFeedReset>(create);
  static SurfaceFeedReset? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get reason => $_getSZ(0);
  @$pb.TagNumber(1)
  set reason($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasReason() => $_has(0);
  @$pb.TagNumber(1)
  void clearReason() => $_clearField(1);

  @$pb.TagNumber(2)
  $fixnum.Int64 get resumeSequence => $_getI64(1);
  @$pb.TagNumber(2)
  set resumeSequence($fixnum.Int64 value) => $_setInt64(1, value);
  @$pb.TagNumber(2)
  $core.bool hasResumeSequence() => $_has(1);
  @$pb.TagNumber(2)
  void clearResumeSequence() => $_clearField(2);

  @$pb.TagNumber(3)
  $pb.PbList<$core.String> get snapshotJson => $_getList(2);
}

class AcknowledgeSurfaceFeedRequest extends $pb.GeneratedMessage {
  factory AcknowledgeSurfaceFeedRequest({
    FeedAudienceKind? audience,
    $fixnum.Int64? sequence,
  }) {
    final result = create();
    if (audience != null) result.audience = audience;
    if (sequence != null) result.sequence = sequence;
    return result;
  }

  AcknowledgeSurfaceFeedRequest._();

  factory AcknowledgeSurfaceFeedRequest.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory AcknowledgeSurfaceFeedRequest.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'AcknowledgeSurfaceFeedRequest',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aE<FeedAudienceKind>(
          1,
          _omitFieldNames ? '' : 'audience',
          enumValues: FeedAudienceKind.values,
        )
        ..aInt64(2, _omitFieldNames ? '' : 'sequence')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  AcknowledgeSurfaceFeedRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  AcknowledgeSurfaceFeedRequest copyWith(
    void Function(AcknowledgeSurfaceFeedRequest) updates,
  ) =>
      super.copyWith(
            (message) => updates(message as AcknowledgeSurfaceFeedRequest),
          )
          as AcknowledgeSurfaceFeedRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static AcknowledgeSurfaceFeedRequest create() =>
      AcknowledgeSurfaceFeedRequest._();
  @$core.override
  AcknowledgeSurfaceFeedRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static AcknowledgeSurfaceFeedRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<AcknowledgeSurfaceFeedRequest>(create);
  static AcknowledgeSurfaceFeedRequest? _defaultInstance;

  @$pb.TagNumber(1)
  FeedAudienceKind get audience => $_getN(0);
  @$pb.TagNumber(1)
  set audience(FeedAudienceKind value) => $_setField(1, value);
  @$pb.TagNumber(1)
  $core.bool hasAudience() => $_has(0);
  @$pb.TagNumber(1)
  void clearAudience() => $_clearField(1);

  @$pb.TagNumber(2)
  $fixnum.Int64 get sequence => $_getI64(1);
  @$pb.TagNumber(2)
  set sequence($fixnum.Int64 value) => $_setInt64(1, value);
  @$pb.TagNumber(2)
  $core.bool hasSequence() => $_has(1);
  @$pb.TagNumber(2)
  void clearSequence() => $_clearField(2);
}

class AcknowledgeSurfaceFeedReply extends $pb.GeneratedMessage {
  factory AcknowledgeSurfaceFeedReply({$fixnum.Int64? acknowledgedSequence}) {
    final result = create();
    if (acknowledgedSequence != null)
      result.acknowledgedSequence = acknowledgedSequence;
    return result;
  }

  AcknowledgeSurfaceFeedReply._();

  factory AcknowledgeSurfaceFeedReply.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory AcknowledgeSurfaceFeedReply.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'AcknowledgeSurfaceFeedReply',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aInt64(1, _omitFieldNames ? '' : 'acknowledgedSequence')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  AcknowledgeSurfaceFeedReply clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  AcknowledgeSurfaceFeedReply copyWith(
    void Function(AcknowledgeSurfaceFeedReply) updates,
  ) =>
      super.copyWith(
            (message) => updates(message as AcknowledgeSurfaceFeedReply),
          )
          as AcknowledgeSurfaceFeedReply;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static AcknowledgeSurfaceFeedReply create() =>
      AcknowledgeSurfaceFeedReply._();
  @$core.override
  AcknowledgeSurfaceFeedReply createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static AcknowledgeSurfaceFeedReply getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<AcknowledgeSurfaceFeedReply>(create);
  static AcknowledgeSurfaceFeedReply? _defaultInstance;

  @$pb.TagNumber(1)
  $fixnum.Int64 get acknowledgedSequence => $_getI64(0);
  @$pb.TagNumber(1)
  set acknowledgedSequence($fixnum.Int64 value) => $_setInt64(0, value);
  @$pb.TagNumber(1)
  $core.bool hasAcknowledgedSequence() => $_has(0);
  @$pb.TagNumber(1)
  void clearAcknowledgedSequence() => $_clearField(1);
}

class SubmitActionRequest extends $pb.GeneratedMessage {
  factory SubmitActionRequest({
    $core.String? bindingId,
    $core.String? actionToken,
    $core.String? surfaceId,
    $core.int? surfaceRevision,
    $core.String? inputJson,
  }) {
    final result = create();
    if (bindingId != null) result.bindingId = bindingId;
    if (actionToken != null) result.actionToken = actionToken;
    if (surfaceId != null) result.surfaceId = surfaceId;
    if (surfaceRevision != null) result.surfaceRevision = surfaceRevision;
    if (inputJson != null) result.inputJson = inputJson;
    return result;
  }

  SubmitActionRequest._();

  factory SubmitActionRequest.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory SubmitActionRequest.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'SubmitActionRequest',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOS(1, _omitFieldNames ? '' : 'bindingId')
        ..aOS(2, _omitFieldNames ? '' : 'actionToken')
        ..aOS(3, _omitFieldNames ? '' : 'surfaceId')
        ..aI(4, _omitFieldNames ? '' : 'surfaceRevision')
        ..aOS(5, _omitFieldNames ? '' : 'inputJson')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SubmitActionRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SubmitActionRequest copyWith(void Function(SubmitActionRequest) updates) =>
      super.copyWith((message) => updates(message as SubmitActionRequest))
          as SubmitActionRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static SubmitActionRequest create() => SubmitActionRequest._();
  @$core.override
  SubmitActionRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static SubmitActionRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<SubmitActionRequest>(create);
  static SubmitActionRequest? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get bindingId => $_getSZ(0);
  @$pb.TagNumber(1)
  set bindingId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasBindingId() => $_has(0);
  @$pb.TagNumber(1)
  void clearBindingId() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get actionToken => $_getSZ(1);
  @$pb.TagNumber(2)
  set actionToken($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasActionToken() => $_has(1);
  @$pb.TagNumber(2)
  void clearActionToken() => $_clearField(2);

  @$pb.TagNumber(3)
  $core.String get surfaceId => $_getSZ(2);
  @$pb.TagNumber(3)
  set surfaceId($core.String value) => $_setString(2, value);
  @$pb.TagNumber(3)
  $core.bool hasSurfaceId() => $_has(2);
  @$pb.TagNumber(3)
  void clearSurfaceId() => $_clearField(3);

  @$pb.TagNumber(4)
  $core.int get surfaceRevision => $_getIZ(3);
  @$pb.TagNumber(4)
  set surfaceRevision($core.int value) => $_setSignedInt32(3, value);
  @$pb.TagNumber(4)
  $core.bool hasSurfaceRevision() => $_has(3);
  @$pb.TagNumber(4)
  void clearSurfaceRevision() => $_clearField(4);

  @$pb.TagNumber(5)
  $core.String get inputJson => $_getSZ(4);
  @$pb.TagNumber(5)
  set inputJson($core.String value) => $_setString(4, value);
  @$pb.TagNumber(5)
  $core.bool hasInputJson() => $_has(4);
  @$pb.TagNumber(5)
  void clearInputJson() => $_clearField(5);
}

class SubmitActionReply extends $pb.GeneratedMessage {
  factory SubmitActionReply({
    $core.String? operationId,
    $core.String? idempotencyKey,
  }) {
    final result = create();
    if (operationId != null) result.operationId = operationId;
    if (idempotencyKey != null) result.idempotencyKey = idempotencyKey;
    return result;
  }

  SubmitActionReply._();

  factory SubmitActionReply.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory SubmitActionReply.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'SubmitActionReply',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOS(1, _omitFieldNames ? '' : 'operationId')
        ..aOS(2, _omitFieldNames ? '' : 'idempotencyKey')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SubmitActionReply clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SubmitActionReply copyWith(void Function(SubmitActionReply) updates) =>
      super.copyWith((message) => updates(message as SubmitActionReply))
          as SubmitActionReply;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static SubmitActionReply create() => SubmitActionReply._();
  @$core.override
  SubmitActionReply createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static SubmitActionReply getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<SubmitActionReply>(create);
  static SubmitActionReply? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get operationId => $_getSZ(0);
  @$pb.TagNumber(1)
  set operationId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasOperationId() => $_has(0);
  @$pb.TagNumber(1)
  void clearOperationId() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get idempotencyKey => $_getSZ(1);
  @$pb.TagNumber(2)
  set idempotencyKey($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasIdempotencyKey() => $_has(1);
  @$pb.TagNumber(2)
  void clearIdempotencyKey() => $_clearField(2);
}

class LogoutSessionRequest extends $pb.GeneratedMessage {
  factory LogoutSessionRequest({$core.String? refreshToken}) {
    final result = create();
    if (refreshToken != null) result.refreshToken = refreshToken;
    return result;
  }

  LogoutSessionRequest._();

  factory LogoutSessionRequest.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory LogoutSessionRequest.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'LogoutSessionRequest',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOS(1, _omitFieldNames ? '' : 'refreshToken')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  LogoutSessionRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  LogoutSessionRequest copyWith(void Function(LogoutSessionRequest) updates) =>
      super.copyWith((message) => updates(message as LogoutSessionRequest))
          as LogoutSessionRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static LogoutSessionRequest create() => LogoutSessionRequest._();
  @$core.override
  LogoutSessionRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static LogoutSessionRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<LogoutSessionRequest>(create);
  static LogoutSessionRequest? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get refreshToken => $_getSZ(0);
  @$pb.TagNumber(1)
  set refreshToken($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasRefreshToken() => $_has(0);
  @$pb.TagNumber(1)
  void clearRefreshToken() => $_clearField(1);
}

class LogoutSessionReply extends $pb.GeneratedMessage {
  factory LogoutSessionReply() => create();

  LogoutSessionReply._();

  factory LogoutSessionReply.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory LogoutSessionReply.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
    _omitMessageNames ? '' : 'LogoutSessionReply',
    package: const $pb.PackageName(
      _omitMessageNames ? '' : 'digitalbrain.v2.ui',
    ),
    createEmptyInstance: create,
  )..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  LogoutSessionReply clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  LogoutSessionReply copyWith(void Function(LogoutSessionReply) updates) =>
      super.copyWith((message) => updates(message as LogoutSessionReply))
          as LogoutSessionReply;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static LogoutSessionReply create() => LogoutSessionReply._();
  @$core.override
  LogoutSessionReply createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static LogoutSessionReply getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<LogoutSessionReply>(create);
  static LogoutSessionReply? _defaultInstance;
}

const $core.bool _omitFieldNames = $core.bool.fromEnvironment(
  'protobuf.omit_field_names',
);
const $core.bool _omitMessageNames = $core.bool.fromEnvironment(
  'protobuf.omit_message_names',
);
