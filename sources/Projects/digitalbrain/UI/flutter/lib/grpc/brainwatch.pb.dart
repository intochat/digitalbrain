// This is a generated file - do not edit.
//
// Generated from brainwatch.proto.

// @dart = 3.3

// ignore_for_file: annotate_overrides, camel_case_types, comment_references
// ignore_for_file: constant_identifier_names
// ignore_for_file: curly_braces_in_flow_control_structures
// ignore_for_file: deprecated_member_use_from_same_package, library_prefixes
// ignore_for_file: non_constant_identifier_names, prefer_relative_imports

import 'dart:core' as $core;

import 'package:fixnum/fixnum.dart' as $fixnum;
import 'package:protobuf/protobuf.dart' as $pb;
import 'package:protobuf/well_known_types/google/protobuf/timestamp.pb.dart'
    as $1;

export 'package:protobuf/protobuf.dart' show GeneratedMessageGenericExtensions;

class WatchSynapsesRequest extends $pb.GeneratedMessage {
  factory WatchSynapsesRequest({
    $core.String? brainId,
  }) {
    final result = create();
    if (brainId != null) result.brainId = brainId;
    return result;
  }

  WatchSynapsesRequest._();

  factory WatchSynapsesRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory WatchSynapsesRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'WatchSynapsesRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'brainId')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  WatchSynapsesRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  WatchSynapsesRequest copyWith(void Function(WatchSynapsesRequest) updates) =>
      super.copyWith((message) => updates(message as WatchSynapsesRequest))
          as WatchSynapsesRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static WatchSynapsesRequest create() => WatchSynapsesRequest._();
  @$core.override
  WatchSynapsesRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static WatchSynapsesRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<WatchSynapsesRequest>(create);
  static WatchSynapsesRequest? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get brainId => $_getSZ(0);
  @$pb.TagNumber(1)
  set brainId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasBrainId() => $_has(0);
  @$pb.TagNumber(1)
  void clearBrainId() => $_clearField(1);
}

class SnapshotRequest extends $pb.GeneratedMessage {
  factory SnapshotRequest({
    $1.Timestamp? since,
  }) {
    final result = create();
    if (since != null) result.since = since;
    return result;
  }

  SnapshotRequest._();

  factory SnapshotRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory SnapshotRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'SnapshotRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOM<$1.Timestamp>(1, _omitFieldNames ? '' : 'since',
        subBuilder: $1.Timestamp.create)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SnapshotRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SnapshotRequest copyWith(void Function(SnapshotRequest) updates) =>
      super.copyWith((message) => updates(message as SnapshotRequest))
          as SnapshotRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static SnapshotRequest create() => SnapshotRequest._();
  @$core.override
  SnapshotRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static SnapshotRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<SnapshotRequest>(create);
  static SnapshotRequest? _defaultInstance;

  @$pb.TagNumber(1)
  $1.Timestamp get since => $_getN(0);
  @$pb.TagNumber(1)
  set since($1.Timestamp value) => $_setField(1, value);
  @$pb.TagNumber(1)
  $core.bool hasSince() => $_has(0);
  @$pb.TagNumber(1)
  void clearSince() => $_clearField(1);
  @$pb.TagNumber(1)
  $1.Timestamp ensureSince() => $_ensure(0);
}

class SnapshotResponse extends $pb.GeneratedMessage {
  factory SnapshotResponse({
    $core.Iterable<NeuronNode>? nodes,
    $core.Iterable<SynapseEdge>? edges,
    $fixnum.Int64? cursor,
  }) {
    final result = create();
    if (nodes != null) result.nodes.addAll(nodes);
    if (edges != null) result.edges.addAll(edges);
    if (cursor != null) result.cursor = cursor;
    return result;
  }

  SnapshotResponse._();

  factory SnapshotResponse.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory SnapshotResponse.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'SnapshotResponse',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..pPM<NeuronNode>(1, _omitFieldNames ? '' : 'nodes',
        subBuilder: NeuronNode.create)
    ..pPM<SynapseEdge>(2, _omitFieldNames ? '' : 'edges',
        subBuilder: SynapseEdge.create)
    ..aInt64(3, _omitFieldNames ? '' : 'cursor')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SnapshotResponse clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SnapshotResponse copyWith(void Function(SnapshotResponse) updates) =>
      super.copyWith((message) => updates(message as SnapshotResponse))
          as SnapshotResponse;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static SnapshotResponse create() => SnapshotResponse._();
  @$core.override
  SnapshotResponse createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static SnapshotResponse getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<SnapshotResponse>(create);
  static SnapshotResponse? _defaultInstance;

