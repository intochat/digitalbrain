import 'dart:core' as $core;

import 'package:fixnum/fixnum.dart' as $fixnum;
import 'package:protobuf/protobuf.dart' as $pb;

import 'ui.pbenum.dart';

export 'package:protobuf/protobuf.dart' show GeneratedMessageGenericExtensions;

export 'ui.pbenum.dart';

class BootstrapSessionRequest extends $pb.GeneratedMessage {
  factory BootstrapSessionRequest({
    $core.String? username,
    $core.String? password,
  }) {
    final result = create();
    if (username != null) result.username = username;
    if (password != null) result.password = password;
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
        ..aOS(2, _omitFieldNames ? '' : 'username')
        ..aOS(3, _omitFieldNames ? '' : 'password')
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

  @$pb.TagNumber(2)
  $core.String get username => $_getSZ(0);
  @$pb.TagNumber(2)
  set username($core.String value) => $_setString(0, value);
  @$pb.TagNumber(2)
  $core.bool hasUsername() => $_has(0);
  @$pb.TagNumber(2)
  void clearUsername() => $_clearField(2);

  @$pb.TagNumber(3)
  $core.String get password => $_getSZ(1);
  @$pb.TagNumber(3)
  set password($core.String value) => $_setString(1, value);
  @$pb.TagNumber(3)
  $core.bool hasPassword() => $_has(1);
  @$pb.TagNumber(3)
  void clearPassword() => $_clearField(3);
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

class GetFeatureDraftRequest extends $pb.GeneratedMessage {
  factory GetFeatureDraftRequest({$core.String? draftId}) {
    final result = create();
    if (draftId != null) result.draftId = draftId;
    return result;
  }

  GetFeatureDraftRequest._();

  factory GetFeatureDraftRequest.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory GetFeatureDraftRequest.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'GetFeatureDraftRequest',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOS(3, _omitFieldNames ? '' : 'draftId')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  GetFeatureDraftRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  GetFeatureDraftRequest copyWith(
    void Function(GetFeatureDraftRequest) updates,
  ) =>
      super.copyWith((message) => updates(message as GetFeatureDraftRequest))
          as GetFeatureDraftRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static GetFeatureDraftRequest create() => GetFeatureDraftRequest._();
  @$core.override
  GetFeatureDraftRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static GetFeatureDraftRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<GetFeatureDraftRequest>(create);
  static GetFeatureDraftRequest? _defaultInstance;

  @$pb.TagNumber(3)
  $core.String get draftId => $_getSZ(0);
  @$pb.TagNumber(3)
  set draftId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(3)
  $core.bool hasDraftId() => $_has(0);
  @$pb.TagNumber(3)
  void clearDraftId() => $_clearField(3);
}

enum ReviseFeatureDraftRequest_Command {
  reviseBehavior,
  reviseSource,
  acceptSuggestedChange,
  rejectSuggestedChange,
  notSet,
}

class ReviseFeatureDraftRequest extends $pb.GeneratedMessage {
  factory ReviseFeatureDraftRequest({
    $core.String? draftId,
    $fixnum.Int64? expectedRevision,
    $core.String? idempotencyId,
    ReviseFeatureBehaviorInput? reviseBehavior,
    ReviseFeatureSourceInput? reviseSource,
    AcceptSuggestedChangeInput? acceptSuggestedChange,
    RejectSuggestedChangeInput? rejectSuggestedChange,
  }) {
    final result = create();
    if (draftId != null) result.draftId = draftId;
    if (expectedRevision != null) result.expectedRevision = expectedRevision;
    if (idempotencyId != null) result.idempotencyId = idempotencyId;
    if (reviseBehavior != null) result.reviseBehavior = reviseBehavior;
    if (reviseSource != null) result.reviseSource = reviseSource;
    if (acceptSuggestedChange != null)
      result.acceptSuggestedChange = acceptSuggestedChange;
    if (rejectSuggestedChange != null)
      result.rejectSuggestedChange = rejectSuggestedChange;
    return result;
  }

  ReviseFeatureDraftRequest._();

  factory ReviseFeatureDraftRequest.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory ReviseFeatureDraftRequest.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static const $core.Map<$core.int, ReviseFeatureDraftRequest_Command>
  _ReviseFeatureDraftRequest_CommandByTag = {
    6: ReviseFeatureDraftRequest_Command.reviseBehavior,
    7: ReviseFeatureDraftRequest_Command.reviseSource,
    8: ReviseFeatureDraftRequest_Command.acceptSuggestedChange,
    9: ReviseFeatureDraftRequest_Command.rejectSuggestedChange,
    0: ReviseFeatureDraftRequest_Command.notSet,
  };
  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'ReviseFeatureDraftRequest',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..oo(0, [6, 7, 8, 9])
        ..aOS(3, _omitFieldNames ? '' : 'draftId')
        ..aInt64(4, _omitFieldNames ? '' : 'expectedRevision')
        ..aOS(5, _omitFieldNames ? '' : 'idempotencyId')
        ..aOM<ReviseFeatureBehaviorInput>(
          6,
          _omitFieldNames ? '' : 'reviseBehavior',
          subBuilder: ReviseFeatureBehaviorInput.create,
        )
        ..aOM<ReviseFeatureSourceInput>(
          7,
          _omitFieldNames ? '' : 'reviseSource',
          subBuilder: ReviseFeatureSourceInput.create,
        )
        ..aOM<AcceptSuggestedChangeInput>(
          8,
          _omitFieldNames ? '' : 'acceptSuggestedChange',
          subBuilder: AcceptSuggestedChangeInput.create,
        )
        ..aOM<RejectSuggestedChangeInput>(
          9,
          _omitFieldNames ? '' : 'rejectSuggestedChange',
          subBuilder: RejectSuggestedChangeInput.create,
        )
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ReviseFeatureDraftRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ReviseFeatureDraftRequest copyWith(
    void Function(ReviseFeatureDraftRequest) updates,
  ) =>
      super.copyWith((message) => updates(message as ReviseFeatureDraftRequest))
          as ReviseFeatureDraftRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static ReviseFeatureDraftRequest create() => ReviseFeatureDraftRequest._();
  @$core.override
  ReviseFeatureDraftRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static ReviseFeatureDraftRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<ReviseFeatureDraftRequest>(create);
  static ReviseFeatureDraftRequest? _defaultInstance;

  @$pb.TagNumber(6)
  @$pb.TagNumber(7)
  @$pb.TagNumber(8)
  @$pb.TagNumber(9)
  ReviseFeatureDraftRequest_Command whichCommand() =>
      _ReviseFeatureDraftRequest_CommandByTag[$_whichOneof(0)]!;
  @$pb.TagNumber(6)
  @$pb.TagNumber(7)
  @$pb.TagNumber(8)
  @$pb.TagNumber(9)
  void clearCommand() => $_clearField($_whichOneof(0));

  @$pb.TagNumber(3)
  $core.String get draftId => $_getSZ(0);
  @$pb.TagNumber(3)
  set draftId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(3)
  $core.bool hasDraftId() => $_has(0);
  @$pb.TagNumber(3)
  void clearDraftId() => $_clearField(3);

  @$pb.TagNumber(4)
  $fixnum.Int64 get expectedRevision => $_getI64(1);
  @$pb.TagNumber(4)
  set expectedRevision($fixnum.Int64 value) => $_setInt64(1, value);
  @$pb.TagNumber(4)
  $core.bool hasExpectedRevision() => $_has(1);
  @$pb.TagNumber(4)
  void clearExpectedRevision() => $_clearField(4);

  @$pb.TagNumber(5)
  $core.String get idempotencyId => $_getSZ(2);
  @$pb.TagNumber(5)
  set idempotencyId($core.String value) => $_setString(2, value);
  @$pb.TagNumber(5)
  $core.bool hasIdempotencyId() => $_has(2);
  @$pb.TagNumber(5)
  void clearIdempotencyId() => $_clearField(5);

  @$pb.TagNumber(6)
  ReviseFeatureBehaviorInput get reviseBehavior => $_getN(3);
  @$pb.TagNumber(6)
  set reviseBehavior(ReviseFeatureBehaviorInput value) => $_setField(6, value);
  @$pb.TagNumber(6)
  $core.bool hasReviseBehavior() => $_has(3);
  @$pb.TagNumber(6)
  void clearReviseBehavior() => $_clearField(6);
  @$pb.TagNumber(6)
  ReviseFeatureBehaviorInput ensureReviseBehavior() => $_ensure(3);

  @$pb.TagNumber(7)
  ReviseFeatureSourceInput get reviseSource => $_getN(4);
  @$pb.TagNumber(7)
  set reviseSource(ReviseFeatureSourceInput value) => $_setField(7, value);
  @$pb.TagNumber(7)
  $core.bool hasReviseSource() => $_has(4);
  @$pb.TagNumber(7)
  void clearReviseSource() => $_clearField(7);
  @$pb.TagNumber(7)
  ReviseFeatureSourceInput ensureReviseSource() => $_ensure(4);

  @$pb.TagNumber(8)
  AcceptSuggestedChangeInput get acceptSuggestedChange => $_getN(5);
  @$pb.TagNumber(8)
  set acceptSuggestedChange(AcceptSuggestedChangeInput value) =>
      $_setField(8, value);
  @$pb.TagNumber(8)
  $core.bool hasAcceptSuggestedChange() => $_has(5);
  @$pb.TagNumber(8)
  void clearAcceptSuggestedChange() => $_clearField(8);
  @$pb.TagNumber(8)
  AcceptSuggestedChangeInput ensureAcceptSuggestedChange() => $_ensure(5);

  @$pb.TagNumber(9)
  RejectSuggestedChangeInput get rejectSuggestedChange => $_getN(6);
  @$pb.TagNumber(9)
  set rejectSuggestedChange(RejectSuggestedChangeInput value) =>
      $_setField(9, value);
  @$pb.TagNumber(9)
  $core.bool hasRejectSuggestedChange() => $_has(6);
  @$pb.TagNumber(9)
  void clearRejectSuggestedChange() => $_clearField(9);
  @$pb.TagNumber(9)
  RejectSuggestedChangeInput ensureRejectSuggestedChange() => $_ensure(6);
}

class SuggestFeatureChangeRequest extends $pb.GeneratedMessage {
  factory SuggestFeatureChangeRequest({
    $core.String? draftId,
    $fixnum.Int64? expectedRevision,
    $core.String? guidance,
    $core.String? suggestionId,
  }) {
    final result = create();
    if (draftId != null) result.draftId = draftId;
    if (expectedRevision != null) result.expectedRevision = expectedRevision;
    if (guidance != null) result.guidance = guidance;
    if (suggestionId != null) result.suggestionId = suggestionId;
    return result;
  }

  SuggestFeatureChangeRequest._();

  factory SuggestFeatureChangeRequest.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory SuggestFeatureChangeRequest.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'SuggestFeatureChangeRequest',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOS(3, _omitFieldNames ? '' : 'draftId')
        ..aInt64(4, _omitFieldNames ? '' : 'expectedRevision')
        ..aOS(5, _omitFieldNames ? '' : 'guidance')
        ..aOS(6, _omitFieldNames ? '' : 'suggestionId')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SuggestFeatureChangeRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SuggestFeatureChangeRequest copyWith(
    void Function(SuggestFeatureChangeRequest) updates,
  ) =>
      super.copyWith(
            (message) => updates(message as SuggestFeatureChangeRequest),
          )
          as SuggestFeatureChangeRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static SuggestFeatureChangeRequest create() =>
      SuggestFeatureChangeRequest._();
  @$core.override
  SuggestFeatureChangeRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static SuggestFeatureChangeRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<SuggestFeatureChangeRequest>(create);
  static SuggestFeatureChangeRequest? _defaultInstance;

  @$pb.TagNumber(3)
  $core.String get draftId => $_getSZ(0);
  @$pb.TagNumber(3)
  set draftId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(3)
  $core.bool hasDraftId() => $_has(0);
  @$pb.TagNumber(3)
  void clearDraftId() => $_clearField(3);

  @$pb.TagNumber(4)
  $fixnum.Int64 get expectedRevision => $_getI64(1);
  @$pb.TagNumber(4)
  set expectedRevision($fixnum.Int64 value) => $_setInt64(1, value);
  @$pb.TagNumber(4)
  $core.bool hasExpectedRevision() => $_has(1);
  @$pb.TagNumber(4)
  void clearExpectedRevision() => $_clearField(4);

  @$pb.TagNumber(5)
  $core.String get guidance => $_getSZ(2);
  @$pb.TagNumber(5)
  set guidance($core.String value) => $_setString(2, value);
  @$pb.TagNumber(5)
  $core.bool hasGuidance() => $_has(2);
  @$pb.TagNumber(5)
  void clearGuidance() => $_clearField(5);

  @$pb.TagNumber(6)
  $core.String get suggestionId => $_getSZ(3);
  @$pb.TagNumber(6)
  set suggestionId($core.String value) => $_setString(3, value);
  @$pb.TagNumber(6)
  $core.bool hasSuggestionId() => $_has(3);
  @$pb.TagNumber(6)
  void clearSuggestionId() => $_clearField(6);
}

class VerifyFeatureDraftRequest extends $pb.GeneratedMessage {
  factory VerifyFeatureDraftRequest({
    $core.String? draftId,
    $fixnum.Int64? expectedRevision,
    $core.String? idempotencyId,
  }) {
    final result = create();
    if (draftId != null) result.draftId = draftId;
    if (expectedRevision != null) result.expectedRevision = expectedRevision;
    if (idempotencyId != null) result.idempotencyId = idempotencyId;
    return result;
  }

  VerifyFeatureDraftRequest._();

  factory VerifyFeatureDraftRequest.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory VerifyFeatureDraftRequest.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'VerifyFeatureDraftRequest',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOS(3, _omitFieldNames ? '' : 'draftId')
        ..aInt64(4, _omitFieldNames ? '' : 'expectedRevision')
        ..aOS(5, _omitFieldNames ? '' : 'idempotencyId')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  VerifyFeatureDraftRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  VerifyFeatureDraftRequest copyWith(
    void Function(VerifyFeatureDraftRequest) updates,
  ) =>
      super.copyWith((message) => updates(message as VerifyFeatureDraftRequest))
          as VerifyFeatureDraftRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static VerifyFeatureDraftRequest create() => VerifyFeatureDraftRequest._();
  @$core.override
  VerifyFeatureDraftRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static VerifyFeatureDraftRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<VerifyFeatureDraftRequest>(create);
  static VerifyFeatureDraftRequest? _defaultInstance;

  @$pb.TagNumber(3)
  $core.String get draftId => $_getSZ(0);
  @$pb.TagNumber(3)
  set draftId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(3)
  $core.bool hasDraftId() => $_has(0);
  @$pb.TagNumber(3)
  void clearDraftId() => $_clearField(3);

  @$pb.TagNumber(4)
  $fixnum.Int64 get expectedRevision => $_getI64(1);
  @$pb.TagNumber(4)
  set expectedRevision($fixnum.Int64 value) => $_setInt64(1, value);
  @$pb.TagNumber(4)
  $core.bool hasExpectedRevision() => $_has(1);
  @$pb.TagNumber(4)
  void clearExpectedRevision() => $_clearField(4);

  @$pb.TagNumber(5)
  $core.String get idempotencyId => $_getSZ(2);
  @$pb.TagNumber(5)
  set idempotencyId($core.String value) => $_setString(2, value);
  @$pb.TagNumber(5)
  $core.bool hasIdempotencyId() => $_has(2);
  @$pb.TagNumber(5)
  void clearIdempotencyId() => $_clearField(5);
}

class InstallFeatureVersionRequest extends $pb.GeneratedMessage {
  factory InstallFeatureVersionRequest({
    $core.String? draftId,
    $fixnum.Int64? expectedRevision,
    $core.String? installationId,
    $core.String? releaseDigest,
    $core.Iterable<FeatureGrant>? grants,
    $core.Iterable<$core.String>? subscriptions,
    $core.String? decisionId,
    $core.String? idempotencyId,
  }) {
    final result = create();
    if (draftId != null) result.draftId = draftId;
    if (expectedRevision != null) result.expectedRevision = expectedRevision;
    if (installationId != null) result.installationId = installationId;
    if (releaseDigest != null) result.releaseDigest = releaseDigest;
    if (grants != null) result.grants.addAll(grants);
    if (subscriptions != null) result.subscriptions.addAll(subscriptions);
    if (decisionId != null) result.decisionId = decisionId;
    if (idempotencyId != null) result.idempotencyId = idempotencyId;
    return result;
  }

  InstallFeatureVersionRequest._();

  factory InstallFeatureVersionRequest.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory InstallFeatureVersionRequest.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'InstallFeatureVersionRequest',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOS(3, _omitFieldNames ? '' : 'draftId')
        ..aInt64(4, _omitFieldNames ? '' : 'expectedRevision')
        ..aOS(5, _omitFieldNames ? '' : 'installationId')
        ..aOS(6, _omitFieldNames ? '' : 'releaseDigest')
        ..pPM<FeatureGrant>(
          7,
          _omitFieldNames ? '' : 'grants',
          subBuilder: FeatureGrant.create,
        )
        ..pPS(8, _omitFieldNames ? '' : 'subscriptions')
        ..aOS(9, _omitFieldNames ? '' : 'decisionId')
        ..aOS(10, _omitFieldNames ? '' : 'idempotencyId')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  InstallFeatureVersionRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  InstallFeatureVersionRequest copyWith(
    void Function(InstallFeatureVersionRequest) updates,
  ) =>
      super.copyWith(
            (message) => updates(message as InstallFeatureVersionRequest),
          )
          as InstallFeatureVersionRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static InstallFeatureVersionRequest create() =>
      InstallFeatureVersionRequest._();
  @$core.override
  InstallFeatureVersionRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static InstallFeatureVersionRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<InstallFeatureVersionRequest>(create);
  static InstallFeatureVersionRequest? _defaultInstance;

  @$pb.TagNumber(3)
  $core.String get draftId => $_getSZ(0);
  @$pb.TagNumber(3)
  set draftId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(3)
  $core.bool hasDraftId() => $_has(0);
  @$pb.TagNumber(3)
  void clearDraftId() => $_clearField(3);

  @$pb.TagNumber(4)
  $fixnum.Int64 get expectedRevision => $_getI64(1);
  @$pb.TagNumber(4)
  set expectedRevision($fixnum.Int64 value) => $_setInt64(1, value);
  @$pb.TagNumber(4)
  $core.bool hasExpectedRevision() => $_has(1);
  @$pb.TagNumber(4)
  void clearExpectedRevision() => $_clearField(4);

  @$pb.TagNumber(5)
  $core.String get installationId => $_getSZ(2);
  @$pb.TagNumber(5)
  set installationId($core.String value) => $_setString(2, value);
  @$pb.TagNumber(5)
  $core.bool hasInstallationId() => $_has(2);
  @$pb.TagNumber(5)
  void clearInstallationId() => $_clearField(5);

  @$pb.TagNumber(6)
  $core.String get releaseDigest => $_getSZ(3);
  @$pb.TagNumber(6)
  set releaseDigest($core.String value) => $_setString(3, value);
  @$pb.TagNumber(6)
  $core.bool hasReleaseDigest() => $_has(3);
  @$pb.TagNumber(6)
  void clearReleaseDigest() => $_clearField(6);

  @$pb.TagNumber(7)
  $pb.PbList<FeatureGrant> get grants => $_getList(4);

  @$pb.TagNumber(8)
  $pb.PbList<$core.String> get subscriptions => $_getList(5);

  @$pb.TagNumber(9)
  $core.String get decisionId => $_getSZ(6);
  @$pb.TagNumber(9)
  set decisionId($core.String value) => $_setString(6, value);
  @$pb.TagNumber(9)
  $core.bool hasDecisionId() => $_has(6);
  @$pb.TagNumber(9)
  void clearDecisionId() => $_clearField(9);

  @$pb.TagNumber(10)
  $core.String get idempotencyId => $_getSZ(7);
  @$pb.TagNumber(10)
  set idempotencyId($core.String value) => $_setString(7, value);
  @$pb.TagNumber(10)
  $core.bool hasIdempotencyId() => $_has(7);
  @$pb.TagNumber(10)
  void clearIdempotencyId() => $_clearField(10);
}

class ReviseFeatureBehaviorInput extends $pb.GeneratedMessage {
  factory ReviseFeatureBehaviorInput({FeatureBehavior? behavior}) {
    final result = create();
    if (behavior != null) result.behavior = behavior;
    return result;
  }

  ReviseFeatureBehaviorInput._();

  factory ReviseFeatureBehaviorInput.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory ReviseFeatureBehaviorInput.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'ReviseFeatureBehaviorInput',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOM<FeatureBehavior>(
          1,
          _omitFieldNames ? '' : 'behavior',
          subBuilder: FeatureBehavior.create,
        )
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ReviseFeatureBehaviorInput clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ReviseFeatureBehaviorInput copyWith(
    void Function(ReviseFeatureBehaviorInput) updates,
  ) =>
      super.copyWith(
            (message) => updates(message as ReviseFeatureBehaviorInput),
          )
          as ReviseFeatureBehaviorInput;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static ReviseFeatureBehaviorInput create() => ReviseFeatureBehaviorInput._();
  @$core.override
  ReviseFeatureBehaviorInput createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static ReviseFeatureBehaviorInput getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<ReviseFeatureBehaviorInput>(create);
  static ReviseFeatureBehaviorInput? _defaultInstance;

  @$pb.TagNumber(1)
  FeatureBehavior get behavior => $_getN(0);
  @$pb.TagNumber(1)
  set behavior(FeatureBehavior value) => $_setField(1, value);
  @$pb.TagNumber(1)
  $core.bool hasBehavior() => $_has(0);
  @$pb.TagNumber(1)
  void clearBehavior() => $_clearField(1);
  @$pb.TagNumber(1)
  FeatureBehavior ensureBehavior() => $_ensure(0);
}

class ReviseFeatureSourceInput extends $pb.GeneratedMessage {
  factory ReviseFeatureSourceInput({FeatureSourceSnapshot? source}) {
    final result = create();
    if (source != null) result.source = source;
    return result;
  }

  ReviseFeatureSourceInput._();

  factory ReviseFeatureSourceInput.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory ReviseFeatureSourceInput.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'ReviseFeatureSourceInput',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOM<FeatureSourceSnapshot>(
          1,
          _omitFieldNames ? '' : 'source',
          subBuilder: FeatureSourceSnapshot.create,
        )
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ReviseFeatureSourceInput clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ReviseFeatureSourceInput copyWith(
    void Function(ReviseFeatureSourceInput) updates,
  ) =>
      super.copyWith((message) => updates(message as ReviseFeatureSourceInput))
          as ReviseFeatureSourceInput;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static ReviseFeatureSourceInput create() => ReviseFeatureSourceInput._();
  @$core.override
  ReviseFeatureSourceInput createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static ReviseFeatureSourceInput getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<ReviseFeatureSourceInput>(create);
  static ReviseFeatureSourceInput? _defaultInstance;

  @$pb.TagNumber(1)
  FeatureSourceSnapshot get source => $_getN(0);
  @$pb.TagNumber(1)
  set source(FeatureSourceSnapshot value) => $_setField(1, value);
  @$pb.TagNumber(1)
  $core.bool hasSource() => $_has(0);
  @$pb.TagNumber(1)
  void clearSource() => $_clearField(1);
  @$pb.TagNumber(1)
  FeatureSourceSnapshot ensureSource() => $_ensure(0);
}

class AcceptSuggestedChangeInput extends $pb.GeneratedMessage {
  factory AcceptSuggestedChangeInput({FeatureDraftPatch? patch}) {
    final result = create();
    if (patch != null) result.patch = patch;
    return result;
  }

  AcceptSuggestedChangeInput._();

  factory AcceptSuggestedChangeInput.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory AcceptSuggestedChangeInput.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'AcceptSuggestedChangeInput',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOM<FeatureDraftPatch>(
          1,
          _omitFieldNames ? '' : 'patch',
          subBuilder: FeatureDraftPatch.create,
        )
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  AcceptSuggestedChangeInput clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  AcceptSuggestedChangeInput copyWith(
    void Function(AcceptSuggestedChangeInput) updates,
  ) =>
      super.copyWith(
            (message) => updates(message as AcceptSuggestedChangeInput),
          )
          as AcceptSuggestedChangeInput;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static AcceptSuggestedChangeInput create() => AcceptSuggestedChangeInput._();
  @$core.override
  AcceptSuggestedChangeInput createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static AcceptSuggestedChangeInput getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<AcceptSuggestedChangeInput>(create);
  static AcceptSuggestedChangeInput? _defaultInstance;

  @$pb.TagNumber(1)
  FeatureDraftPatch get patch => $_getN(0);
  @$pb.TagNumber(1)
  set patch(FeatureDraftPatch value) => $_setField(1, value);
  @$pb.TagNumber(1)
  $core.bool hasPatch() => $_has(0);
  @$pb.TagNumber(1)
  void clearPatch() => $_clearField(1);
  @$pb.TagNumber(1)
  FeatureDraftPatch ensurePatch() => $_ensure(0);
}

class RejectSuggestedChangeInput extends $pb.GeneratedMessage {
  factory RejectSuggestedChangeInput({
    $core.String? patchId,
    $fixnum.Int64? baseRevision,
  }) {
    final result = create();
    if (patchId != null) result.patchId = patchId;
    if (baseRevision != null) result.baseRevision = baseRevision;
    return result;
  }

  RejectSuggestedChangeInput._();

  factory RejectSuggestedChangeInput.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory RejectSuggestedChangeInput.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'RejectSuggestedChangeInput',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOS(1, _omitFieldNames ? '' : 'patchId')
        ..aInt64(2, _omitFieldNames ? '' : 'baseRevision')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  RejectSuggestedChangeInput clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  RejectSuggestedChangeInput copyWith(
    void Function(RejectSuggestedChangeInput) updates,
  ) =>
      super.copyWith(
            (message) => updates(message as RejectSuggestedChangeInput),
          )
          as RejectSuggestedChangeInput;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static RejectSuggestedChangeInput create() => RejectSuggestedChangeInput._();
  @$core.override
  RejectSuggestedChangeInput createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static RejectSuggestedChangeInput getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<RejectSuggestedChangeInput>(create);
  static RejectSuggestedChangeInput? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get patchId => $_getSZ(0);
  @$pb.TagNumber(1)
  set patchId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasPatchId() => $_has(0);
  @$pb.TagNumber(1)
  void clearPatchId() => $_clearField(1);

  @$pb.TagNumber(2)
  $fixnum.Int64 get baseRevision => $_getI64(1);
  @$pb.TagNumber(2)
  set baseRevision($fixnum.Int64 value) => $_setInt64(1, value);
  @$pb.TagNumber(2)
  $core.bool hasBaseRevision() => $_has(1);
  @$pb.TagNumber(2)
  void clearBaseRevision() => $_clearField(2);
}

class OriginatingRequest extends $pb.GeneratedMessage {
  factory OriginatingRequest({
    $core.String? operationId,
    $core.String? conversationId,
    $core.String? text,
  }) {
    final result = create();
    if (operationId != null) result.operationId = operationId;
    if (conversationId != null) result.conversationId = conversationId;
    if (text != null) result.text = text;
    return result;
  }

  OriginatingRequest._();

  factory OriginatingRequest.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory OriginatingRequest.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'OriginatingRequest',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOS(1, _omitFieldNames ? '' : 'operationId')
        ..aOS(2, _omitFieldNames ? '' : 'conversationId')
        ..aOS(3, _omitFieldNames ? '' : 'text')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  OriginatingRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  OriginatingRequest copyWith(void Function(OriginatingRequest) updates) =>
      super.copyWith((message) => updates(message as OriginatingRequest))
          as OriginatingRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static OriginatingRequest create() => OriginatingRequest._();
  @$core.override
  OriginatingRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static OriginatingRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<OriginatingRequest>(create);
  static OriginatingRequest? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get operationId => $_getSZ(0);
  @$pb.TagNumber(1)
  set operationId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasOperationId() => $_has(0);
  @$pb.TagNumber(1)
  void clearOperationId() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get conversationId => $_getSZ(1);
  @$pb.TagNumber(2)
  set conversationId($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasConversationId() => $_has(1);
  @$pb.TagNumber(2)
  void clearConversationId() => $_clearField(2);

  @$pb.TagNumber(3)
  $core.String get text => $_getSZ(2);
  @$pb.TagNumber(3)
  set text($core.String value) => $_setString(2, value);
  @$pb.TagNumber(3)
  $core.bool hasText() => $_has(2);
  @$pb.TagNumber(3)
  void clearText() => $_clearField(3);
}

class FeatureScenario extends $pb.GeneratedMessage {
  factory FeatureScenario({
    $core.String? scenarioId,
    $core.String? name,
    $core.String? given,
    $core.String? when,
    $core.String? then,
  }) {
    final result = create();
    if (scenarioId != null) result.scenarioId = scenarioId;
    if (name != null) result.name = name;
    if (given != null) result.given = given;
    if (when != null) result.when = when;
    if (then != null) result.then = then;
    return result;
  }

  FeatureScenario._();

  factory FeatureScenario.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory FeatureScenario.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'FeatureScenario',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOS(1, _omitFieldNames ? '' : 'scenarioId')
        ..aOS(2, _omitFieldNames ? '' : 'name')
        ..aOS(3, _omitFieldNames ? '' : 'given')
        ..aOS(4, _omitFieldNames ? '' : 'when')
        ..aOS(5, _omitFieldNames ? '' : 'then')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureScenario clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureScenario copyWith(void Function(FeatureScenario) updates) =>
      super.copyWith((message) => updates(message as FeatureScenario))
          as FeatureScenario;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static FeatureScenario create() => FeatureScenario._();
  @$core.override
  FeatureScenario createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static FeatureScenario getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<FeatureScenario>(create);
  static FeatureScenario? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get scenarioId => $_getSZ(0);
  @$pb.TagNumber(1)
  set scenarioId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasScenarioId() => $_has(0);
  @$pb.TagNumber(1)
  void clearScenarioId() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get name => $_getSZ(1);
  @$pb.TagNumber(2)
  set name($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasName() => $_has(1);
  @$pb.TagNumber(2)
  void clearName() => $_clearField(2);

  @$pb.TagNumber(3)
  $core.String get given => $_getSZ(2);
  @$pb.TagNumber(3)
  set given($core.String value) => $_setString(2, value);
  @$pb.TagNumber(3)
  $core.bool hasGiven() => $_has(2);
  @$pb.TagNumber(3)
  void clearGiven() => $_clearField(3);

  @$pb.TagNumber(4)
  $core.String get when => $_getSZ(3);
  @$pb.TagNumber(4)
  set when($core.String value) => $_setString(3, value);
  @$pb.TagNumber(4)
  $core.bool hasWhen() => $_has(3);
  @$pb.TagNumber(4)
  void clearWhen() => $_clearField(4);

  @$pb.TagNumber(5)
  $core.String get then => $_getSZ(4);
  @$pb.TagNumber(5)
  set then($core.String value) => $_setString(4, value);
  @$pb.TagNumber(5)
  $core.bool hasThen() => $_has(4);
  @$pb.TagNumber(5)
  void clearThen() => $_clearField(5);
}

class FeatureBehavior extends $pb.GeneratedMessage {
  factory FeatureBehavior({$core.Iterable<FeatureScenario>? scenarios}) {
    final result = create();
    if (scenarios != null) result.scenarios.addAll(scenarios);
    return result;
  }

  FeatureBehavior._();

  factory FeatureBehavior.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory FeatureBehavior.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'FeatureBehavior',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..pPM<FeatureScenario>(
          1,
          _omitFieldNames ? '' : 'scenarios',
          subBuilder: FeatureScenario.create,
        )
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureBehavior clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureBehavior copyWith(void Function(FeatureBehavior) updates) =>
      super.copyWith((message) => updates(message as FeatureBehavior))
          as FeatureBehavior;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static FeatureBehavior create() => FeatureBehavior._();
  @$core.override
  FeatureBehavior createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static FeatureBehavior getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<FeatureBehavior>(create);
  static FeatureBehavior? _defaultInstance;

  @$pb.TagNumber(1)
  $pb.PbList<FeatureScenario> get scenarios => $_getList(0);
}

class FeatureSourceFile extends $pb.GeneratedMessage {
  factory FeatureSourceFile({$core.String? path, $core.String? content}) {
    final result = create();
    if (path != null) result.path = path;
    if (content != null) result.content = content;
    return result;
  }

  FeatureSourceFile._();

  factory FeatureSourceFile.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory FeatureSourceFile.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'FeatureSourceFile',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOS(1, _omitFieldNames ? '' : 'path')
        ..aOS(2, _omitFieldNames ? '' : 'content')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureSourceFile clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureSourceFile copyWith(void Function(FeatureSourceFile) updates) =>
      super.copyWith((message) => updates(message as FeatureSourceFile))
          as FeatureSourceFile;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static FeatureSourceFile create() => FeatureSourceFile._();
  @$core.override
  FeatureSourceFile createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static FeatureSourceFile getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<FeatureSourceFile>(create);
  static FeatureSourceFile? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get path => $_getSZ(0);
  @$pb.TagNumber(1)
  set path($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasPath() => $_has(0);
  @$pb.TagNumber(1)
  void clearPath() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get content => $_getSZ(1);
  @$pb.TagNumber(2)
  set content($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasContent() => $_has(1);
  @$pb.TagNumber(2)
  void clearContent() => $_clearField(2);
}

class FeatureSourceSnapshot extends $pb.GeneratedMessage {
  factory FeatureSourceSnapshot({
    $core.String? implementationProjectPath,
    $core.String? scenarioProjectPath,
    $core.Iterable<FeatureSourceFile>? files,
  }) {
    final result = create();
    if (implementationProjectPath != null)
      result.implementationProjectPath = implementationProjectPath;
    if (scenarioProjectPath != null)
      result.scenarioProjectPath = scenarioProjectPath;
    if (files != null) result.files.addAll(files);
    return result;
  }

  FeatureSourceSnapshot._();

  factory FeatureSourceSnapshot.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory FeatureSourceSnapshot.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'FeatureSourceSnapshot',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOS(1, _omitFieldNames ? '' : 'implementationProjectPath')
        ..aOS(2, _omitFieldNames ? '' : 'scenarioProjectPath')
        ..pPM<FeatureSourceFile>(
          3,
          _omitFieldNames ? '' : 'files',
          subBuilder: FeatureSourceFile.create,
        )
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureSourceSnapshot clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureSourceSnapshot copyWith(
    void Function(FeatureSourceSnapshot) updates,
  ) =>
      super.copyWith((message) => updates(message as FeatureSourceSnapshot))
          as FeatureSourceSnapshot;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static FeatureSourceSnapshot create() => FeatureSourceSnapshot._();
  @$core.override
  FeatureSourceSnapshot createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static FeatureSourceSnapshot getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<FeatureSourceSnapshot>(create);
  static FeatureSourceSnapshot? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get implementationProjectPath => $_getSZ(0);
  @$pb.TagNumber(1)
  set implementationProjectPath($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasImplementationProjectPath() => $_has(0);
  @$pb.TagNumber(1)
  void clearImplementationProjectPath() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get scenarioProjectPath => $_getSZ(1);
  @$pb.TagNumber(2)
  set scenarioProjectPath($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasScenarioProjectPath() => $_has(1);
  @$pb.TagNumber(2)
  void clearScenarioProjectPath() => $_clearField(2);

  @$pb.TagNumber(3)
  $pb.PbList<FeatureSourceFile> get files => $_getList(2);
}

class FeatureVerification extends $pb.GeneratedMessage {
  factory FeatureVerification({
    $core.String? releaseDigest,
    $core.int? total,
    $core.int? passed,
    $core.int? failed,
    $core.int? skipped,
    $fixnum.Int64? verifiedAtUnixMs,
  }) {
    final result = create();
    if (releaseDigest != null) result.releaseDigest = releaseDigest;
    if (total != null) result.total = total;
    if (passed != null) result.passed = passed;
    if (failed != null) result.failed = failed;
    if (skipped != null) result.skipped = skipped;
    if (verifiedAtUnixMs != null) result.verifiedAtUnixMs = verifiedAtUnixMs;
    return result;
  }

  FeatureVerification._();

  factory FeatureVerification.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory FeatureVerification.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'FeatureVerification',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOS(1, _omitFieldNames ? '' : 'releaseDigest')
        ..aI(2, _omitFieldNames ? '' : 'total')
        ..aI(3, _omitFieldNames ? '' : 'passed')
        ..aI(4, _omitFieldNames ? '' : 'failed')
        ..aI(5, _omitFieldNames ? '' : 'skipped')
        ..aInt64(6, _omitFieldNames ? '' : 'verifiedAtUnixMs')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureVerification clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureVerification copyWith(void Function(FeatureVerification) updates) =>
      super.copyWith((message) => updates(message as FeatureVerification))
          as FeatureVerification;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static FeatureVerification create() => FeatureVerification._();
  @$core.override
  FeatureVerification createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static FeatureVerification getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<FeatureVerification>(create);
  static FeatureVerification? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get releaseDigest => $_getSZ(0);
  @$pb.TagNumber(1)
  set releaseDigest($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasReleaseDigest() => $_has(0);
  @$pb.TagNumber(1)
  void clearReleaseDigest() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.int get total => $_getIZ(1);
  @$pb.TagNumber(2)
  set total($core.int value) => $_setSignedInt32(1, value);
  @$pb.TagNumber(2)
  $core.bool hasTotal() => $_has(1);
  @$pb.TagNumber(2)
  void clearTotal() => $_clearField(2);

  @$pb.TagNumber(3)
  $core.int get passed => $_getIZ(2);
  @$pb.TagNumber(3)
  set passed($core.int value) => $_setSignedInt32(2, value);
  @$pb.TagNumber(3)
  $core.bool hasPassed() => $_has(2);
  @$pb.TagNumber(3)
  void clearPassed() => $_clearField(3);

  @$pb.TagNumber(4)
  $core.int get failed => $_getIZ(3);
  @$pb.TagNumber(4)
  set failed($core.int value) => $_setSignedInt32(3, value);
  @$pb.TagNumber(4)
  $core.bool hasFailed() => $_has(3);
  @$pb.TagNumber(4)
  void clearFailed() => $_clearField(4);

  @$pb.TagNumber(5)
  $core.int get skipped => $_getIZ(4);
  @$pb.TagNumber(5)
  set skipped($core.int value) => $_setSignedInt32(4, value);
  @$pb.TagNumber(5)
  $core.bool hasSkipped() => $_has(4);
  @$pb.TagNumber(5)
  void clearSkipped() => $_clearField(5);

  @$pb.TagNumber(6)
  $fixnum.Int64 get verifiedAtUnixMs => $_getI64(5);
  @$pb.TagNumber(6)
  set verifiedAtUnixMs($fixnum.Int64 value) => $_setInt64(5, value);
  @$pb.TagNumber(6)
  $core.bool hasVerifiedAtUnixMs() => $_has(5);
  @$pb.TagNumber(6)
  void clearVerifiedAtUnixMs() => $_clearField(6);
}

class FeatureDraft extends $pb.GeneratedMessage {
  factory FeatureDraft({
    $core.String? draftId,
    OriginatingRequest? originatingRequest,
    $core.String? goal,
    FeatureDraftStatus? status,
    FeatureBehavior? behavior,
    FeatureSourceSnapshot? source,
    FeatureVerification? verification,
    $core.String? installationId,
    $fixnum.Int64? revision,
    $fixnum.Int64? createdAtUnixMs,
    $fixnum.Int64? updatedAtUnixMs,
  }) {
    final result = create();
    if (draftId != null) result.draftId = draftId;
    if (originatingRequest != null)
      result.originatingRequest = originatingRequest;
    if (goal != null) result.goal = goal;
    if (status != null) result.status = status;
    if (behavior != null) result.behavior = behavior;
    if (source != null) result.source = source;
    if (verification != null) result.verification = verification;
    if (installationId != null) result.installationId = installationId;
    if (revision != null) result.revision = revision;
    if (createdAtUnixMs != null) result.createdAtUnixMs = createdAtUnixMs;
    if (updatedAtUnixMs != null) result.updatedAtUnixMs = updatedAtUnixMs;
    return result;
  }

  FeatureDraft._();

  factory FeatureDraft.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory FeatureDraft.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'FeatureDraft',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOS(1, _omitFieldNames ? '' : 'draftId')
        ..aOM<OriginatingRequest>(
          2,
          _omitFieldNames ? '' : 'originatingRequest',
          subBuilder: OriginatingRequest.create,
        )
        ..aOS(3, _omitFieldNames ? '' : 'goal')
        ..aE<FeatureDraftStatus>(
          4,
          _omitFieldNames ? '' : 'status',
          enumValues: FeatureDraftStatus.values,
        )
        ..aOM<FeatureBehavior>(
          5,
          _omitFieldNames ? '' : 'behavior',
          subBuilder: FeatureBehavior.create,
        )
        ..aOM<FeatureSourceSnapshot>(
          6,
          _omitFieldNames ? '' : 'source',
          subBuilder: FeatureSourceSnapshot.create,
        )
        ..aOM<FeatureVerification>(
          7,
          _omitFieldNames ? '' : 'verification',
          subBuilder: FeatureVerification.create,
        )
        ..aOS(8, _omitFieldNames ? '' : 'installationId')
        ..aInt64(9, _omitFieldNames ? '' : 'revision')
        ..aInt64(10, _omitFieldNames ? '' : 'createdAtUnixMs')
        ..aInt64(11, _omitFieldNames ? '' : 'updatedAtUnixMs')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureDraft clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureDraft copyWith(void Function(FeatureDraft) updates) =>
      super.copyWith((message) => updates(message as FeatureDraft))
          as FeatureDraft;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static FeatureDraft create() => FeatureDraft._();
  @$core.override
  FeatureDraft createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static FeatureDraft getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<FeatureDraft>(create);
  static FeatureDraft? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get draftId => $_getSZ(0);
  @$pb.TagNumber(1)
  set draftId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasDraftId() => $_has(0);
  @$pb.TagNumber(1)
  void clearDraftId() => $_clearField(1);

  @$pb.TagNumber(2)
  OriginatingRequest get originatingRequest => $_getN(1);
  @$pb.TagNumber(2)
  set originatingRequest(OriginatingRequest value) => $_setField(2, value);
  @$pb.TagNumber(2)
  $core.bool hasOriginatingRequest() => $_has(1);
  @$pb.TagNumber(2)
  void clearOriginatingRequest() => $_clearField(2);
  @$pb.TagNumber(2)
  OriginatingRequest ensureOriginatingRequest() => $_ensure(1);

  @$pb.TagNumber(3)
  $core.String get goal => $_getSZ(2);
  @$pb.TagNumber(3)
  set goal($core.String value) => $_setString(2, value);
  @$pb.TagNumber(3)
  $core.bool hasGoal() => $_has(2);
  @$pb.TagNumber(3)
  void clearGoal() => $_clearField(3);

  @$pb.TagNumber(4)
  FeatureDraftStatus get status => $_getN(3);
  @$pb.TagNumber(4)
  set status(FeatureDraftStatus value) => $_setField(4, value);
  @$pb.TagNumber(4)
  $core.bool hasStatus() => $_has(3);
  @$pb.TagNumber(4)
  void clearStatus() => $_clearField(4);

  @$pb.TagNumber(5)
  FeatureBehavior get behavior => $_getN(4);
  @$pb.TagNumber(5)
  set behavior(FeatureBehavior value) => $_setField(5, value);
  @$pb.TagNumber(5)
  $core.bool hasBehavior() => $_has(4);
  @$pb.TagNumber(5)
  void clearBehavior() => $_clearField(5);
  @$pb.TagNumber(5)
  FeatureBehavior ensureBehavior() => $_ensure(4);

  @$pb.TagNumber(6)
  FeatureSourceSnapshot get source => $_getN(5);
  @$pb.TagNumber(6)
  set source(FeatureSourceSnapshot value) => $_setField(6, value);
  @$pb.TagNumber(6)
  $core.bool hasSource() => $_has(5);
  @$pb.TagNumber(6)
  void clearSource() => $_clearField(6);
  @$pb.TagNumber(6)
  FeatureSourceSnapshot ensureSource() => $_ensure(5);

  @$pb.TagNumber(7)
  FeatureVerification get verification => $_getN(6);
  @$pb.TagNumber(7)
  set verification(FeatureVerification value) => $_setField(7, value);
  @$pb.TagNumber(7)
  $core.bool hasVerification() => $_has(6);
  @$pb.TagNumber(7)
  void clearVerification() => $_clearField(7);
  @$pb.TagNumber(7)
  FeatureVerification ensureVerification() => $_ensure(6);

  @$pb.TagNumber(8)
  $core.String get installationId => $_getSZ(7);
  @$pb.TagNumber(8)
  set installationId($core.String value) => $_setString(7, value);
  @$pb.TagNumber(8)
  $core.bool hasInstallationId() => $_has(7);
  @$pb.TagNumber(8)
  void clearInstallationId() => $_clearField(8);

  @$pb.TagNumber(9)
  $fixnum.Int64 get revision => $_getI64(8);
  @$pb.TagNumber(9)
  set revision($fixnum.Int64 value) => $_setInt64(8, value);
  @$pb.TagNumber(9)
  $core.bool hasRevision() => $_has(8);
  @$pb.TagNumber(9)
  void clearRevision() => $_clearField(9);

  @$pb.TagNumber(10)
  $fixnum.Int64 get createdAtUnixMs => $_getI64(9);
  @$pb.TagNumber(10)
  set createdAtUnixMs($fixnum.Int64 value) => $_setInt64(9, value);
  @$pb.TagNumber(10)
  $core.bool hasCreatedAtUnixMs() => $_has(9);
  @$pb.TagNumber(10)
  void clearCreatedAtUnixMs() => $_clearField(10);

  @$pb.TagNumber(11)
  $fixnum.Int64 get updatedAtUnixMs => $_getI64(10);
  @$pb.TagNumber(11)
  set updatedAtUnixMs($fixnum.Int64 value) => $_setInt64(10, value);
  @$pb.TagNumber(11)
  $core.bool hasUpdatedAtUnixMs() => $_has(10);
  @$pb.TagNumber(11)
  void clearUpdatedAtUnixMs() => $_clearField(11);
}

class FeatureDraftPatch extends $pb.GeneratedMessage {
  factory FeatureDraftPatch({
    $core.String? patchId,
    $core.String? draftId,
    $fixnum.Int64? baseRevision,
    $core.String? summary,
    FeatureBehavior? replacementBehavior,
    FeatureSourceSnapshot? replacementSource,
  }) {
    final result = create();
    if (patchId != null) result.patchId = patchId;
    if (draftId != null) result.draftId = draftId;
    if (baseRevision != null) result.baseRevision = baseRevision;
    if (summary != null) result.summary = summary;
    if (replacementBehavior != null)
      result.replacementBehavior = replacementBehavior;
    if (replacementSource != null) result.replacementSource = replacementSource;
    return result;
  }

  FeatureDraftPatch._();

  factory FeatureDraftPatch.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory FeatureDraftPatch.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'FeatureDraftPatch',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOS(1, _omitFieldNames ? '' : 'patchId')
        ..aOS(2, _omitFieldNames ? '' : 'draftId')
        ..aInt64(3, _omitFieldNames ? '' : 'baseRevision')
        ..aOS(4, _omitFieldNames ? '' : 'summary')
        ..aOM<FeatureBehavior>(
          5,
          _omitFieldNames ? '' : 'replacementBehavior',
          subBuilder: FeatureBehavior.create,
        )
        ..aOM<FeatureSourceSnapshot>(
          6,
          _omitFieldNames ? '' : 'replacementSource',
          subBuilder: FeatureSourceSnapshot.create,
        )
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureDraftPatch clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureDraftPatch copyWith(void Function(FeatureDraftPatch) updates) =>
      super.copyWith((message) => updates(message as FeatureDraftPatch))
          as FeatureDraftPatch;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static FeatureDraftPatch create() => FeatureDraftPatch._();
  @$core.override
  FeatureDraftPatch createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static FeatureDraftPatch getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<FeatureDraftPatch>(create);
  static FeatureDraftPatch? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get patchId => $_getSZ(0);
  @$pb.TagNumber(1)
  set patchId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasPatchId() => $_has(0);
  @$pb.TagNumber(1)
  void clearPatchId() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get draftId => $_getSZ(1);
  @$pb.TagNumber(2)
  set draftId($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasDraftId() => $_has(1);
  @$pb.TagNumber(2)
  void clearDraftId() => $_clearField(2);

  @$pb.TagNumber(3)
  $fixnum.Int64 get baseRevision => $_getI64(2);
  @$pb.TagNumber(3)
  set baseRevision($fixnum.Int64 value) => $_setInt64(2, value);
  @$pb.TagNumber(3)
  $core.bool hasBaseRevision() => $_has(2);
  @$pb.TagNumber(3)
  void clearBaseRevision() => $_clearField(3);

  @$pb.TagNumber(4)
  $core.String get summary => $_getSZ(3);
  @$pb.TagNumber(4)
  set summary($core.String value) => $_setString(3, value);
  @$pb.TagNumber(4)
  $core.bool hasSummary() => $_has(3);
  @$pb.TagNumber(4)
  void clearSummary() => $_clearField(4);

  @$pb.TagNumber(5)
  FeatureBehavior get replacementBehavior => $_getN(4);
  @$pb.TagNumber(5)
  set replacementBehavior(FeatureBehavior value) => $_setField(5, value);
  @$pb.TagNumber(5)
  $core.bool hasReplacementBehavior() => $_has(4);
  @$pb.TagNumber(5)
  void clearReplacementBehavior() => $_clearField(5);
  @$pb.TagNumber(5)
  FeatureBehavior ensureReplacementBehavior() => $_ensure(4);

  @$pb.TagNumber(6)
  FeatureSourceSnapshot get replacementSource => $_getN(5);
  @$pb.TagNumber(6)
  set replacementSource(FeatureSourceSnapshot value) => $_setField(6, value);
  @$pb.TagNumber(6)
  $core.bool hasReplacementSource() => $_has(5);
  @$pb.TagNumber(6)
  void clearReplacementSource() => $_clearField(6);
  @$pb.TagNumber(6)
  FeatureSourceSnapshot ensureReplacementSource() => $_ensure(5);
}

class FeatureRelease extends $pb.GeneratedMessage {
  factory FeatureRelease({
    $core.String? digest,
    FeatureSourceKind? sourceKind,
    $core.Iterable<$core.String>? requestedCapabilityIds,
    $core.Iterable<$core.String>? dependencies,
  }) {
    final result = create();
    if (digest != null) result.digest = digest;
    if (sourceKind != null) result.sourceKind = sourceKind;
    if (requestedCapabilityIds != null)
      result.requestedCapabilityIds.addAll(requestedCapabilityIds);
    if (dependencies != null) result.dependencies.addAll(dependencies);
    return result;
  }

  FeatureRelease._();

  factory FeatureRelease.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory FeatureRelease.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'FeatureRelease',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOS(1, _omitFieldNames ? '' : 'digest')
        ..aE<FeatureSourceKind>(
          2,
          _omitFieldNames ? '' : 'sourceKind',
          enumValues: FeatureSourceKind.values,
        )
        ..pPS(3, _omitFieldNames ? '' : 'requestedCapabilityIds')
        ..pPS(4, _omitFieldNames ? '' : 'dependencies')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureRelease clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureRelease copyWith(void Function(FeatureRelease) updates) =>
      super.copyWith((message) => updates(message as FeatureRelease))
          as FeatureRelease;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static FeatureRelease create() => FeatureRelease._();
  @$core.override
  FeatureRelease createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static FeatureRelease getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<FeatureRelease>(create);
  static FeatureRelease? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get digest => $_getSZ(0);
  @$pb.TagNumber(1)
  set digest($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasDigest() => $_has(0);
  @$pb.TagNumber(1)
  void clearDigest() => $_clearField(1);

  @$pb.TagNumber(2)
  FeatureSourceKind get sourceKind => $_getN(1);
  @$pb.TagNumber(2)
  set sourceKind(FeatureSourceKind value) => $_setField(2, value);
  @$pb.TagNumber(2)
  $core.bool hasSourceKind() => $_has(1);
  @$pb.TagNumber(2)
  void clearSourceKind() => $_clearField(2);

  @$pb.TagNumber(3)
  $pb.PbList<$core.String> get requestedCapabilityIds => $_getList(2);

  @$pb.TagNumber(4)
  $pb.PbList<$core.String> get dependencies => $_getList(3);
}

class FeatureGrant extends $pb.GeneratedMessage {
  factory FeatureGrant({
    $core.String? capabilityId,
    $core.int? capabilityVersion,
    $core.String? connectionId,
    $core.String? constraintsJson,
    $core.String? provider,
  }) {
    final result = create();
    if (capabilityId != null) result.capabilityId = capabilityId;
    if (capabilityVersion != null) result.capabilityVersion = capabilityVersion;
    if (connectionId != null) result.connectionId = connectionId;
    if (constraintsJson != null) result.constraintsJson = constraintsJson;
    if (provider != null) result.provider = provider;
    return result;
  }

  FeatureGrant._();

  factory FeatureGrant.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory FeatureGrant.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'FeatureGrant',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOS(1, _omitFieldNames ? '' : 'capabilityId')
        ..aI(2, _omitFieldNames ? '' : 'capabilityVersion')
        ..aOS(3, _omitFieldNames ? '' : 'connectionId')
        ..aOS(4, _omitFieldNames ? '' : 'constraintsJson')
        ..aOS(5, _omitFieldNames ? '' : 'provider')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureGrant clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureGrant copyWith(void Function(FeatureGrant) updates) =>
      super.copyWith((message) => updates(message as FeatureGrant))
          as FeatureGrant;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static FeatureGrant create() => FeatureGrant._();
  @$core.override
  FeatureGrant createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static FeatureGrant getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<FeatureGrant>(create);
  static FeatureGrant? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get capabilityId => $_getSZ(0);
  @$pb.TagNumber(1)
  set capabilityId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasCapabilityId() => $_has(0);
  @$pb.TagNumber(1)
  void clearCapabilityId() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.int get capabilityVersion => $_getIZ(1);
  @$pb.TagNumber(2)
  set capabilityVersion($core.int value) => $_setSignedInt32(1, value);
  @$pb.TagNumber(2)
  $core.bool hasCapabilityVersion() => $_has(1);
  @$pb.TagNumber(2)
  void clearCapabilityVersion() => $_clearField(2);

  @$pb.TagNumber(3)
  $core.String get connectionId => $_getSZ(2);
  @$pb.TagNumber(3)
  set connectionId($core.String value) => $_setString(2, value);
  @$pb.TagNumber(3)
  $core.bool hasConnectionId() => $_has(2);
  @$pb.TagNumber(3)
  void clearConnectionId() => $_clearField(3);

  @$pb.TagNumber(4)
  $core.String get constraintsJson => $_getSZ(3);
  @$pb.TagNumber(4)
  set constraintsJson($core.String value) => $_setString(3, value);
  @$pb.TagNumber(4)
  $core.bool hasConstraintsJson() => $_has(3);
  @$pb.TagNumber(4)
  void clearConstraintsJson() => $_clearField(4);

  @$pb.TagNumber(5)
  $core.String get provider => $_getSZ(4);
  @$pb.TagNumber(5)
  set provider($core.String value) => $_setString(4, value);
  @$pb.TagNumber(5)
  $core.bool hasProvider() => $_has(4);
  @$pb.TagNumber(5)
  void clearProvider() => $_clearField(5);
}

class FeatureDraftReply extends $pb.GeneratedMessage {
  factory FeatureDraftReply({FeatureDraft? draft}) {
    final result = create();
    if (draft != null) result.draft = draft;
    return result;
  }

  FeatureDraftReply._();

  factory FeatureDraftReply.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory FeatureDraftReply.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'FeatureDraftReply',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOM<FeatureDraft>(
          1,
          _omitFieldNames ? '' : 'draft',
          subBuilder: FeatureDraft.create,
        )
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureDraftReply clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureDraftReply copyWith(void Function(FeatureDraftReply) updates) =>
      super.copyWith((message) => updates(message as FeatureDraftReply))
          as FeatureDraftReply;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static FeatureDraftReply create() => FeatureDraftReply._();
  @$core.override
  FeatureDraftReply createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static FeatureDraftReply getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<FeatureDraftReply>(create);
  static FeatureDraftReply? _defaultInstance;

  @$pb.TagNumber(1)
  FeatureDraft get draft => $_getN(0);
  @$pb.TagNumber(1)
  set draft(FeatureDraft value) => $_setField(1, value);
  @$pb.TagNumber(1)
  $core.bool hasDraft() => $_has(0);
  @$pb.TagNumber(1)
  void clearDraft() => $_clearField(1);
  @$pb.TagNumber(1)
  FeatureDraft ensureDraft() => $_ensure(0);
}

class FeatureDraftPatchReply extends $pb.GeneratedMessage {
  factory FeatureDraftPatchReply({FeatureDraftPatch? patch}) {
    final result = create();
    if (patch != null) result.patch = patch;
    return result;
  }

  FeatureDraftPatchReply._();

  factory FeatureDraftPatchReply.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory FeatureDraftPatchReply.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'FeatureDraftPatchReply',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOM<FeatureDraftPatch>(
          1,
          _omitFieldNames ? '' : 'patch',
          subBuilder: FeatureDraftPatch.create,
        )
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureDraftPatchReply clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureDraftPatchReply copyWith(
    void Function(FeatureDraftPatchReply) updates,
  ) =>
      super.copyWith((message) => updates(message as FeatureDraftPatchReply))
          as FeatureDraftPatchReply;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static FeatureDraftPatchReply create() => FeatureDraftPatchReply._();
  @$core.override
  FeatureDraftPatchReply createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static FeatureDraftPatchReply getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<FeatureDraftPatchReply>(create);
  static FeatureDraftPatchReply? _defaultInstance;

  @$pb.TagNumber(1)
  FeatureDraftPatch get patch => $_getN(0);
  @$pb.TagNumber(1)
  set patch(FeatureDraftPatch value) => $_setField(1, value);
  @$pb.TagNumber(1)
  $core.bool hasPatch() => $_has(0);
  @$pb.TagNumber(1)
  void clearPatch() => $_clearField(1);
  @$pb.TagNumber(1)
  FeatureDraftPatch ensurePatch() => $_ensure(0);
}

class FeatureReleaseReviewReply extends $pb.GeneratedMessage {
  factory FeatureReleaseReviewReply({
    FeatureDraft? draft,
    FeatureRelease? release,
  }) {
    final result = create();
    if (draft != null) result.draft = draft;
    if (release != null) result.release = release;
    return result;
  }

  FeatureReleaseReviewReply._();

  factory FeatureReleaseReviewReply.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory FeatureReleaseReviewReply.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'FeatureReleaseReviewReply',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOM<FeatureDraft>(
          1,
          _omitFieldNames ? '' : 'draft',
          subBuilder: FeatureDraft.create,
        )
        ..aOM<FeatureRelease>(
          2,
          _omitFieldNames ? '' : 'release',
          subBuilder: FeatureRelease.create,
        )
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureReleaseReviewReply clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureReleaseReviewReply copyWith(
    void Function(FeatureReleaseReviewReply) updates,
  ) =>
      super.copyWith((message) => updates(message as FeatureReleaseReviewReply))
          as FeatureReleaseReviewReply;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static FeatureReleaseReviewReply create() => FeatureReleaseReviewReply._();
  @$core.override
  FeatureReleaseReviewReply createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static FeatureReleaseReviewReply getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<FeatureReleaseReviewReply>(create);
  static FeatureReleaseReviewReply? _defaultInstance;

  @$pb.TagNumber(1)
  FeatureDraft get draft => $_getN(0);
  @$pb.TagNumber(1)
  set draft(FeatureDraft value) => $_setField(1, value);
  @$pb.TagNumber(1)
  $core.bool hasDraft() => $_has(0);
  @$pb.TagNumber(1)
  void clearDraft() => $_clearField(1);
  @$pb.TagNumber(1)
  FeatureDraft ensureDraft() => $_ensure(0);

  @$pb.TagNumber(2)
  FeatureRelease get release => $_getN(1);
  @$pb.TagNumber(2)
  set release(FeatureRelease value) => $_setField(2, value);
  @$pb.TagNumber(2)
  $core.bool hasRelease() => $_has(1);
  @$pb.TagNumber(2)
  void clearRelease() => $_clearField(2);
  @$pb.TagNumber(2)
  FeatureRelease ensureRelease() => $_ensure(1);
}

class FeatureInstallReply extends $pb.GeneratedMessage {
  factory FeatureInstallReply({
    FeatureDraft? draft,
    FeatureRelease? release,
    $core.String? installationId,
    $core.Iterable<FeatureGrant>? activeGrants,
    $core.Iterable<$core.String>? subscriptions,
    $core.bool? rollbackAvailable,
    $core.bool? paused,
    $core.String? pauseReason,
  }) {
    final result = create();
    if (draft != null) result.draft = draft;
    if (release != null) result.release = release;
    if (installationId != null) result.installationId = installationId;
    if (activeGrants != null) result.activeGrants.addAll(activeGrants);
    if (subscriptions != null) result.subscriptions.addAll(subscriptions);
    if (rollbackAvailable != null) result.rollbackAvailable = rollbackAvailable;
    if (paused != null) result.paused = paused;
    if (pauseReason != null) result.pauseReason = pauseReason;
    return result;
  }

  FeatureInstallReply._();

  factory FeatureInstallReply.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory FeatureInstallReply.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'FeatureInstallReply',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOM<FeatureDraft>(
          1,
          _omitFieldNames ? '' : 'draft',
          subBuilder: FeatureDraft.create,
        )
        ..aOM<FeatureRelease>(
          2,
          _omitFieldNames ? '' : 'release',
          subBuilder: FeatureRelease.create,
        )
        ..aOS(3, _omitFieldNames ? '' : 'installationId')
        ..pPM<FeatureGrant>(
          4,
          _omitFieldNames ? '' : 'activeGrants',
          subBuilder: FeatureGrant.create,
        )
        ..pPS(5, _omitFieldNames ? '' : 'subscriptions')
        ..aOB(6, _omitFieldNames ? '' : 'rollbackAvailable')
        ..aOB(7, _omitFieldNames ? '' : 'paused')
        ..aOS(8, _omitFieldNames ? '' : 'pauseReason')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureInstallReply clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureInstallReply copyWith(void Function(FeatureInstallReply) updates) =>
      super.copyWith((message) => updates(message as FeatureInstallReply))
          as FeatureInstallReply;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static FeatureInstallReply create() => FeatureInstallReply._();
  @$core.override
  FeatureInstallReply createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static FeatureInstallReply getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<FeatureInstallReply>(create);
  static FeatureInstallReply? _defaultInstance;

  @$pb.TagNumber(1)
  FeatureDraft get draft => $_getN(0);
  @$pb.TagNumber(1)
  set draft(FeatureDraft value) => $_setField(1, value);
  @$pb.TagNumber(1)
  $core.bool hasDraft() => $_has(0);
  @$pb.TagNumber(1)
  void clearDraft() => $_clearField(1);
  @$pb.TagNumber(1)
  FeatureDraft ensureDraft() => $_ensure(0);

  @$pb.TagNumber(2)
  FeatureRelease get release => $_getN(1);
  @$pb.TagNumber(2)
  set release(FeatureRelease value) => $_setField(2, value);
  @$pb.TagNumber(2)
  $core.bool hasRelease() => $_has(1);
  @$pb.TagNumber(2)
  void clearRelease() => $_clearField(2);
  @$pb.TagNumber(2)
  FeatureRelease ensureRelease() => $_ensure(1);

  @$pb.TagNumber(3)
  $core.String get installationId => $_getSZ(2);
  @$pb.TagNumber(3)
  set installationId($core.String value) => $_setString(2, value);
  @$pb.TagNumber(3)
  $core.bool hasInstallationId() => $_has(2);
  @$pb.TagNumber(3)
  void clearInstallationId() => $_clearField(3);

  @$pb.TagNumber(4)
  $pb.PbList<FeatureGrant> get activeGrants => $_getList(3);

  @$pb.TagNumber(5)
  $pb.PbList<$core.String> get subscriptions => $_getList(4);

  @$pb.TagNumber(6)
  $core.bool get rollbackAvailable => $_getBF(5);
  @$pb.TagNumber(6)
  set rollbackAvailable($core.bool value) => $_setBool(5, value);
  @$pb.TagNumber(6)
  $core.bool hasRollbackAvailable() => $_has(5);
  @$pb.TagNumber(6)
  void clearRollbackAvailable() => $_clearField(6);

  @$pb.TagNumber(7)
  $core.bool get paused => $_getBF(6);
  @$pb.TagNumber(7)
  set paused($core.bool value) => $_setBool(6, value);
  @$pb.TagNumber(7)
  $core.bool hasPaused() => $_has(6);
  @$pb.TagNumber(7)
  void clearPaused() => $_clearField(7);

  @$pb.TagNumber(8)
  $core.String get pauseReason => $_getSZ(7);
  @$pb.TagNumber(8)
  set pauseReason($core.String value) => $_setString(7, value);
  @$pb.TagNumber(8)
  $core.bool hasPauseReason() => $_has(7);
  @$pb.TagNumber(8)
  void clearPauseReason() => $_clearField(8);
}

class ResumeOriginatingRequestRequest extends $pb.GeneratedMessage {
  factory ResumeOriginatingRequestRequest({
    $core.String? draftId,
    $fixnum.Int64? expectedRevision,
    $core.String? idempotencyId,
  }) {
    final result = create();
    if (draftId != null) result.draftId = draftId;
    if (expectedRevision != null) result.expectedRevision = expectedRevision;
    if (idempotencyId != null) result.idempotencyId = idempotencyId;
    return result;
  }

  ResumeOriginatingRequestRequest._();

  factory ResumeOriginatingRequestRequest.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory ResumeOriginatingRequestRequest.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'ResumeOriginatingRequestRequest',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOS(3, _omitFieldNames ? '' : 'draftId')
        ..aInt64(4, _omitFieldNames ? '' : 'expectedRevision')
        ..aOS(5, _omitFieldNames ? '' : 'idempotencyId')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ResumeOriginatingRequestRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ResumeOriginatingRequestRequest copyWith(
    void Function(ResumeOriginatingRequestRequest) updates,
  ) =>
      super.copyWith(
            (message) => updates(message as ResumeOriginatingRequestRequest),
          )
          as ResumeOriginatingRequestRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static ResumeOriginatingRequestRequest create() =>
      ResumeOriginatingRequestRequest._();
  @$core.override
  ResumeOriginatingRequestRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static ResumeOriginatingRequestRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<ResumeOriginatingRequestRequest>(
        create,
      );
  static ResumeOriginatingRequestRequest? _defaultInstance;

  @$pb.TagNumber(3)
  $core.String get draftId => $_getSZ(0);
  @$pb.TagNumber(3)
  set draftId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(3)
  $core.bool hasDraftId() => $_has(0);
  @$pb.TagNumber(3)
  void clearDraftId() => $_clearField(3);

  @$pb.TagNumber(4)
  $fixnum.Int64 get expectedRevision => $_getI64(1);
  @$pb.TagNumber(4)
  set expectedRevision($fixnum.Int64 value) => $_setInt64(1, value);
  @$pb.TagNumber(4)
  $core.bool hasExpectedRevision() => $_has(1);
  @$pb.TagNumber(4)
  void clearExpectedRevision() => $_clearField(4);

  @$pb.TagNumber(5)
  $core.String get idempotencyId => $_getSZ(2);
  @$pb.TagNumber(5)
  set idempotencyId($core.String value) => $_setString(2, value);
  @$pb.TagNumber(5)
  $core.bool hasIdempotencyId() => $_has(2);
  @$pb.TagNumber(5)
  void clearIdempotencyId() => $_clearField(5);
}

class ResumeOriginatingRequestReply extends $pb.GeneratedMessage {
  factory ResumeOriginatingRequestReply() => create();

  ResumeOriginatingRequestReply._();

  factory ResumeOriginatingRequestReply.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory ResumeOriginatingRequestReply.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
    _omitMessageNames ? '' : 'ResumeOriginatingRequestReply',
    package: const $pb.PackageName(
      _omitMessageNames ? '' : 'digitalbrain.v2.ui',
    ),
    createEmptyInstance: create,
  )..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ResumeOriginatingRequestReply clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ResumeOriginatingRequestReply copyWith(
    void Function(ResumeOriginatingRequestReply) updates,
  ) =>
      super.copyWith(
            (message) => updates(message as ResumeOriginatingRequestReply),
          )
          as ResumeOriginatingRequestReply;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static ResumeOriginatingRequestReply create() =>
      ResumeOriginatingRequestReply._();
  @$core.override
  ResumeOriginatingRequestReply createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static ResumeOriginatingRequestReply getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<ResumeOriginatingRequestReply>(create);
  static ResumeOriginatingRequestReply? _defaultInstance;
}

class ListFeaturesRequest extends $pb.GeneratedMessage {
  factory ListFeaturesRequest() => create();

  ListFeaturesRequest._();

  factory ListFeaturesRequest.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory ListFeaturesRequest.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
    _omitMessageNames ? '' : 'ListFeaturesRequest',
    package: const $pb.PackageName(
      _omitMessageNames ? '' : 'digitalbrain.v2.ui',
    ),
    createEmptyInstance: create,
  )..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ListFeaturesRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ListFeaturesRequest copyWith(void Function(ListFeaturesRequest) updates) =>
      super.copyWith((message) => updates(message as ListFeaturesRequest))
          as ListFeaturesRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static ListFeaturesRequest create() => ListFeaturesRequest._();
  @$core.override
  ListFeaturesRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static ListFeaturesRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<ListFeaturesRequest>(create);
  static ListFeaturesRequest? _defaultInstance;
}

class ListFeaturesReply extends $pb.GeneratedMessage {
  factory ListFeaturesReply() => create();

  ListFeaturesReply._();

  factory ListFeaturesReply.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory ListFeaturesReply.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
    _omitMessageNames ? '' : 'ListFeaturesReply',
    package: const $pb.PackageName(
      _omitMessageNames ? '' : 'digitalbrain.v2.ui',
    ),
    createEmptyInstance: create,
  )..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ListFeaturesReply clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ListFeaturesReply copyWith(void Function(ListFeaturesReply) updates) =>
      super.copyWith((message) => updates(message as ListFeaturesReply))
          as ListFeaturesReply;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static ListFeaturesReply create() => ListFeaturesReply._();
  @$core.override
  ListFeaturesReply createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static ListFeaturesReply getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<ListFeaturesReply>(create);
  static ListFeaturesReply? _defaultInstance;
}

class GetFeatureRequest extends $pb.GeneratedMessage {
  factory GetFeatureRequest({$core.String? featureId}) {
    final result = create();
    if (featureId != null) result.featureId = featureId;
    return result;
  }

  GetFeatureRequest._();

  factory GetFeatureRequest.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory GetFeatureRequest.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'GetFeatureRequest',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOS(3, _omitFieldNames ? '' : 'featureId')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  GetFeatureRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  GetFeatureRequest copyWith(void Function(GetFeatureRequest) updates) =>
      super.copyWith((message) => updates(message as GetFeatureRequest))
          as GetFeatureRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static GetFeatureRequest create() => GetFeatureRequest._();
  @$core.override
  GetFeatureRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static GetFeatureRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<GetFeatureRequest>(create);
  static GetFeatureRequest? _defaultInstance;

  @$pb.TagNumber(3)
  $core.String get featureId => $_getSZ(0);
  @$pb.TagNumber(3)
  set featureId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(3)
  $core.bool hasFeatureId() => $_has(0);
  @$pb.TagNumber(3)
  void clearFeatureId() => $_clearField(3);
}

class FeatureReply extends $pb.GeneratedMessage {
  factory FeatureReply() => create();

  FeatureReply._();

  factory FeatureReply.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory FeatureReply.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
    _omitMessageNames ? '' : 'FeatureReply',
    package: const $pb.PackageName(
      _omitMessageNames ? '' : 'digitalbrain.v2.ui',
    ),
    createEmptyInstance: create,
  )..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureReply clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FeatureReply copyWith(void Function(FeatureReply) updates) =>
      super.copyWith((message) => updates(message as FeatureReply))
          as FeatureReply;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static FeatureReply create() => FeatureReply._();
  @$core.override
  FeatureReply createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static FeatureReply getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<FeatureReply>(create);
  static FeatureReply? _defaultInstance;
}

class ListConnectionsRequest extends $pb.GeneratedMessage {
  factory ListConnectionsRequest() => create();

  ListConnectionsRequest._();

  factory ListConnectionsRequest.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory ListConnectionsRequest.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
    _omitMessageNames ? '' : 'ListConnectionsRequest',
    package: const $pb.PackageName(
      _omitMessageNames ? '' : 'digitalbrain.v2.ui',
    ),
    createEmptyInstance: create,
  )..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ListConnectionsRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ListConnectionsRequest copyWith(
    void Function(ListConnectionsRequest) updates,
  ) =>
      super.copyWith((message) => updates(message as ListConnectionsRequest))
          as ListConnectionsRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static ListConnectionsRequest create() => ListConnectionsRequest._();
  @$core.override
  ListConnectionsRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static ListConnectionsRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<ListConnectionsRequest>(create);
  static ListConnectionsRequest? _defaultInstance;
}

class ListConnectionsReply extends $pb.GeneratedMessage {
  factory ListConnectionsReply() => create();

  ListConnectionsReply._();

  factory ListConnectionsReply.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory ListConnectionsReply.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
    _omitMessageNames ? '' : 'ListConnectionsReply',
    package: const $pb.PackageName(
      _omitMessageNames ? '' : 'digitalbrain.v2.ui',
    ),
    createEmptyInstance: create,
  )..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ListConnectionsReply clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ListConnectionsReply copyWith(void Function(ListConnectionsReply) updates) =>
      super.copyWith((message) => updates(message as ListConnectionsReply))
          as ListConnectionsReply;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static ListConnectionsReply create() => ListConnectionsReply._();
  @$core.override
  ListConnectionsReply createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static ListConnectionsReply getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<ListConnectionsReply>(create);
  static ListConnectionsReply? _defaultInstance;
}

class GetConnectionRequest extends $pb.GeneratedMessage {
  factory GetConnectionRequest({$core.String? connectionId}) {
    final result = create();
    if (connectionId != null) result.connectionId = connectionId;
    return result;
  }

  GetConnectionRequest._();

  factory GetConnectionRequest.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory GetConnectionRequest.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'GetConnectionRequest',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOS(3, _omitFieldNames ? '' : 'connectionId')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  GetConnectionRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  GetConnectionRequest copyWith(void Function(GetConnectionRequest) updates) =>
      super.copyWith((message) => updates(message as GetConnectionRequest))
          as GetConnectionRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static GetConnectionRequest create() => GetConnectionRequest._();
  @$core.override
  GetConnectionRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static GetConnectionRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<GetConnectionRequest>(create);
  static GetConnectionRequest? _defaultInstance;

  @$pb.TagNumber(3)
  $core.String get connectionId => $_getSZ(0);
  @$pb.TagNumber(3)
  set connectionId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(3)
  $core.bool hasConnectionId() => $_has(0);
  @$pb.TagNumber(3)
  void clearConnectionId() => $_clearField(3);
}

class ConnectionReply extends $pb.GeneratedMessage {
  factory ConnectionReply() => create();

  ConnectionReply._();

  factory ConnectionReply.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory ConnectionReply.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
    _omitMessageNames ? '' : 'ConnectionReply',
    package: const $pb.PackageName(
      _omitMessageNames ? '' : 'digitalbrain.v2.ui',
    ),
    createEmptyInstance: create,
  )..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ConnectionReply clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ConnectionReply copyWith(void Function(ConnectionReply) updates) =>
      super.copyWith((message) => updates(message as ConnectionReply))
          as ConnectionReply;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static ConnectionReply create() => ConnectionReply._();
  @$core.override
  ConnectionReply createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static ConnectionReply getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<ConnectionReply>(create);
  static ConnectionReply? _defaultInstance;
}

class ListActivityRequest extends $pb.GeneratedMessage {
  factory ListActivityRequest() => create();

  ListActivityRequest._();

  factory ListActivityRequest.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory ListActivityRequest.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
    _omitMessageNames ? '' : 'ListActivityRequest',
    package: const $pb.PackageName(
      _omitMessageNames ? '' : 'digitalbrain.v2.ui',
    ),
    createEmptyInstance: create,
  )..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ListActivityRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ListActivityRequest copyWith(void Function(ListActivityRequest) updates) =>
      super.copyWith((message) => updates(message as ListActivityRequest))
          as ListActivityRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static ListActivityRequest create() => ListActivityRequest._();
  @$core.override
  ListActivityRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static ListActivityRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<ListActivityRequest>(create);
  static ListActivityRequest? _defaultInstance;
}

class ListActivityReply extends $pb.GeneratedMessage {
  factory ListActivityReply() => create();

  ListActivityReply._();

  factory ListActivityReply.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory ListActivityReply.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
    _omitMessageNames ? '' : 'ListActivityReply',
    package: const $pb.PackageName(
      _omitMessageNames ? '' : 'digitalbrain.v2.ui',
    ),
    createEmptyInstance: create,
  )..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ListActivityReply clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ListActivityReply copyWith(void Function(ListActivityReply) updates) =>
      super.copyWith((message) => updates(message as ListActivityReply))
          as ListActivityReply;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static ListActivityReply create() => ListActivityReply._();
  @$core.override
  ListActivityReply createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static ListActivityReply getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<ListActivityReply>(create);
  static ListActivityReply? _defaultInstance;
}

class GetRunRequest extends $pb.GeneratedMessage {
  factory GetRunRequest({$core.String? runId}) {
    final result = create();
    if (runId != null) result.runId = runId;
    return result;
  }

  GetRunRequest._();

  factory GetRunRequest.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory GetRunRequest.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'GetRunRequest',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOS(3, _omitFieldNames ? '' : 'runId')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  GetRunRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  GetRunRequest copyWith(void Function(GetRunRequest) updates) =>
      super.copyWith((message) => updates(message as GetRunRequest))
          as GetRunRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static GetRunRequest create() => GetRunRequest._();
  @$core.override
  GetRunRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static GetRunRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<GetRunRequest>(create);
  static GetRunRequest? _defaultInstance;

  @$pb.TagNumber(3)
  $core.String get runId => $_getSZ(0);
  @$pb.TagNumber(3)
  set runId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(3)
  $core.bool hasRunId() => $_has(0);
  @$pb.TagNumber(3)
  void clearRunId() => $_clearField(3);
}

class RunReply extends $pb.GeneratedMessage {
  factory RunReply() => create();

  RunReply._();

  factory RunReply.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory RunReply.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
    _omitMessageNames ? '' : 'RunReply',
    package: const $pb.PackageName(
      _omitMessageNames ? '' : 'digitalbrain.v2.ui',
    ),
    createEmptyInstance: create,
  )..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  RunReply clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  RunReply copyWith(void Function(RunReply) updates) =>
      super.copyWith((message) => updates(message as RunReply)) as RunReply;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static RunReply create() => RunReply._();
  @$core.override
  RunReply createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static RunReply getDefault() =>
      _defaultInstance ??= $pb.GeneratedMessage.$_defaultFor<RunReply>(create);
  static RunReply? _defaultInstance;
}

class ListMemoryItemsRequest extends $pb.GeneratedMessage {
  factory ListMemoryItemsRequest() => create();

