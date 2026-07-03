// This is a generated file - do not edit.
//
// Generated from brainregistry.proto.

// @dart = 3.3

// ignore_for_file: annotate_overrides, camel_case_types, comment_references
// ignore_for_file: constant_identifier_names
// ignore_for_file: curly_braces_in_flow_control_structures
// ignore_for_file: deprecated_member_use_from_same_package, library_prefixes
// ignore_for_file: non_constant_identifier_names, prefer_relative_imports

import 'dart:core' as $core;

import 'package:protobuf/protobuf.dart' as $pb;
import 'package:protobuf/well_known_types/google/protobuf/timestamp.pb.dart'
    as $1;

export 'package:protobuf/protobuf.dart' show GeneratedMessageGenericExtensions;

class ListBrainsRequest extends $pb.GeneratedMessage {
  factory ListBrainsRequest() => create();

  ListBrainsRequest._();

  factory ListBrainsRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory ListBrainsRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'ListBrainsRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ListBrainsRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ListBrainsRequest copyWith(void Function(ListBrainsRequest) updates) =>
      super.copyWith((message) => updates(message as ListBrainsRequest))
          as ListBrainsRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static ListBrainsRequest create() => ListBrainsRequest._();
  @$core.override
  ListBrainsRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static ListBrainsRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<ListBrainsRequest>(create);
  static ListBrainsRequest? _defaultInstance;
}

class BrainsResult extends $pb.GeneratedMessage {
  factory BrainsResult({
    $core.Iterable<BrainSummary>? brains,
  }) {
    final result = create();
    if (brains != null) result.brains.addAll(brains);
    return result;
  }

  BrainsResult._();

  factory BrainsResult.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory BrainsResult.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'BrainsResult',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..pPM<BrainSummary>(1, _omitFieldNames ? '' : 'brains',
        subBuilder: BrainSummary.create)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  BrainsResult clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  BrainsResult copyWith(void Function(BrainsResult) updates) =>
      super.copyWith((message) => updates(message as BrainsResult))
          as BrainsResult;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static BrainsResult create() => BrainsResult._();
  @$core.override
  BrainsResult createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static BrainsResult getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<BrainsResult>(create);
  static BrainsResult? _defaultInstance;

  @$pb.TagNumber(1)
  $pb.PbList<BrainSummary> get brains => $_getList(0);
}

class WatchActivityRequest extends $pb.GeneratedMessage {
  factory WatchActivityRequest() => create();

  WatchActivityRequest._();

  factory WatchActivityRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory WatchActivityRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'WatchActivityRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  WatchActivityRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  WatchActivityRequest copyWith(void Function(WatchActivityRequest) updates) =>
      super.copyWith((message) => updates(message as WatchActivityRequest))
          as WatchActivityRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static WatchActivityRequest create() => WatchActivityRequest._();
  @$core.override
  WatchActivityRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static WatchActivityRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<WatchActivityRequest>(create);
  static WatchActivityRequest? _defaultInstance;
}

class BrainActivityDelta extends $pb.GeneratedMessage {
  factory BrainActivityDelta({
    $core.String? brainId,
    $core.double? synapsesPerSecond,
    $core.int? neuronCountDelta,
    $core.String? versionBump,
  }) {
    final result = create();
    if (brainId != null) result.brainId = brainId;
    if (synapsesPerSecond != null) result.synapsesPerSecond = synapsesPerSecond;
    if (neuronCountDelta != null) result.neuronCountDelta = neuronCountDelta;
    if (versionBump != null) result.versionBump = versionBump;
    return result;
  }

  BrainActivityDelta._();