  @$pb.TagNumber(1)
  $pb.PbList<NeuronNode> get nodes => $_getList(0);

  @$pb.TagNumber(2)
  $pb.PbList<SynapseEdge> get edges => $_getList(1);

  @$pb.TagNumber(3)
  $fixnum.Int64 get cursor => $_getI64(2);
  @$pb.TagNumber(3)
  set cursor($fixnum.Int64 value) => $_setInt64(2, value);
  @$pb.TagNumber(3)
  $core.bool hasCursor() => $_has(2);
  @$pb.TagNumber(3)
  void clearCursor() => $_clearField(3);
}

class WatchSinceRequest extends $pb.GeneratedMessage {
  factory WatchSinceRequest({
    $fixnum.Int64? cursor,
  }) {
    final result = create();
    if (cursor != null) result.cursor = cursor;
    return result;
  }

  WatchSinceRequest._();

  factory WatchSinceRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory WatchSinceRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'WatchSinceRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aInt64(1, _omitFieldNames ? '' : 'cursor')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  WatchSinceRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  WatchSinceRequest copyWith(void Function(WatchSinceRequest) updates) =>
      super.copyWith((message) => updates(message as WatchSinceRequest))
          as WatchSinceRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static WatchSinceRequest create() => WatchSinceRequest._();
  @$core.override
  WatchSinceRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static WatchSinceRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<WatchSinceRequest>(create);
  static WatchSinceRequest? _defaultInstance;

  @$pb.TagNumber(1)
  $fixnum.Int64 get cursor => $_getI64(0);
  @$pb.TagNumber(1)
  set cursor($fixnum.Int64 value) => $_setInt64(0, value);
  @$pb.TagNumber(1)
  $core.bool hasCursor() => $_has(0);
  @$pb.TagNumber(1)
  void clearCursor() => $_clearField(1);
}

class WatchSinceResponse extends $pb.GeneratedMessage {
  factory WatchSinceResponse({
    $core.Iterable<NeuronNode>? newNodes,
    $core.Iterable<SynapseEdge>? newEdges,
    $fixnum.Int64? cursor,
  }) {
    final result = create();
    if (newNodes != null) result.newNodes.addAll(newNodes);
    if (newEdges != null) result.newEdges.addAll(newEdges);
    if (cursor != null) result.cursor = cursor;
    return result;
  }

  WatchSinceResponse._();

  factory WatchSinceResponse.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory WatchSinceResponse.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'WatchSinceResponse',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..pPM<NeuronNode>(1, _omitFieldNames ? '' : 'newNodes',
        subBuilder: NeuronNode.create)
    ..pPM<SynapseEdge>(2, _omitFieldNames ? '' : 'newEdges',
        subBuilder: SynapseEdge.create)
    ..aInt64(3, _omitFieldNames ? '' : 'cursor')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  WatchSinceResponse clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  WatchSinceResponse copyWith(void Function(WatchSinceResponse) updates) =>
      super.copyWith((message) => updates(message as WatchSinceResponse))
          as WatchSinceResponse;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static WatchSinceResponse create() => WatchSinceResponse._();
  @$core.override
  WatchSinceResponse createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static WatchSinceResponse getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<WatchSinceResponse>(create);
  static WatchSinceResponse? _defaultInstance;

  @$pb.TagNumber(1)
  $pb.PbList<NeuronNode> get newNodes => $_getList(0);

  @$pb.TagNumber(2)
  $pb.PbList<SynapseEdge> get newEdges => $_getList(1);

  @$pb.TagNumber(3)
  $fixnum.Int64 get cursor => $_getI64(2);
  @$pb.TagNumber(3)
  set cursor($fixnum.Int64 value) => $_setInt64(2, value);
  @$pb.TagNumber(3)
  $core.bool hasCursor() => $_has(2);
  @$pb.TagNumber(3)
  void clearCursor() => $_clearField(3);
}