  ListMemoryItemsRequest._();

  factory ListMemoryItemsRequest.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory ListMemoryItemsRequest.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
    _omitMessageNames ? '' : 'ListMemoryItemsRequest',
    package: const $pb.PackageName(
      _omitMessageNames ? '' : 'digitalbrain.v2.ui',
    ),
    createEmptyInstance: create,
  )..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ListMemoryItemsRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ListMemoryItemsRequest copyWith(
    void Function(ListMemoryItemsRequest) updates,
  ) =>
      super.copyWith((message) => updates(message as ListMemoryItemsRequest))
          as ListMemoryItemsRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static ListMemoryItemsRequest create() => ListMemoryItemsRequest._();
  @$core.override
  ListMemoryItemsRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static ListMemoryItemsRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<ListMemoryItemsRequest>(create);
  static ListMemoryItemsRequest? _defaultInstance;
}

class ListMemoryItemsReply extends $pb.GeneratedMessage {
  factory ListMemoryItemsReply() => create();

  ListMemoryItemsReply._();

  factory ListMemoryItemsReply.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory ListMemoryItemsReply.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
    _omitMessageNames ? '' : 'ListMemoryItemsReply',
    package: const $pb.PackageName(
      _omitMessageNames ? '' : 'digitalbrain.v2.ui',
    ),
    createEmptyInstance: create,
  )..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ListMemoryItemsReply clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ListMemoryItemsReply copyWith(void Function(ListMemoryItemsReply) updates) =>
      super.copyWith((message) => updates(message as ListMemoryItemsReply))
          as ListMemoryItemsReply;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static ListMemoryItemsReply create() => ListMemoryItemsReply._();
  @$core.override
  ListMemoryItemsReply createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static ListMemoryItemsReply getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<ListMemoryItemsReply>(create);
  static ListMemoryItemsReply? _defaultInstance;
}

