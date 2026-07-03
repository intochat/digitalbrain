// This is a generated file - do not edit.
//
// Generated from digitalbrain.proto.

// @dart = 3.3

// ignore_for_file: annotate_overrides, camel_case_types, comment_references
// ignore_for_file: constant_identifier_names
// ignore_for_file: curly_braces_in_flow_control_structures
// ignore_for_file: deprecated_member_use_from_same_package, library_prefixes
// ignore_for_file: non_constant_identifier_names, prefer_relative_imports

import 'dart:core' as $core;

import 'package:protobuf/protobuf.dart' as $pb;

export 'package:protobuf/protobuf.dart' show GeneratedMessageGenericExtensions;

class SynapseEnvelope extends $pb.GeneratedMessage {
  factory SynapseEnvelope({
    $core.String? correlationId,
    $core.String? typeName,
    $core.List<$core.int>? payload,
  }) {
    final result = create();
    if (correlationId != null) result.correlationId = correlationId;
    if (typeName != null) result.typeName = typeName;
    if (payload != null) result.payload = payload;
    return result;
  }

  SynapseEnvelope._();

  factory SynapseEnvelope.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory SynapseEnvelope.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'SynapseEnvelope',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'correlationId')
    ..aOS(2, _omitFieldNames ? '' : 'typeName')
    ..a<$core.List<$core.int>>(
        3, _omitFieldNames ? '' : 'payload', $pb.PbFieldType.OY)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SynapseEnvelope clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SynapseEnvelope copyWith(void Function(SynapseEnvelope) updates) =>
      super.copyWith((message) => updates(message as SynapseEnvelope))
          as SynapseEnvelope;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static SynapseEnvelope create() => SynapseEnvelope._();
  @$core.override
  SynapseEnvelope createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static SynapseEnvelope getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<SynapseEnvelope>(create);
  static SynapseEnvelope? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get correlationId => $_getSZ(0);
  @$pb.TagNumber(1)
  set correlationId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasCorrelationId() => $_has(0);
  @$pb.TagNumber(1)
  void clearCorrelationId() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get typeName => $_getSZ(1);
  @$pb.TagNumber(2)
  set typeName($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasTypeName() => $_has(1);
  @$pb.TagNumber(2)
  void clearTypeName() => $_clearField(2);

  @$pb.TagNumber(3)
  $core.List<$core.int> get payload => $_getN(2);
  @$pb.TagNumber(3)
  set payload($core.List<$core.int> value) => $_setBytes(2, value);
  @$pb.TagNumber(3)
  $core.bool hasPayload() => $_has(2);
  @$pb.TagNumber(3)
  void clearPayload() => $_clearField(3);
}

class WatchHomeFeedRequest extends $pb.GeneratedMessage {
  factory WatchHomeFeedRequest() => create();

  WatchHomeFeedRequest._();

  factory WatchHomeFeedRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory WatchHomeFeedRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'WatchHomeFeedRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  WatchHomeFeedRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  WatchHomeFeedRequest copyWith(void Function(WatchHomeFeedRequest) updates) =>
      super.copyWith((message) => updates(message as WatchHomeFeedRequest))
          as WatchHomeFeedRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static WatchHomeFeedRequest create() => WatchHomeFeedRequest._();
  @$core.override
  WatchHomeFeedRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static WatchHomeFeedRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<WatchHomeFeedRequest>(create);
  static WatchHomeFeedRequest? _defaultInstance;
}

class RfwCardEnvelope extends $pb.GeneratedMessage {
  factory RfwCardEnvelope({
    $core.String? correlationId,
    $core.String? libraryName,
    $core.String? rootWidget,
    $core.String? dataJson,
    $core.String? timestamp,
    $core.String? callerNeuronType,
  }) {
    final result = create();
    if (correlationId != null) result.correlationId = correlationId;
    if (libraryName != null) result.libraryName = libraryName;
    if (rootWidget != null) result.rootWidget = rootWidget;
    if (dataJson != null) result.dataJson = dataJson;
    if (timestamp != null) result.timestamp = timestamp;
    if (callerNeuronType != null) result.callerNeuronType = callerNeuronType;
    return result;
  }

  RfwCardEnvelope._();