class NeuronDetailRequest extends $pb.GeneratedMessage {
  factory NeuronDetailRequest({
    $core.String? neuronId,
    $1.Timestamp? since,
  }) {
    final result = create();
    if (neuronId != null) result.neuronId = neuronId;
    if (since != null) result.since = since;
    return result;
  }

  NeuronDetailRequest._();

  factory NeuronDetailRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory NeuronDetailRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'NeuronDetailRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'neuronId')
    ..aOM<$1.Timestamp>(2, _omitFieldNames ? '' : 'since',
        subBuilder: $1.Timestamp.create)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  NeuronDetailRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  NeuronDetailRequest copyWith(void Function(NeuronDetailRequest) updates) =>
      super.copyWith((message) => updates(message as NeuronDetailRequest))
          as NeuronDetailRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static NeuronDetailRequest create() => NeuronDetailRequest._();
  @$core.override
  NeuronDetailRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static NeuronDetailRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<NeuronDetailRequest>(create);
  static NeuronDetailRequest? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get neuronId => $_getSZ(0);
  @$pb.TagNumber(1)
  set neuronId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasNeuronId() => $_has(0);
  @$pb.TagNumber(1)
  void clearNeuronId() => $_clearField(1);

  @$pb.TagNumber(2)
  $1.Timestamp get since => $_getN(1);
  @$pb.TagNumber(2)
  set since($1.Timestamp value) => $_setField(2, value);
  @$pb.TagNumber(2)
  $core.bool hasSince() => $_has(1);
  @$pb.TagNumber(2)
  void clearSince() => $_clearField(2);
  @$pb.TagNumber(2)
  $1.Timestamp ensureSince() => $_ensure(1);
}

class NeuronDetailResponse extends $pb.GeneratedMessage {
  factory NeuronDetailResponse({
    $core.String? neuronId,
    $1.Timestamp? firstSeenAt,
    $1.Timestamp? lastSeenAt,
    $core.Iterable<SynapseEdge>? recent,
  }) {
    final result = create();
    if (neuronId != null) result.neuronId = neuronId;
    if (firstSeenAt != null) result.firstSeenAt = firstSeenAt;
    if (lastSeenAt != null) result.lastSeenAt = lastSeenAt;
    if (recent != null) result.recent.addAll(recent);
    return result;
  }

  NeuronDetailResponse._();

  factory NeuronDetailResponse.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory NeuronDetailResponse.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'NeuronDetailResponse',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'neuronId')
    ..aOM<$1.Timestamp>(2, _omitFieldNames ? '' : 'firstSeenAt',
        subBuilder: $1.Timestamp.create)
    ..aOM<$1.Timestamp>(3, _omitFieldNames ? '' : 'lastSeenAt',
        subBuilder: $1.Timestamp.create)
    ..pPM<SynapseEdge>(4, _omitFieldNames ? '' : 'recent',
        subBuilder: SynapseEdge.create)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  NeuronDetailResponse clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  NeuronDetailResponse copyWith(void Function(NeuronDetailResponse) updates) =>
      super.copyWith((message) => updates(message as NeuronDetailResponse))
          as NeuronDetailResponse;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static NeuronDetailResponse create() => NeuronDetailResponse._();
  @$core.override
  NeuronDetailResponse createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static NeuronDetailResponse getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<NeuronDetailResponse>(create);
  static NeuronDetailResponse? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get neuronId => $_getSZ(0);
  @$pb.TagNumber(1)
  set neuronId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasNeuronId() => $_has(0);
  @$pb.TagNumber(1)
  void clearNeuronId() => $_clearField(1);

  @$pb.TagNumber(2)
  $1.Timestamp get firstSeenAt => $_getN(1);
  @$pb.TagNumber(2)
  set firstSeenAt($1.Timestamp value) => $_setField(2, value);
  @$pb.TagNumber(2)
  $core.bool hasFirstSeenAt() => $_has(1);
  @$pb.TagNumber(2)
  void clearFirstSeenAt() => $_clearField(2);
  @$pb.TagNumber(2)
  $1.Timestamp ensureFirstSeenAt() => $_ensure(1);

  @$pb.TagNumber(3)
  $1.Timestamp get lastSeenAt => $_getN(2);
  @$pb.TagNumber(3)
  set lastSeenAt($1.Timestamp value) => $_setField(3, value);
  @$pb.TagNumber(3)
  $core.bool hasLastSeenAt() => $_has(2);
  @$pb.TagNumber(3)
  void clearLastSeenAt() => $_clearField(3);
  @$pb.TagNumber(3)
  $1.Timestamp ensureLastSeenAt() => $_ensure(2);

  @$pb.TagNumber(4)
  $pb.PbList<SynapseEdge> get recent => $_getList(3);
}