class GetMemoryItemRequest extends $pb.GeneratedMessage {
  factory GetMemoryItemRequest({$core.String? memoryItemId}) {
    final result = create();
    if (memoryItemId != null) result.memoryItemId = memoryItemId;
    return result;
  }

  GetMemoryItemRequest._();

  factory GetMemoryItemRequest.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory GetMemoryItemRequest.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i =
      $pb.BuilderInfo(
          _omitMessageNames ? '' : 'GetMemoryItemRequest',
          package: const $pb.PackageName(
            _omitMessageNames ? '' : 'digitalbrain.v2.ui',
          ),
          createEmptyInstance: create,
        )
        ..aOS(3, _omitFieldNames ? '' : 'memoryItemId')
        ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  GetMemoryItemRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  GetMemoryItemRequest copyWith(void Function(GetMemoryItemRequest) updates) =>
      super.copyWith((message) => updates(message as GetMemoryItemRequest))
          as GetMemoryItemRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static GetMemoryItemRequest create() => GetMemoryItemRequest._();
  @$core.override
  GetMemoryItemRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static GetMemoryItemRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<GetMemoryItemRequest>(create);
  static GetMemoryItemRequest? _defaultInstance;

  @$pb.TagNumber(3)
  $core.String get memoryItemId => $_getSZ(0);
  @$pb.TagNumber(3)
  set memoryItemId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(3)
  $core.bool hasMemoryItemId() => $_has(0);
  @$pb.TagNumber(3)
  void clearMemoryItemId() => $_clearField(3);
}