  factory BrainActivityDelta.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory BrainActivityDelta.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'BrainActivityDelta',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'brainId')
    ..aD(2, _omitFieldNames ? '' : 'synapsesPerSecond')
    ..aI(3, _omitFieldNames ? '' : 'neuronCountDelta')
    ..aOS(4, _omitFieldNames ? '' : 'versionBump')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  BrainActivityDelta clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  BrainActivityDelta copyWith(void Function(BrainActivityDelta) updates) =>
      super.copyWith((message) => updates(message as BrainActivityDelta))
          as BrainActivityDelta;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static BrainActivityDelta create() => BrainActivityDelta._();
  @$core.override
  BrainActivityDelta createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static BrainActivityDelta getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<BrainActivityDelta>(create);
  static BrainActivityDelta? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get brainId => $_getSZ(0);
  @$pb.TagNumber(1)
  set brainId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasBrainId() => $_has(0);
  @$pb.TagNumber(1)
  void clearBrainId() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.double get synapsesPerSecond => $_getN(1);
  @$pb.TagNumber(2)
  set synapsesPerSecond($core.double value) => $_setDouble(1, value);
  @$pb.TagNumber(2)
  $core.bool hasSynapsesPerSecond() => $_has(1);
  @$pb.TagNumber(2)
  void clearSynapsesPerSecond() => $_clearField(2);

  @$pb.TagNumber(3)
  $core.int get neuronCountDelta => $_getIZ(2);
  @$pb.TagNumber(3)
  set neuronCountDelta($core.int value) => $_setSignedInt32(2, value);
  @$pb.TagNumber(3)
  $core.bool hasNeuronCountDelta() => $_has(2);
  @$pb.TagNumber(3)
  void clearNeuronCountDelta() => $_clearField(3);

  @$pb.TagNumber(4)
  $core.String get versionBump => $_getSZ(3);
  @$pb.TagNumber(4)
  set versionBump($core.String value) => $_setString(3, value);
  @$pb.TagNumber(4)
  $core.bool hasVersionBump() => $_has(3);
  @$pb.TagNumber(4)
  void clearVersionBump() => $_clearField(4);
}

class CreateBrainCommand extends $pb.GeneratedMessage {
  factory CreateBrainCommand({
    $core.String? name,
    $core.String? seedTemplate,
  }) {
    final result = create();
    if (name != null) result.name = name;
    if (seedTemplate != null) result.seedTemplate = seedTemplate;
    return result;
  }

  CreateBrainCommand._();

  factory CreateBrainCommand.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory CreateBrainCommand.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'CreateBrainCommand',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'name')
    ..aOS(2, _omitFieldNames ? '' : 'seedTemplate')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  CreateBrainCommand clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  CreateBrainCommand copyWith(void Function(CreateBrainCommand) updates) =>
      super.copyWith((message) => updates(message as CreateBrainCommand))
          as CreateBrainCommand;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static CreateBrainCommand create() => CreateBrainCommand._();
  @$core.override
  CreateBrainCommand createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static CreateBrainCommand getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<CreateBrainCommand>(create);
  static CreateBrainCommand? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get name => $_getSZ(0);
  @$pb.TagNumber(1)
  set name($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasName() => $_has(0);
  @$pb.TagNumber(1)
  void clearName() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get seedTemplate => $_getSZ(1);
  @$pb.TagNumber(2)
  set seedTemplate($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasSeedTemplate() => $_has(1);
  @$pb.TagNumber(2)
  void clearSeedTemplate() => $_clearField(2);
}

class BrainCreated extends $pb.GeneratedMessage {
  factory BrainCreated({
    $core.String? brainId,
    $core.bool? success,
    $core.String? errorMessage,
  }) {
    final result = create();
    if (brainId != null) result.brainId = brainId;
    if (success != null) result.success = success;
    if (errorMessage != null) result.errorMessage = errorMessage;
    return result;
  }

  BrainCreated._();

  factory BrainCreated.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory BrainCreated.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'BrainCreated',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'brainId')
    ..aOB(2, _omitFieldNames ? '' : 'success')
    ..aOS(3, _omitFieldNames ? '' : 'errorMessage')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  BrainCreated clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  BrainCreated copyWith(void Function(BrainCreated) updates) =>
      super.copyWith((message) => updates(message as BrainCreated))
          as BrainCreated;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static BrainCreated create() => BrainCreated._();
  @$core.override
  BrainCreated createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static BrainCreated getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<BrainCreated>(create);
  static BrainCreated? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get brainId => $_getSZ(0);
  @$pb.TagNumber(1)
  set brainId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasBrainId() => $_has(0);
  @$pb.TagNumber(1)
  void clearBrainId() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.bool get success => $_getBF(1);
  @$pb.TagNumber(2)
  set success($core.bool value) => $_setBool(1, value);
  @$pb.TagNumber(2)
  $core.bool hasSuccess() => $_has(1);
  @$pb.TagNumber(2)
  void clearSuccess() => $_clearField(2);

  @$pb.TagNumber(3)
  $core.String get errorMessage => $_getSZ(2);
  @$pb.TagNumber(3)
  set errorMessage($core.String value) => $_setString(2, value);
  @$pb.TagNumber(3)
  $core.bool hasErrorMessage() => $_has(2);
  @$pb.TagNumber(3)
  void clearErrorMessage() => $_clearField(3);
}