class SynapseDetailRequest extends $pb.GeneratedMessage {
  factory SynapseDetailRequest({
    $core.String? correlationId,
  }) {
    final result = create();
    if (correlationId != null) result.correlationId = correlationId;
    return result;
  }

  SynapseDetailRequest._();

  factory SynapseDetailRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory SynapseDetailRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'SynapseDetailRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'correlationId')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SynapseDetailRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SynapseDetailRequest copyWith(void Function(SynapseDetailRequest) updates) =>
      super.copyWith((message) => updates(message as SynapseDetailRequest))
          as SynapseDetailRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static SynapseDetailRequest create() => SynapseDetailRequest._();
  @$core.override
  SynapseDetailRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static SynapseDetailRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<SynapseDetailRequest>(create);
  static SynapseDetailRequest? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get correlationId => $_getSZ(0);
  @$pb.TagNumber(1)
  set correlationId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasCorrelationId() => $_has(0);
  @$pb.TagNumber(1)
  void clearCorrelationId() => $_clearField(1);
}

class SynapseDetailResponse extends $pb.GeneratedMessage {
  factory SynapseDetailResponse({
    $core.Iterable<SynapseEdge>? chain,
  }) {
    final result = create();
    if (chain != null) result.chain.addAll(chain);
    return result;
  }

  SynapseDetailResponse._();

  factory SynapseDetailResponse.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory SynapseDetailResponse.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'SynapseDetailResponse',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..pPM<SynapseEdge>(1, _omitFieldNames ? '' : 'chain',
        subBuilder: SynapseEdge.create)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SynapseDetailResponse clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SynapseDetailResponse copyWith(
          void Function(SynapseDetailResponse) updates) =>
      super.copyWith((message) => updates(message as SynapseDetailResponse))
          as SynapseDetailResponse;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static SynapseDetailResponse create() => SynapseDetailResponse._();
  @$core.override
  SynapseDetailResponse createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static SynapseDetailResponse getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<SynapseDetailResponse>(create);
  static SynapseDetailResponse? _defaultInstance;

  @$pb.TagNumber(1)
  $pb.PbList<SynapseEdge> get chain => $_getList(0);
}

class NeuronFeatureRequest extends $pb.GeneratedMessage {
  factory NeuronFeatureRequest({
    $core.String? neuronId,
  }) {
    final result = create();
    if (neuronId != null) result.neuronId = neuronId;
    return result;
  }

  NeuronFeatureRequest._();

  factory NeuronFeatureRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory NeuronFeatureRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'NeuronFeatureRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'neuronId')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  NeuronFeatureRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  NeuronFeatureRequest copyWith(void Function(NeuronFeatureRequest) updates) =>
      super.copyWith((message) => updates(message as NeuronFeatureRequest))
          as NeuronFeatureRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static NeuronFeatureRequest create() => NeuronFeatureRequest._();
  @$core.override
  NeuronFeatureRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static NeuronFeatureRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<NeuronFeatureRequest>(create);
  static NeuronFeatureRequest? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get neuronId => $_getSZ(0);
  @$pb.TagNumber(1)
  set neuronId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasNeuronId() => $_has(0);
  @$pb.TagNumber(1)
  void clearNeuronId() => $_clearField(1);
}

class NeuronFeatureResponse extends $pb.GeneratedMessage {
  factory NeuronFeatureResponse({
    $core.String? featureText,
    $core.String? sourceFile,
  }) {
    final result = create();
    if (featureText != null) result.featureText = featureText;
    if (sourceFile != null) result.sourceFile = sourceFile;
    return result;
  }

  NeuronFeatureResponse._();