class MemoryItemReply extends $pb.GeneratedMessage {
  factory MemoryItemReply() => create();

  MemoryItemReply._();

  factory MemoryItemReply.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory MemoryItemReply.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
    _omitMessageNames ? '' : 'MemoryItemReply',
    package: const $pb.PackageName(
      _omitMessageNames ? '' : 'digitalbrain.v2.ui',
    ),
    createEmptyInstance: create,
  )..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  MemoryItemReply clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  MemoryItemReply copyWith(void Function(MemoryItemReply) updates) =>
      super.copyWith((message) => updates(message as MemoryItemReply))
          as MemoryItemReply;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static MemoryItemReply create() => MemoryItemReply._();
  @$core.override
  MemoryItemReply createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static MemoryItemReply getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<MemoryItemReply>(create);
  static MemoryItemReply? _defaultInstance;
}

class GetHomeSummaryRequest extends $pb.GeneratedMessage {
  factory GetHomeSummaryRequest() => create();

  GetHomeSummaryRequest._();

  factory GetHomeSummaryRequest.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory GetHomeSummaryRequest.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
    _omitMessageNames ? '' : 'GetHomeSummaryRequest',
    package: const $pb.PackageName(
      _omitMessageNames ? '' : 'digitalbrain.v2.ui',
    ),
    createEmptyInstance: create,
  )..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  GetHomeSummaryRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  GetHomeSummaryRequest copyWith(
    void Function(GetHomeSummaryRequest) updates,
  ) =>
      super.copyWith((message) => updates(message as GetHomeSummaryRequest))
          as GetHomeSummaryRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static GetHomeSummaryRequest create() => GetHomeSummaryRequest._();
  @$core.override
  GetHomeSummaryRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static GetHomeSummaryRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<GetHomeSummaryRequest>(create);
  static GetHomeSummaryRequest? _defaultInstance;
}