  factory RfwCardEnvelope.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory RfwCardEnvelope.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'RfwCardEnvelope',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'correlationId')
    ..aOS(2, _omitFieldNames ? '' : 'libraryName')
    ..aOS(3, _omitFieldNames ? '' : 'rootWidget')
    ..aOS(4, _omitFieldNames ? '' : 'dataJson')
    ..aOS(5, _omitFieldNames ? '' : 'timestamp')
    ..aOS(6, _omitFieldNames ? '' : 'callerNeuronType')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  RfwCardEnvelope clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  RfwCardEnvelope copyWith(void Function(RfwCardEnvelope) updates) =>
      super.copyWith((message) => updates(message as RfwCardEnvelope))
          as RfwCardEnvelope;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static RfwCardEnvelope create() => RfwCardEnvelope._();
  @$core.override
  RfwCardEnvelope createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static RfwCardEnvelope getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<RfwCardEnvelope>(create);
  static RfwCardEnvelope? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get correlationId => $_getSZ(0);
  @$pb.TagNumber(1)
  set correlationId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasCorrelationId() => $_has(0);
  @$pb.TagNumber(1)
  void clearCorrelationId() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get libraryName => $_getSZ(1);
  @$pb.TagNumber(2)
  set libraryName($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasLibraryName() => $_has(1);
  @$pb.TagNumber(2)
  void clearLibraryName() => $_clearField(2);

  @$pb.TagNumber(3)
  $core.String get rootWidget => $_getSZ(2);
  @$pb.TagNumber(3)
  set rootWidget($core.String value) => $_setString(2, value);
  @$pb.TagNumber(3)
  $core.bool hasRootWidget() => $_has(2);
  @$pb.TagNumber(3)
  void clearRootWidget() => $_clearField(3);

  @$pb.TagNumber(4)
  $core.String get dataJson => $_getSZ(3);
  @$pb.TagNumber(4)
  set dataJson($core.String value) => $_setString(3, value);
  @$pb.TagNumber(4)
  $core.bool hasDataJson() => $_has(3);
  @$pb.TagNumber(4)
  void clearDataJson() => $_clearField(4);

  @$pb.TagNumber(5)
  $core.String get timestamp => $_getSZ(4);
  @$pb.TagNumber(5)
  set timestamp($core.String value) => $_setString(4, value);
  @$pb.TagNumber(5)
  $core.bool hasTimestamp() => $_has(4);
  @$pb.TagNumber(5)
  void clearTimestamp() => $_clearField(5);

  @$pb.TagNumber(6)
  $core.String get callerNeuronType => $_getSZ(5);
  @$pb.TagNumber(6)
  set callerNeuronType($core.String value) => $_setString(5, value);
  @$pb.TagNumber(6)
  $core.bool hasCallerNeuronType() => $_has(5);
  @$pb.TagNumber(6)
  void clearCallerNeuronType() => $_clearField(6);
}

class TranscribeRequest extends $pb.GeneratedMessage {
  factory TranscribeRequest({
    $core.List<$core.int>? audioChunk,
    $core.String? mimeType,
    $core.String? languageHint,
  }) {
    final result = create();
    if (audioChunk != null) result.audioChunk = audioChunk;
    if (mimeType != null) result.mimeType = mimeType;
    if (languageHint != null) result.languageHint = languageHint;
    return result;
  }

  TranscribeRequest._();

  factory TranscribeRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory TranscribeRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'TranscribeRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..a<$core.List<$core.int>>(
        1, _omitFieldNames ? '' : 'audioChunk', $pb.PbFieldType.OY)
    ..aOS(2, _omitFieldNames ? '' : 'mimeType')
    ..aOS(3, _omitFieldNames ? '' : 'languageHint')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  TranscribeRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  TranscribeRequest copyWith(void Function(TranscribeRequest) updates) =>
      super.copyWith((message) => updates(message as TranscribeRequest))
          as TranscribeRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static TranscribeRequest create() => TranscribeRequest._();
  @$core.override
  TranscribeRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static TranscribeRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<TranscribeRequest>(create);
  static TranscribeRequest? _defaultInstance;

  @$pb.TagNumber(1)
  $core.List<$core.int> get audioChunk => $_getN(0);
  @$pb.TagNumber(1)
  set audioChunk($core.List<$core.int> value) => $_setBytes(0, value);
  @$pb.TagNumber(1)
  $core.bool hasAudioChunk() => $_has(0);
  @$pb.TagNumber(1)
  void clearAudioChunk() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get mimeType => $_getSZ(1);
  @$pb.TagNumber(2)
  set mimeType($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasMimeType() => $_has(1);
  @$pb.TagNumber(2)
  void clearMimeType() => $_clearField(2);

  @$pb.TagNumber(3)
  $core.String get languageHint => $_getSZ(2);
  @$pb.TagNumber(3)
  set languageHint($core.String value) => $_setString(2, value);
  @$pb.TagNumber(3)
  $core.bool hasLanguageHint() => $_has(2);
  @$pb.TagNumber(3)
  void clearLanguageHint() => $_clearField(3);
}