  factory NeuronFeatureResponse.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory NeuronFeatureResponse.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'NeuronFeatureResponse',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'featureText')
    ..aOS(2, _omitFieldNames ? '' : 'sourceFile')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  NeuronFeatureResponse clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  NeuronFeatureResponse copyWith(
          void Function(NeuronFeatureResponse) updates) =>
      super.copyWith((message) => updates(message as NeuronFeatureResponse))
          as NeuronFeatureResponse;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static NeuronFeatureResponse create() => NeuronFeatureResponse._();
  @$core.override
  NeuronFeatureResponse createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static NeuronFeatureResponse getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<NeuronFeatureResponse>(create);
  static NeuronFeatureResponse? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get featureText => $_getSZ(0);
  @$pb.TagNumber(1)
  set featureText($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasFeatureText() => $_has(0);
  @$pb.TagNumber(1)
  void clearFeatureText() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get sourceFile => $_getSZ(1);
  @$pb.TagNumber(2)
  set sourceFile($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasSourceFile() => $_has(1);
  @$pb.TagNumber(2)
  void clearSourceFile() => $_clearField(2);
}

class NeuronNode extends $pb.GeneratedMessage {
  factory NeuronNode({
    $core.String? id,
    $1.Timestamp? firstSeenAt,
    $1.Timestamp? lastSeenAt,
    $core.String? domain,
  }) {
    final result = create();
    if (id != null) result.id = id;
    if (firstSeenAt != null) result.firstSeenAt = firstSeenAt;
    if (lastSeenAt != null) result.lastSeenAt = lastSeenAt;
    if (domain != null) result.domain = domain;
    return result;
  }

  NeuronNode._();

  factory NeuronNode.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory NeuronNode.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'NeuronNode',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'id')
    ..aOM<$1.Timestamp>(2, _omitFieldNames ? '' : 'firstSeenAt',
        subBuilder: $1.Timestamp.create)
    ..aOM<$1.Timestamp>(3, _omitFieldNames ? '' : 'lastSeenAt',
        subBuilder: $1.Timestamp.create)
    ..aOS(5, _omitFieldNames ? '' : 'domain')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  NeuronNode clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  NeuronNode copyWith(void Function(NeuronNode) updates) =>
      super.copyWith((message) => updates(message as NeuronNode)) as NeuronNode;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static NeuronNode create() => NeuronNode._();
  @$core.override
  NeuronNode createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static NeuronNode getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<NeuronNode>(create);
  static NeuronNode? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get id => $_getSZ(0);
  @$pb.TagNumber(1)
  set id($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasId() => $_has(0);
  @$pb.TagNumber(1)
  void clearId() => $_clearField(1);

  @$pb.TagNumber(2)
  $1.Timestamp get firstSeenAt => $_getN(1);
  @$pb.TagNumber(2)
  set firstSeenAt($1.Timestamp value) => $_setField(2, value);
  @$pb.TagNumber(2)
  $core.bool hasFirstSeenAt() => $_has(1);
  @$pb.TagNumber(2)
  void clearFirstSeenAt() => $_clearField(2);
  @$pb.TagNumber(2)
  $1.Timestamp ensureFirstSeenAt() => $_ensure(1);

  @$pb.TagNumber(3)
  $1.Timestamp get lastSeenAt => $_getN(2);
  @$pb.TagNumber(3)
  set lastSeenAt($1.Timestamp value) => $_setField(3, value);
  @$pb.TagNumber(3)
  $core.bool hasLastSeenAt() => $_has(2);
  @$pb.TagNumber(3)
  void clearLastSeenAt() => $_clearField(3);
  @$pb.TagNumber(3)
  $1.Timestamp ensureLastSeenAt() => $_ensure(2);

  @$pb.TagNumber(5)
  $core.String get domain => $_getSZ(3);
  @$pb.TagNumber(5)
  set domain($core.String value) => $_setString(3, value);
  @$pb.TagNumber(5)
  $core.bool hasDomain() => $_has(3);
  @$pb.TagNumber(5)
  void clearDomain() => $_clearField(5);
}

class SynapseEdge extends $pb.GeneratedMessage {
  factory SynapseEdge({
    $1.Timestamp? at,
    $core.String? fromId,
    $core.String? toId,
    $core.String? typeName,
    $core.String? methodName,
    $core.String? correlationId,
    $core.List<$core.int>? payload,
  }) {
    final result = create();
    if (at != null) result.at = at;
    if (fromId != null) result.fromId = fromId;
    if (toId != null) result.toId = toId;
    if (typeName != null) result.typeName = typeName;
    if (methodName != null) result.methodName = methodName;
    if (correlationId != null) result.correlationId = correlationId;
    if (payload != null) result.payload = payload;
    return result;
  }

  SynapseEdge._();

  factory SynapseEdge.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory SynapseEdge.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'SynapseEdge',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOM<$1.Timestamp>(1, _omitFieldNames ? '' : 'at',
        subBuilder: $1.Timestamp.create)
    ..aOS(2, _omitFieldNames ? '' : 'fromId')
    ..aOS(3, _omitFieldNames ? '' : 'toId')
    ..aOS(4, _omitFieldNames ? '' : 'typeName')
    ..aOS(5, _omitFieldNames ? '' : 'methodName')
    ..aOS(6, _omitFieldNames ? '' : 'correlationId')
    ..a<$core.List<$core.int>>(
        7, _omitFieldNames ? '' : 'payload', $pb.PbFieldType.OY)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SynapseEdge clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  SynapseEdge copyWith(void Function(SynapseEdge) updates) =>
      super.copyWith((message) => updates(message as SynapseEdge))
          as SynapseEdge;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static SynapseEdge create() => SynapseEdge._();
  @$core.override
  SynapseEdge createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static SynapseEdge getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<SynapseEdge>(create);
  static SynapseEdge? _defaultInstance;

  @$pb.TagNumber(1)
  $1.Timestamp get at => $_getN(0);
  @$pb.TagNumber(1)
  set at($1.Timestamp value) => $_setField(1, value);
  @$pb.TagNumber(1)
  $core.bool hasAt() => $_has(0);
  @$pb.TagNumber(1)
  void clearAt() => $_clearField(1);
  @$pb.TagNumber(1)
  $1.Timestamp ensureAt() => $_ensure(0);

  @$pb.TagNumber(2)
  $core.String get fromId => $_getSZ(1);
  @$pb.TagNumber(2)
  set fromId($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasFromId() => $_has(1);
  @$pb.TagNumber(2)
  void clearFromId() => $_clearField(2);

  @$pb.TagNumber(3)
  $core.String get toId => $_getSZ(2);
  @$pb.TagNumber(3)
  set toId($core.String value) => $_setString(2, value);
  @$pb.TagNumber(3)
  $core.bool hasToId() => $_has(2);
  @$pb.TagNumber(3)
  void clearToId() => $_clearField(3);

  @$pb.TagNumber(4)
  $core.String get typeName => $_getSZ(3);
  @$pb.TagNumber(4)
  set typeName($core.String value) => $_setString(3, value);
  @$pb.TagNumber(4)
  $core.bool hasTypeName() => $_has(3);
  @$pb.TagNumber(4)
  void clearTypeName() => $_clearField(4);

  @$pb.TagNumber(5)
  $core.String get methodName => $_getSZ(4);
  @$pb.TagNumber(5)
  set methodName($core.String value) => $_setString(4, value);
  @$pb.TagNumber(5)
  $core.bool hasMethodName() => $_has(4);
  @$pb.TagNumber(5)
  void clearMethodName() => $_clearField(5);

  @$pb.TagNumber(6)
  $core.String get correlationId => $_getSZ(5);
  @$pb.TagNumber(6)
  set correlationId($core.String value) => $_setString(5, value);
  @$pb.TagNumber(6)
  $core.bool hasCorrelationId() => $_has(5);
  @$pb.TagNumber(6)
  void clearCorrelationId() => $_clearField(6);

  @$pb.TagNumber(7)
  $core.List<$core.int> get payload => $_getN(6);
  @$pb.TagNumber(7)
  set payload($core.List<$core.int> value) => $_setBytes(6, value);
  @$pb.TagNumber(7)
  $core.bool hasPayload() => $_has(6);
  @$pb.TagNumber(7)
  void clearPayload() => $_clearField(7);
}

const $core.bool _omitFieldNames =
    $core.bool.fromEnvironment('protobuf.omit_field_names');
const $core.bool _omitMessageNames =
    $core.bool.fromEnvironment('protobuf.omit_message_names');