class HomeSummaryReply extends $pb.GeneratedMessage {
  factory HomeSummaryReply() => create();

  HomeSummaryReply._();

  factory HomeSummaryReply.fromBuffer(
    $core.List<$core.int> data, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromBuffer(data, registry);
  factory HomeSummaryReply.fromJson(
    $core.String json, [
    $pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY,
  ]) => create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
    _omitMessageNames ? '' : 'HomeSummaryReply',
    package: const $pb.PackageName(
      _omitMessageNames ? '' : 'digitalbrain.v2.ui',
    ),
    createEmptyInstance: create,
  )..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  HomeSummaryReply clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  HomeSummaryReply copyWith(void Function(HomeSummaryReply) updates) =>
      super.copyWith((message) => updates(message as HomeSummaryReply))
          as HomeSummaryReply;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static HomeSummaryReply create() => HomeSummaryReply._();
  @$core.override
  HomeSummaryReply createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static HomeSummaryReply getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<HomeSummaryReply>(create);
  static HomeSummaryReply? _defaultInstance;
}

const $core.bool _omitFieldNames = $core.bool.fromEnvironment(
  'protobuf.omit_field_names',
);
const $core.bool _omitMessageNames = $core.bool.fromEnvironment(
  'protobuf.omit_message_names',
);