class RenameBrainCommand extends $pb.GeneratedMessage {
  factory RenameBrainCommand({
    $core.String? brainId,
    $core.String? newName,
  }) {
    final result = create();
    if (brainId != null) result.brainId = brainId;
    if (newName != null) result.newName = newName;
    return result;
  }

  RenameBrainCommand._();

  factory RenameBrainCommand.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory RenameBrainCommand.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'RenameBrainCommand',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'brainId')
    ..aOS(2, _omitFieldNames ? '' : 'newName')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  RenameBrainCommand clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  RenameBrainCommand copyWith(void Function(RenameBrainCommand) updates) =>
      super.copyWith((message) => updates(message as RenameBrainCommand))
          as RenameBrainCommand;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static RenameBrainCommand create() => RenameBrainCommand._();
  @$core.override
  RenameBrainCommand createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static RenameBrainCommand getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<RenameBrainCommand>(create);
  static RenameBrainCommand? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get brainId => $_getSZ(0);
  @$pb.TagNumber(1)
  set brainId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasBrainId() => $_has(0);
  @$pb.TagNumber(1)
  void clearBrainId() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get newName => $_getSZ(1);
  @$pb.TagNumber(2)
  set newName($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasNewName() => $_has(1);
  @$pb.TagNumber(2)
  void clearNewName() => $_clearField(2);
}

class BrainRenamed extends $pb.GeneratedMessage {
  factory BrainRenamed({
    $core.String? brainId,
    $core.bool? success,
    $core.String? errorMessage,
  }) {
    final result = create();
    if (brainId != null) result.brainId = brainId;
    if (success != null) result.success = success;
    if (errorMessage != null) result.errorMessage = errorMessage;
    return result;
  }

  BrainRenamed._();

  factory BrainRenamed.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory BrainRenamed.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'BrainRenamed',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'brainId')
    ..aOB(2, _omitFieldNames ? '' : 'success')
    ..aOS(3, _omitFieldNames ? '' : 'errorMessage')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  BrainRenamed clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  BrainRenamed copyWith(void Function(BrainRenamed) updates) =>
      super.copyWith((message) => updates(message as BrainRenamed))
          as BrainRenamed;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static BrainRenamed create() => BrainRenamed._();
  @$core.override
  BrainRenamed createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static BrainRenamed getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<BrainRenamed>(create);
  static BrainRenamed? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get brainId => $_getSZ(0);
  @$pb.TagNumber(1)
  set brainId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasBrainId() => $_has(0);
  @$pb.TagNumber(1)
  void clearBrainId() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.bool get success => $_getBF(1);
  @$pb.TagNumber(2)
  set success($core.bool value) => $_setBool(1, value);
  @$pb.TagNumber(2)
  $core.bool hasSuccess() => $_has(1);
  @$pb.TagNumber(2)
  void clearSuccess() => $_clearField(2);

  @$pb.TagNumber(3)
  $core.String get errorMessage => $_getSZ(2);
  @$pb.TagNumber(3)
  set errorMessage($core.String value) => $_setString(2, value);
  @$pb.TagNumber(3)
  $core.bool hasErrorMessage() => $_has(2);
  @$pb.TagNumber(3)
  void clearErrorMessage() => $_clearField(3);
}