class TranscribeResponse extends $pb.GeneratedMessage {
  factory TranscribeResponse({
    $core.String? transcript,
    $core.String? detectedLanguage,
    $core.String? correlationId,
  }) {
    final result = create();
    if (transcript != null) result.transcript = transcript;
    if (detectedLanguage != null) result.detectedLanguage = detectedLanguage;
    if (correlationId != null) result.correlationId = correlationId;
    return result;
  }

  TranscribeResponse._();

  factory TranscribeResponse.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory TranscribeResponse.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'TranscribeResponse',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'transcript')
    ..aOS(2, _omitFieldNames ? '' : 'detectedLanguage')
    ..aOS(3, _omitFieldNames ? '' : 'correlationId')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  TranscribeResponse clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  TranscribeResponse copyWith(void Function(TranscribeResponse) updates) =>
      super.copyWith((message) => updates(message as TranscribeResponse))
          as TranscribeResponse;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static TranscribeResponse create() => TranscribeResponse._();
  @$core.override
  TranscribeResponse createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static TranscribeResponse getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<TranscribeResponse>(create);
  static TranscribeResponse? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get transcript => $_getSZ(0);
  @$pb.TagNumber(1)
  set transcript($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasTranscript() => $_has(0);
  @$pb.TagNumber(1)
  void clearTranscript() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get detectedLanguage => $_getSZ(1);
  @$pb.TagNumber(2)
  set detectedLanguage($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasDetectedLanguage() => $_has(1);
  @$pb.TagNumber(2)
  void clearDetectedLanguage() => $_clearField(2);

  @$pb.TagNumber(3)
  $core.String get correlationId => $_getSZ(2);
  @$pb.TagNumber(3)
  set correlationId($core.String value) => $_setString(2, value);
  @$pb.TagNumber(3)
  $core.bool hasCorrelationId() => $_has(2);
  @$pb.TagNumber(3)
  void clearCorrelationId() => $_clearField(3);
}

class AiHealthRequest extends $pb.GeneratedMessage {
  factory AiHealthRequest() => create();

  AiHealthRequest._();

  factory AiHealthRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory AiHealthRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'AiHealthRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  AiHealthRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  AiHealthRequest copyWith(void Function(AiHealthRequest) updates) =>
      super.copyWith((message) => updates(message as AiHealthRequest))
          as AiHealthRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static AiHealthRequest create() => AiHealthRequest._();
  @$core.override
  AiHealthRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static AiHealthRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<AiHealthRequest>(create);
  static AiHealthRequest? _defaultInstance;
}

class AiHealthResponse extends $pb.GeneratedMessage {
  factory AiHealthResponse({
    $core.bool? live,
    $core.String? reason,
    $core.String? model,
  }) {
    final result = create();
    if (live != null) result.live = live;
    if (reason != null) result.reason = reason;
    if (model != null) result.model = model;
    return result;
  }

  AiHealthResponse._();

  factory AiHealthResponse.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory AiHealthResponse.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'AiHealthResponse',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOB(1, _omitFieldNames ? '' : 'live')
    ..aOS(2, _omitFieldNames ? '' : 'reason')
    ..aOS(3, _omitFieldNames ? '' : 'model')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  AiHealthResponse clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  AiHealthResponse copyWith(void Function(AiHealthResponse) updates) =>
      super.copyWith((message) => updates(message as AiHealthResponse))
          as AiHealthResponse;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static AiHealthResponse create() => AiHealthResponse._();
  @$core.override
  AiHealthResponse createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static AiHealthResponse getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<AiHealthResponse>(create);
  static AiHealthResponse? _defaultInstance;

  @$pb.TagNumber(1)
  $core.bool get live => $_getBF(0);
  @$pb.TagNumber(1)
  set live($core.bool value) => $_setBool(0, value);
  @$pb.TagNumber(1)
  $core.bool hasLive() => $_has(0);
  @$pb.TagNumber(1)
  void clearLive() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get reason => $_getSZ(1);
  @$pb.TagNumber(2)
  set reason($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasReason() => $_has(1);
  @$pb.TagNumber(2)
  void clearReason() => $_clearField(2);

  @$pb.TagNumber(3)
  $core.String get model => $_getSZ(2);
  @$pb.TagNumber(3)
  set model($core.String value) => $_setString(2, value);
  @$pb.TagNumber(3)
  $core.bool hasModel() => $_has(2);
  @$pb.TagNumber(3)
  void clearModel() => $_clearField(3);
}

class SubmitPromptRequest extends $pb.GeneratedMessage {
  factory SubmitPromptRequest({
    $core.String? userId,
    $core.String? text,
    $core.String? correlationId,
  }) {
    final result = create();
    if (userId != null) result.userId = userId;
    if (text != null) result.text = text;
    if (correlationId != null) result.correlationId = correlationId;
    return result;
  }

  SubmitPromptRequest._();

  factory SubmitPromptRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory SubmitPromptRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'SubmitPromptRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'userId')
    ..aOS(2, _omitFieldNames ? '' : 'text')
    ..aOS(3, _omitFieldNames ? '' : 'correlationId')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SubmitPromptRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SubmitPromptRequest copyWith(void Function(SubmitPromptRequest) updates) =>
      super.copyWith((message) => updates(message as SubmitPromptRequest))
          as SubmitPromptRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static SubmitPromptRequest create() => SubmitPromptRequest._();
  @$core.override
  SubmitPromptRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static SubmitPromptRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<SubmitPromptRequest>(create);
  static SubmitPromptRequest? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get userId => $_getSZ(0);
  @$pb.TagNumber(1)
  set userId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasUserId() => $_has(0);
  @$pb.TagNumber(1)
  void clearUserId() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get text => $_getSZ(1);
  @$pb.TagNumber(2)
  set text($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasText() => $_has(1);
  @$pb.TagNumber(2)
  void clearText() => $_clearField(2);

  @$pb.TagNumber(3)
  $core.String get correlationId => $_getSZ(2);
  @$pb.TagNumber(3)
  set correlationId($core.String value) => $_setString(2, value);
  @$pb.TagNumber(3)
  $core.bool hasCorrelationId() => $_has(2);
  @$pb.TagNumber(3)
  void clearCorrelationId() => $_clearField(3);
}

class SubmitPromptReply extends $pb.GeneratedMessage {
  factory SubmitPromptReply({
    $core.String? correlationId,
  }) {
    final result = create();
    if (correlationId != null) result.correlationId = correlationId;
    return result;
  }

  SubmitPromptReply._();

  factory SubmitPromptReply.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory SubmitPromptReply.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'SubmitPromptReply',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'correlationId')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SubmitPromptReply clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SubmitPromptReply copyWith(void Function(SubmitPromptReply) updates) =>
      super.copyWith((message) => updates(message as SubmitPromptReply))
          as SubmitPromptReply;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static SubmitPromptReply create() => SubmitPromptReply._();
  @$core.override
  SubmitPromptReply createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static SubmitPromptReply getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<SubmitPromptReply>(create);
  static SubmitPromptReply? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get correlationId => $_getSZ(0);
  @$pb.TagNumber(1)
  set correlationId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasCorrelationId() => $_has(0);
  @$pb.TagNumber(1)
  void clearCorrelationId() => $_clearField(1);
}

class FlutterPerfSampleProto extends $pb.GeneratedMessage {
  factory FlutterPerfSampleProto({
    $core.String? clientId,
    $core.String? sampleWindowId,
    $core.int? frameCount,
    $core.double? p50FrameMs,
    $core.double? p95FrameMs,
    $core.double? jankPct,
    $core.int? widgetCount,
    $core.int? glowPainterCount,
    $core.int? rebuildsPerSecond,
    $core.String? platform,
    $core.String? timestamp,
  }) {
    final result = create();
    if (clientId != null) result.clientId = clientId;
    if (sampleWindowId != null) result.sampleWindowId = sampleWindowId;
    if (frameCount != null) result.frameCount = frameCount;
    if (p50FrameMs != null) result.p50FrameMs = p50FrameMs;
    if (p95FrameMs != null) result.p95FrameMs = p95FrameMs;
    if (jankPct != null) result.jankPct = jankPct;
    if (widgetCount != null) result.widgetCount = widgetCount;
    if (glowPainterCount != null) result.glowPainterCount = glowPainterCount;
    if (rebuildsPerSecond != null) result.rebuildsPerSecond = rebuildsPerSecond;
    if (platform != null) result.platform = platform;
    if (timestamp != null) result.timestamp = timestamp;
    return result;
  }

  FlutterPerfSampleProto._();

  factory FlutterPerfSampleProto.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory FlutterPerfSampleProto.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'FlutterPerfSampleProto',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'clientId')
    ..aOS(2, _omitFieldNames ? '' : 'sampleWindowId')
    ..aI(3, _omitFieldNames ? '' : 'frameCount')
    ..aD(4, _omitFieldNames ? '' : 'p50FrameMs')
    ..aD(5, _omitFieldNames ? '' : 'p95FrameMs')
    ..aD(6, _omitFieldNames ? '' : 'jankPct')
    ..aI(7, _omitFieldNames ? '' : 'widgetCount')
    ..aI(8, _omitFieldNames ? '' : 'glowPainterCount')
    ..aI(9, _omitFieldNames ? '' : 'rebuildsPerSecond')
    ..aOS(10, _omitFieldNames ? '' : 'platform')
    ..aOS(11, _omitFieldNames ? '' : 'timestamp')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FlutterPerfSampleProto clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FlutterPerfSampleProto copyWith(
          void Function(FlutterPerfSampleProto) updates) =>
      super.copyWith((message) => updates(message as FlutterPerfSampleProto))
          as FlutterPerfSampleProto;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static FlutterPerfSampleProto create() => FlutterPerfSampleProto._();
  @$core.override
  FlutterPerfSampleProto createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static FlutterPerfSampleProto getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<FlutterPerfSampleProto>(create);
  static FlutterPerfSampleProto? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get clientId => $_getSZ(0);
  @$pb.TagNumber(1)
  set clientId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasClientId() => $_has(0);
  @$pb.TagNumber(1)
  void clearClientId() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get sampleWindowId => $_getSZ(1);
  @$pb.TagNumber(2)
  set sampleWindowId($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasSampleWindowId() => $_has(1);
  @$pb.TagNumber(2)
  void clearSampleWindowId() => $_clearField(2);

  @$pb.TagNumber(3)
  $core.int get frameCount => $_getIZ(2);
  @$pb.TagNumber(3)
  set frameCount($core.int value) => $_setSignedInt32(2, value);
  @$pb.TagNumber(3)
  $core.bool hasFrameCount() => $_has(2);
  @$pb.TagNumber(3)
  void clearFrameCount() => $_clearField(3);

  @$pb.TagNumber(4)
  $core.double get p50FrameMs => $_getN(3);
  @$pb.TagNumber(4)
  set p50FrameMs($core.double value) => $_setDouble(3, value);
  @$pb.TagNumber(4)
  $core.bool hasP50FrameMs() => $_has(3);
  @$pb.TagNumber(4)
  void clearP50FrameMs() => $_clearField(4);

  @$pb.TagNumber(5)
  $core.double get p95FrameMs => $_getN(4);
  @$pb.TagNumber(5)
  set p95FrameMs($core.double value) => $_setDouble(4, value);
  @$pb.TagNumber(5)
  $core.bool hasP95FrameMs() => $_has(4);
  @$pb.TagNumber(5)
  void clearP95FrameMs() => $_clearField(5);

  @$pb.TagNumber(6)
  $core.double get jankPct => $_getN(5);
  @$pb.TagNumber(6)
  set jankPct($core.double value) => $_setDouble(5, value);
  @$pb.TagNumber(6)
  $core.bool hasJankPct() => $_has(5);
  @$pb.TagNumber(6)
  void clearJankPct() => $_clearField(6);

  @$pb.TagNumber(7)
  $core.int get widgetCount => $_getIZ(6);
  @$pb.TagNumber(7)
  set widgetCount($core.int value) => $_setSignedInt32(6, value);
  @$pb.TagNumber(7)
  $core.bool hasWidgetCount() => $_has(6);
  @$pb.TagNumber(7)
  void clearWidgetCount() => $_clearField(7);

  @$pb.TagNumber(8)
  $core.int get glowPainterCount => $_getIZ(7);
  @$pb.TagNumber(8)
  set glowPainterCount($core.int value) => $_setSignedInt32(7, value);
  @$pb.TagNumber(8)
  $core.bool hasGlowPainterCount() => $_has(7);
  @$pb.TagNumber(8)
  void clearGlowPainterCount() => $_clearField(8);

  @$pb.TagNumber(9)
  $core.int get rebuildsPerSecond => $_getIZ(8);
  @$pb.TagNumber(9)
  set rebuildsPerSecond($core.int value) => $_setSignedInt32(8, value);
  @$pb.TagNumber(9)
  $core.bool hasRebuildsPerSecond() => $_has(8);
  @$pb.TagNumber(9)
  void clearRebuildsPerSecond() => $_clearField(9);

  @$pb.TagNumber(10)
  $core.String get platform => $_getSZ(9);
  @$pb.TagNumber(10)
  set platform($core.String value) => $_setString(9, value);
  @$pb.TagNumber(10)
  $core.bool hasPlatform() => $_has(9);
  @$pb.TagNumber(10)
  void clearPlatform() => $_clearField(10);

  @$pb.TagNumber(11)
  $core.String get timestamp => $_getSZ(10);
  @$pb.TagNumber(11)
  set timestamp($core.String value) => $_setString(10, value);
  @$pb.TagNumber(11)
  $core.bool hasTimestamp() => $_has(10);
  @$pb.TagNumber(11)
  void clearTimestamp() => $_clearField(11);
}

class FlutterPerfAck extends $pb.GeneratedMessage {
  factory FlutterPerfAck() => create();

  FlutterPerfAck._();

  factory FlutterPerfAck.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory FlutterPerfAck.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'FlutterPerfAck',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FlutterPerfAck clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  FlutterPerfAck copyWith(void Function(FlutterPerfAck) updates) =>
      super.copyWith((message) => updates(message as FlutterPerfAck))
          as FlutterPerfAck;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static FlutterPerfAck create() => FlutterPerfAck._();
  @$core.override
  FlutterPerfAck createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static FlutterPerfAck getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<FlutterPerfAck>(create);
  static FlutterPerfAck? _defaultInstance;
}

class WatchVisualLoadHintRequest extends $pb.GeneratedMessage {
  factory WatchVisualLoadHintRequest({
    $core.String? clientId,
  }) {
    final result = create();
    if (clientId != null) result.clientId = clientId;
    return result;
  }

  WatchVisualLoadHintRequest._();

  factory WatchVisualLoadHintRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory WatchVisualLoadHintRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'WatchVisualLoadHintRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'clientId')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  WatchVisualLoadHintRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  WatchVisualLoadHintRequest copyWith(
          void Function(WatchVisualLoadHintRequest) updates) =>
      super.copyWith(
              (message) => updates(message as WatchVisualLoadHintRequest))
          as WatchVisualLoadHintRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static WatchVisualLoadHintRequest create() => WatchVisualLoadHintRequest._();
  @$core.override
  WatchVisualLoadHintRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static WatchVisualLoadHintRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<WatchVisualLoadHintRequest>(create);
  static WatchVisualLoadHintRequest? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get clientId => $_getSZ(0);
  @$pb.TagNumber(1)
  set clientId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasClientId() => $_has(0);
  @$pb.TagNumber(1)
  void clearClientId() => $_clearField(1);
}

class VisualLoadHintProto extends $pb.GeneratedMessage {
  factory VisualLoadHintProto({
    $core.String? clientId,
    $core.String? tier,
    $core.String? reason,
    $core.String? timestamp,
  }) {
    final result = create();
    if (clientId != null) result.clientId = clientId;
    if (tier != null) result.tier = tier;
    if (reason != null) result.reason = reason;
    if (timestamp != null) result.timestamp = timestamp;
    return result;
  }

  VisualLoadHintProto._();

  factory VisualLoadHintProto.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory VisualLoadHintProto.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'VisualLoadHintProto',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'clientId')
    ..aOS(2, _omitFieldNames ? '' : 'tier')
    ..aOS(3, _omitFieldNames ? '' : 'reason')
    ..aOS(4, _omitFieldNames ? '' : 'timestamp')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  VisualLoadHintProto clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  VisualLoadHintProto copyWith(void Function(VisualLoadHintProto) updates) =>
      super.copyWith((message) => updates(message as VisualLoadHintProto))
          as VisualLoadHintProto;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static VisualLoadHintProto create() => VisualLoadHintProto._();
  @$core.override
  VisualLoadHintProto createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static VisualLoadHintProto getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<VisualLoadHintProto>(create);
  static VisualLoadHintProto? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get clientId => $_getSZ(0);
  @$pb.TagNumber(1)
  set clientId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasClientId() => $_has(0);
  @$pb.TagNumber(1)
  void clearClientId() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get tier => $_getSZ(1);
  @$pb.TagNumber(2)
  set tier($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasTier() => $_has(1);
  @$pb.TagNumber(2)
  void clearTier() => $_clearField(2);

  @$pb.TagNumber(3)
  $core.String get reason => $_getSZ(2);
  @$pb.TagNumber(3)
  set reason($core.String value) => $_setString(2, value);
  @$pb.TagNumber(3)
  $core.bool hasReason() => $_has(2);
  @$pb.TagNumber(3)
  void clearReason() => $_clearField(3);

  @$pb.TagNumber(4)
  $core.String get timestamp => $_getSZ(3);
  @$pb.TagNumber(4)
  set timestamp($core.String value) => $_setString(3, value);
  @$pb.TagNumber(4)
  $core.bool hasTimestamp() => $_has(3);
  @$pb.TagNumber(4)
  void clearTimestamp() => $_clearField(4);
}

class GetLatestCardRequest extends $pb.GeneratedMessage {
  factory GetLatestCardRequest({
    $core.String? neuronId,
  }) {
    final result = create();
    if (neuronId != null) result.neuronId = neuronId;
    return result;
  }

  GetLatestCardRequest._();

  factory GetLatestCardRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory GetLatestCardRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'GetLatestCardRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'neuronId')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  GetLatestCardRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  GetLatestCardRequest copyWith(void Function(GetLatestCardRequest) updates) =>
      super.copyWith((message) => updates(message as GetLatestCardRequest))
          as GetLatestCardRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static GetLatestCardRequest create() => GetLatestCardRequest._();
  @$core.override
  GetLatestCardRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static GetLatestCardRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<GetLatestCardRequest>(create);
  static GetLatestCardRequest? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get neuronId => $_getSZ(0);
  @$pb.TagNumber(1)
  set neuronId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasNeuronId() => $_has(0);
  @$pb.TagNumber(1)
  void clearNeuronId() => $_clearField(1);
}

class GetLatestCardReply extends $pb.GeneratedMessage {
  factory GetLatestCardReply({
    RfwCardEnvelope? card,
    $core.bool? hasCard_2,
  }) {
    final result = create();
    if (card != null) result.card = card;
    if (hasCard_2 != null) result.hasCard_2 = hasCard_2;
    return result;
  }

  GetLatestCardReply._();

  factory GetLatestCardReply.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory GetLatestCardReply.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'GetLatestCardReply',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOM<RfwCardEnvelope>(1, _omitFieldNames ? '' : 'card',
        subBuilder: RfwCardEnvelope.create)
    ..aOB(2, _omitFieldNames ? '' : 'hasCard')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  GetLatestCardReply clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  GetLatestCardReply copyWith(void Function(GetLatestCardReply) updates) =>
      super.copyWith((message) => updates(message as GetLatestCardReply))
          as GetLatestCardReply;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static GetLatestCardReply create() => GetLatestCardReply._();
  @$core.override
  GetLatestCardReply createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static GetLatestCardReply getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<GetLatestCardReply>(create);
  static GetLatestCardReply? _defaultInstance;

  @$pb.TagNumber(1)
  RfwCardEnvelope get card => $_getN(0);
  @$pb.TagNumber(1)
  set card(RfwCardEnvelope value) => $_setField(1, value);
  @$pb.TagNumber(1)
  $core.bool hasCard() => $_has(0);
  @$pb.TagNumber(1)
  void clearCard() => $_clearField(1);
  @$pb.TagNumber(1)
  RfwCardEnvelope ensureCard() => $_ensure(0);

  @$pb.TagNumber(2)
  $core.bool get hasCard_2 => $_getBF(1);
  @$pb.TagNumber(2)
  set hasCard_2($core.bool value) => $_setBool(1, value);
  @$pb.TagNumber(2)
  $core.bool hasHasCard_2() => $_has(1);
  @$pb.TagNumber(2)
  void clearHasCard_2() => $_clearField(2);
}

class RfwLayoutRequest extends $pb.GeneratedMessage {
  factory RfwLayoutRequest({
    $core.String? layoutName,
    $core.String? neuronId,
  }) {
    final result = create();
    if (layoutName != null) result.layoutName = layoutName;
    if (neuronId != null) result.neuronId = neuronId;
    return result;
  }

  RfwLayoutRequest._();

  factory RfwLayoutRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory RfwLayoutRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'RfwLayoutRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'layoutName')
    ..aOS(2, _omitFieldNames ? '' : 'neuronId')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  RfwLayoutRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  RfwLayoutRequest copyWith(void Function(RfwLayoutRequest) updates) =>
      super.copyWith((message) => updates(message as RfwLayoutRequest))
          as RfwLayoutRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static RfwLayoutRequest create() => RfwLayoutRequest._();
  @$core.override
  RfwLayoutRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static RfwLayoutRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<RfwLayoutRequest>(create);
  static RfwLayoutRequest? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get layoutName => $_getSZ(0);
  @$pb.TagNumber(1)
  set layoutName($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasLayoutName() => $_has(0);
  @$pb.TagNumber(1)
  void clearLayoutName() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get neuronId => $_getSZ(1);
  @$pb.TagNumber(2)
  set neuronId($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasNeuronId() => $_has(1);
  @$pb.TagNumber(2)
  void clearNeuronId() => $_clearField(2);
}

class RfwLayoutReply extends $pb.GeneratedMessage {
  factory RfwLayoutReply({
    $core.String? rfwTemplate,
    $core.String? dataJson,
  }) {
    final result = create();
    if (rfwTemplate != null) result.rfwTemplate = rfwTemplate;
    if (dataJson != null) result.dataJson = dataJson;
    return result;
  }

  RfwLayoutReply._();

  factory RfwLayoutReply.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory RfwLayoutReply.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'RfwLayoutReply',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'rfwTemplate')
    ..aOS(2, _omitFieldNames ? '' : 'dataJson')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  RfwLayoutReply clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  RfwLayoutReply copyWith(void Function(RfwLayoutReply) updates) =>
      super.copyWith((message) => updates(message as RfwLayoutReply))
          as RfwLayoutReply;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static RfwLayoutReply create() => RfwLayoutReply._();
  @$core.override
  RfwLayoutReply createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static RfwLayoutReply getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<RfwLayoutReply>(create);
  static RfwLayoutReply? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get rfwTemplate => $_getSZ(0);
  @$pb.TagNumber(1)
  set rfwTemplate($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasRfwTemplate() => $_has(0);
  @$pb.TagNumber(1)
  void clearRfwTemplate() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get dataJson => $_getSZ(1);
  @$pb.TagNumber(2)
  set dataJson($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasDataJson() => $_has(1);
  @$pb.TagNumber(2)
  void clearDataJson() => $_clearField(2);
}

const $core.bool _omitFieldNames =
    $core.bool.fromEnvironment('protobuf.omit_field_names');
const $core.bool _omitMessageNames =
    $core.bool.fromEnvironment('protobuf.omit_message_names');