class DeleteBrainCommand extends $pb.GeneratedMessage {
  factory DeleteBrainCommand({
    $core.String? brainId,
  }) {
    final result = create();
    if (brainId != null) result.brainId = brainId;
    return result;
  }

  DeleteBrainCommand._();

  factory DeleteBrainCommand.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory DeleteBrainCommand.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'DeleteBrainCommand',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'brainId')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  DeleteBrainCommand clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  DeleteBrainCommand copyWith(void Function(DeleteBrainCommand) updates) =>
      super.copyWith((message) => updates(message as DeleteBrainCommand))
          as DeleteBrainCommand;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static DeleteBrainCommand create() => DeleteBrainCommand._();
  @$core.override
  DeleteBrainCommand createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static DeleteBrainCommand getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<DeleteBrainCommand>(create);
  static DeleteBrainCommand? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get brainId => $_getSZ(0);
  @$pb.TagNumber(1)
  set brainId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasBrainId() => $_has(0);
  @$pb.TagNumber(1)
  void clearBrainId() => $_clearField(1);
}

class BrainDeleted extends $pb.GeneratedMessage {
  factory BrainDeleted({
    $core.String? brainId,
    $core.bool? success,
    $core.String? errorMessage,
  }) {
    final result = create();
    if (brainId != null) result.brainId = brainId;
    if (success != null) result.success = success;
    if (errorMessage != null) result.errorMessage = errorMessage;
    return result;
  }

  BrainDeleted._();

  factory BrainDeleted.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory BrainDeleted.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'BrainDeleted',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'brainId')
    ..aOB(2, _omitFieldNames ? '' : 'success')
    ..aOS(3, _omitFieldNames ? '' : 'errorMessage')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  BrainDeleted clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  BrainDeleted copyWith(void Function(BrainDeleted) updates) =>
      super.copyWith((message) => updates(message as BrainDeleted))
          as BrainDeleted;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static BrainDeleted create() => BrainDeleted._();
  @$core.override
  BrainDeleted createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static BrainDeleted getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<BrainDeleted>(create);
  static BrainDeleted? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get brainId => $_getSZ(0);
  @$pb.TagNumber(1)
  set brainId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasBrainId() => $_has(0);
  @$pb.TagNumber(1)
  void clearBrainId() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.bool get success => $_getBF(1);
  @$pb.TagNumber(2)
  set success($core.bool value) => $_setBool(1, value);
  @$pb.TagNumber(2)
  $core.bool hasSuccess() => $_has(1);
  @$pb.TagNumber(2)
  void clearSuccess() => $_clearField(2);

  @$pb.TagNumber(3)
  $core.String get errorMessage => $_getSZ(2);
  @$pb.TagNumber(3)
  set errorMessage($core.String value) => $_setString(2, value);
  @$pb.TagNumber(3)
  $core.bool hasErrorMessage() => $_has(2);
  @$pb.TagNumber(3)
  void clearErrorMessage() => $_clearField(3);
}

class BrainSummary extends $pb.GeneratedMessage {
  factory BrainSummary({
    $core.String? id,
    $core.String? name,
    $core.String? version,
    $core.String? brandColor,
    $core.int? neuronCount,
    $1.Timestamp? lastActivityAt,
    $core.int? installedBundleCount,
    $core.Iterable<$core.String>? capabilityTags,
  }) {
    final result = create();
    if (id != null) result.id = id;
    if (name != null) result.name = name;
    if (version != null) result.version = version;
    if (brandColor != null) result.brandColor = brandColor;
    if (neuronCount != null) result.neuronCount = neuronCount;
    if (lastActivityAt != null) result.lastActivityAt = lastActivityAt;
    if (installedBundleCount != null)
      result.installedBundleCount = installedBundleCount;
    if (capabilityTags != null) result.capabilityTags.addAll(capabilityTags);
    return result;
  }

  BrainSummary._();

  factory BrainSummary.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory BrainSummary.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'BrainSummary',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'id')
    ..aOS(2, _omitFieldNames ? '' : 'name')
    ..aOS(3, _omitFieldNames ? '' : 'version')
    ..aOS(4, _omitFieldNames ? '' : 'brandColor')
    ..aI(5, _omitFieldNames ? '' : 'neuronCount')
    ..aOM<$1.Timestamp>(6, _omitFieldNames ? '' : 'lastActivityAt',
        subBuilder: $1.Timestamp.create)
    ..aI(7, _omitFieldNames ? '' : 'installedBundleCount')
    ..pPS(8, _omitFieldNames ? '' : 'capabilityTags')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  BrainSummary clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  BrainSummary copyWith(void Function(BrainSummary) updates) =>
      super.copyWith((message) => updates(message as BrainSummary))
          as BrainSummary;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static BrainSummary create() => BrainSummary._();
  @$core.override
  BrainSummary createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static BrainSummary getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<BrainSummary>(create);
  static BrainSummary? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get id => $_getSZ(0);
  @$pb.TagNumber(1)
  set id($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasId() => $_has(0);
  @$pb.TagNumber(1)
  void clearId() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get name => $_getSZ(1);
  @$pb.TagNumber(2)
  set name($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasName() => $_has(1);
  @$pb.TagNumber(2)
  void clearName() => $_clearField(2);

  @$pb.TagNumber(3)
  $core.String get version => $_getSZ(2);
  @$pb.TagNumber(3)
  set version($core.String value) => $_setString(2, value);
  @$pb.TagNumber(3)
  $core.bool hasVersion() => $_has(2);
  @$pb.TagNumber(3)
  void clearVersion() => $_clearField(3);

  @$pb.TagNumber(4)
  $core.String get brandColor => $_getSZ(3);
  @$pb.TagNumber(4)
  set brandColor($core.String value) => $_setString(3, value);
  @$pb.TagNumber(4)
  $core.bool hasBrandColor() => $_has(3);
  @$pb.TagNumber(4)
  void clearBrandColor() => $_clearField(4);

  @$pb.TagNumber(5)
  $core.int get neuronCount => $_getIZ(4);
  @$pb.TagNumber(5)
  set neuronCount($core.int value) => $_setSignedInt32(4, value);
  @$pb.TagNumber(5)
  $core.bool hasNeuronCount() => $_has(4);
  @$pb.TagNumber(5)
  void clearNeuronCount() => $_clearField(5);

  @$pb.TagNumber(6)
  $1.Timestamp get lastActivityAt => $_getN(5);
  @$pb.TagNumber(6)
  set lastActivityAt($1.Timestamp value) => $_setField(6, value);
  @$pb.TagNumber(6)
  $core.bool hasLastActivityAt() => $_has(5);
  @$pb.TagNumber(6)
  void clearLastActivityAt() => $_clearField(6);
  @$pb.TagNumber(6)
  $1.Timestamp ensureLastActivityAt() => $_ensure(5);

  @$pb.TagNumber(7)
  $core.int get installedBundleCount => $_getIZ(6);
  @$pb.TagNumber(7)
  set installedBundleCount($core.int value) => $_setSignedInt32(6, value);
  @$pb.TagNumber(7)
  $core.bool hasInstalledBundleCount() => $_has(6);
  @$pb.TagNumber(7)
  void clearInstalledBundleCount() => $_clearField(7);

  @$pb.TagNumber(8)
  $pb.PbList<$core.String> get capabilityTags => $_getList(7);
}

const $core.bool _omitFieldNames =
    $core.bool.fromEnvironment('protobuf.omit_field_names');
const $core.bool _omitMessageNames =
    $core.bool.fromEnvironment('protobuf.omit_message_names');
