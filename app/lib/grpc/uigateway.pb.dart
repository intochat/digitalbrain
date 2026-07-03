// This is a generated file - do not edit.
//
// Generated from uigateway.proto.

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

import 'uigateway.pbenum.dart';

export 'package:protobuf/protobuf.dart' show GeneratedMessageGenericExtensions;

export 'uigateway.pbenum.dart';

class UiInputSynapse extends $pb.GeneratedMessage {
  factory UiInputSynapse({
    $core.String? brainId,
    $core.String? elementId,
    $core.String? targetNeuronId,
    UiInputSynapse_InteractionType? interaction,
    $core.String? inputPayload,
    Point3D? coordinates,
    $1.Timestamp? clientTime,
  }) {
    final result = create();
    if (brainId != null) result.brainId = brainId;
    if (elementId != null) result.elementId = elementId;
    if (targetNeuronId != null) result.targetNeuronId = targetNeuronId;
    if (interaction != null) result.interaction = interaction;
    if (inputPayload != null) result.inputPayload = inputPayload;
    if (coordinates != null) result.coordinates = coordinates;
    if (clientTime != null) result.clientTime = clientTime;
    return result;
  }

  UiInputSynapse._();

  factory UiInputSynapse.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory UiInputSynapse.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'UiInputSynapse',
      package:
          const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain.ui'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'brainId')
    ..aOS(2, _omitFieldNames ? '' : 'elementId')
    ..aOS(3, _omitFieldNames ? '' : 'targetNeuronId')
    ..aE<UiInputSynapse_InteractionType>(
        4, _omitFieldNames ? '' : 'interaction',
        enumValues: UiInputSynapse_InteractionType.values)
    ..aOS(5, _omitFieldNames ? '' : 'inputPayload')
    ..aOM<Point3D>(6, _omitFieldNames ? '' : 'coordinates',
        subBuilder: Point3D.create)
    ..aOM<$1.Timestamp>(7, _omitFieldNames ? '' : 'clientTime',
        subBuilder: $1.Timestamp.create)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  UiInputSynapse clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  UiInputSynapse copyWith(void Function(UiInputSynapse) updates) =>
      super.copyWith((message) => updates(message as UiInputSynapse))
          as UiInputSynapse;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static UiInputSynapse create() => UiInputSynapse._();
  @$core.override
  UiInputSynapse createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static UiInputSynapse getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<UiInputSynapse>(create);
  static UiInputSynapse? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get brainId => $_getSZ(0);
  @$pb.TagNumber(1)
  set brainId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasBrainId() => $_has(0);
  @$pb.TagNumber(1)
  void clearBrainId() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get elementId => $_getSZ(1);
  @$pb.TagNumber(2)
  set elementId($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasElementId() => $_has(1);
  @$pb.TagNumber(2)
  void clearElementId() => $_clearField(2);

  @$pb.TagNumber(3)
  $core.String get targetNeuronId => $_getSZ(2);
  @$pb.TagNumber(3)
  set targetNeuronId($core.String value) => $_setString(2, value);
  @$pb.TagNumber(3)
  $core.bool hasTargetNeuronId() => $_has(2);
  @$pb.TagNumber(3)
  void clearTargetNeuronId() => $_clearField(3);

  @$pb.TagNumber(4)
  UiInputSynapse_InteractionType get interaction => $_getN(3);
  @$pb.TagNumber(4)
  set interaction(UiInputSynapse_InteractionType value) => $_setField(4, value);
  @$pb.TagNumber(4)
  $core.bool hasInteraction() => $_has(3);
  @$pb.TagNumber(4)
  void clearInteraction() => $_clearField(4);

  @$pb.TagNumber(5)
  $core.String get inputPayload => $_getSZ(4);
  @$pb.TagNumber(5)
  set inputPayload($core.String value) => $_setString(4, value);
  @$pb.TagNumber(5)
  $core.bool hasInputPayload() => $_has(4);
  @$pb.TagNumber(5)
  void clearInputPayload() => $_clearField(5);

  @$pb.TagNumber(6)
  Point3D get coordinates => $_getN(5);
  @$pb.TagNumber(6)
  set coordinates(Point3D value) => $_setField(6, value);
  @$pb.TagNumber(6)
  $core.bool hasCoordinates() => $_has(5);
  @$pb.TagNumber(6)
  void clearCoordinates() => $_clearField(6);
  @$pb.TagNumber(6)
  Point3D ensureCoordinates() => $_ensure(5);

  @$pb.TagNumber(7)
  $1.Timestamp get clientTime => $_getN(6);
  @$pb.TagNumber(7)
  set clientTime($1.Timestamp value) => $_setField(7, value);
  @$pb.TagNumber(7)
  $core.bool hasClientTime() => $_has(6);
  @$pb.TagNumber(7)
  void clearClientTime() => $_clearField(7);
  @$pb.TagNumber(7)
  $1.Timestamp ensureClientTime() => $_ensure(6);
}

class Point3D extends $pb.GeneratedMessage {
  factory Point3D({
    $core.double? x,
    $core.double? y,
    $core.double? z,
  }) {
    final result = create();
    if (x != null) result.x = x;
    if (y != null) result.y = y;
    if (z != null) result.z = z;
    return result;
  }

  Point3D._();

  factory Point3D.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory Point3D.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'Point3D',
      package:
          const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain.ui'),
      createEmptyInstance: create)
    ..aD(1, _omitFieldNames ? '' : 'x', fieldType: $pb.PbFieldType.OF)
    ..aD(2, _omitFieldNames ? '' : 'y', fieldType: $pb.PbFieldType.OF)
    ..aD(3, _omitFieldNames ? '' : 'z', fieldType: $pb.PbFieldType.OF)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  Point3D clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  Point3D copyWith(void Function(Point3D) updates) =>
      super.copyWith((message) => updates(message as Point3D)) as Point3D;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static Point3D create() => Point3D._();
  @$core.override
  Point3D createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static Point3D getDefault() =>
      _defaultInstance ??= $pb.GeneratedMessage.$_defaultFor<Point3D>(create);
  static Point3D? _defaultInstance;

  @$pb.TagNumber(1)
  $core.double get x => $_getN(0);
  @$pb.TagNumber(1)
  set x($core.double value) => $_setFloat(0, value);
  @$pb.TagNumber(1)
  $core.bool hasX() => $_has(0);
  @$pb.TagNumber(1)
  void clearX() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.double get y => $_getN(1);
  @$pb.TagNumber(2)
  set y($core.double value) => $_setFloat(1, value);
  @$pb.TagNumber(2)
  $core.bool hasY() => $_has(1);
  @$pb.TagNumber(2)
  void clearY() => $_clearField(2);

  @$pb.TagNumber(3)
  $core.double get z => $_getN(2);
  @$pb.TagNumber(3)
  set z($core.double value) => $_setFloat(2, value);
  @$pb.TagNumber(3)
  $core.bool hasZ() => $_has(2);
  @$pb.TagNumber(3)
  void clearZ() => $_clearField(3);
}

enum UiStateSignal_Payload { rfwCard, viewport, sound, notSet }

class UiStateSignal extends $pb.GeneratedMessage {
  factory UiStateSignal({
    RfwCardProto? rfwCard,
    UiViewportSignal? viewport,
    UiSoundSignal? sound,
  }) {
    final result = create();
    if (rfwCard != null) result.rfwCard = rfwCard;
    if (viewport != null) result.viewport = viewport;
    if (sound != null) result.sound = sound;
    return result;
  }

  UiStateSignal._();

  factory UiStateSignal.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory UiStateSignal.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static const $core.Map<$core.int, UiStateSignal_Payload>
      _UiStateSignal_PayloadByTag = {
    1: UiStateSignal_Payload.rfwCard,
    2: UiStateSignal_Payload.viewport,
    3: UiStateSignal_Payload.sound,
    0: UiStateSignal_Payload.notSet
  };
  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'UiStateSignal',
      package:
          const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain.ui'),
      createEmptyInstance: create)
    ..oo(0, [1, 2, 3])
    ..aOM<RfwCardProto>(1, _omitFieldNames ? '' : 'rfwCard',
        subBuilder: RfwCardProto.create)
    ..aOM<UiViewportSignal>(2, _omitFieldNames ? '' : 'viewport',
        subBuilder: UiViewportSignal.create)
    ..aOM<UiSoundSignal>(3, _omitFieldNames ? '' : 'sound',
        subBuilder: UiSoundSignal.create)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  UiStateSignal clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  UiStateSignal copyWith(void Function(UiStateSignal) updates) =>
      super.copyWith((message) => updates(message as UiStateSignal))
          as UiStateSignal;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static UiStateSignal create() => UiStateSignal._();
  @$core.override
  UiStateSignal createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static UiStateSignal getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<UiStateSignal>(create);
  static UiStateSignal? _defaultInstance;

  @$pb.TagNumber(1)
  @$pb.TagNumber(2)
  @$pb.TagNumber(3)
  UiStateSignal_Payload whichPayload() =>
      _UiStateSignal_PayloadByTag[$_whichOneof(0)]!;
  @$pb.TagNumber(1)
  @$pb.TagNumber(2)
  @$pb.TagNumber(3)
  void clearPayload() => $_clearField($_whichOneof(0));

  @$pb.TagNumber(1)
  RfwCardProto get rfwCard => $_getN(0);
  @$pb.TagNumber(1)
  set rfwCard(RfwCardProto value) => $_setField(1, value);
  @$pb.TagNumber(1)
  $core.bool hasRfwCard() => $_has(0);
  @$pb.TagNumber(1)
  void clearRfwCard() => $_clearField(1);
  @$pb.TagNumber(1)
  RfwCardProto ensureRfwCard() => $_ensure(0);

  @$pb.TagNumber(2)
  UiViewportSignal get viewport => $_getN(1);
  @$pb.TagNumber(2)
  set viewport(UiViewportSignal value) => $_setField(2, value);
  @$pb.TagNumber(2)
  $core.bool hasViewport() => $_has(1);
  @$pb.TagNumber(2)
  void clearViewport() => $_clearField(2);
  @$pb.TagNumber(2)
  UiViewportSignal ensureViewport() => $_ensure(1);

  @$pb.TagNumber(3)
  UiSoundSignal get sound => $_getN(2);
  @$pb.TagNumber(3)
  set sound(UiSoundSignal value) => $_setField(3, value);
  @$pb.TagNumber(3)
  $core.bool hasSound() => $_has(2);
  @$pb.TagNumber(3)
  void clearSound() => $_clearField(3);
  @$pb.TagNumber(3)
  UiSoundSignal ensureSound() => $_ensure(2);
}

class RfwCardProto extends $pb.GeneratedMessage {
  factory RfwCardProto({
    $core.String? brainId,
    $core.String? neuronId,
    $core.String? surfaceId,
    $core.String? libraryUri,
    $core.String? rootWidget,
    $core.List<$core.int>? dataJson,
    RfwCardProto_RfwLayoutProto? layout,
    RfwCardProto_RfwLockProto? lockState,
    $core.String? requiredActionId,
    $1.Timestamp? at,
  }) {
    final result = create();
    if (brainId != null) result.brainId = brainId;
    if (neuronId != null) result.neuronId = neuronId;
    if (surfaceId != null) result.surfaceId = surfaceId;
    if (libraryUri != null) result.libraryUri = libraryUri;
    if (rootWidget != null) result.rootWidget = rootWidget;
    if (dataJson != null) result.dataJson = dataJson;
    if (layout != null) result.layout = layout;
    if (lockState != null) result.lockState = lockState;
    if (requiredActionId != null) result.requiredActionId = requiredActionId;
    if (at != null) result.at = at;
    return result;
  }

  RfwCardProto._();

  factory RfwCardProto.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory RfwCardProto.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'RfwCardProto',
      package:
          const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain.ui'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'brainId')
    ..aOS(2, _omitFieldNames ? '' : 'neuronId')
    ..aOS(3, _omitFieldNames ? '' : 'surfaceId')
    ..aOS(4, _omitFieldNames ? '' : 'libraryUri')
    ..aOS(5, _omitFieldNames ? '' : 'rootWidget')
    ..a<$core.List<$core.int>>(
        6, _omitFieldNames ? '' : 'dataJson', $pb.PbFieldType.OY)
    ..aE<RfwCardProto_RfwLayoutProto>(7, _omitFieldNames ? '' : 'layout',
        enumValues: RfwCardProto_RfwLayoutProto.values)
    ..aE<RfwCardProto_RfwLockProto>(8, _omitFieldNames ? '' : 'lockState',
        enumValues: RfwCardProto_RfwLockProto.values)
    ..aOS(9, _omitFieldNames ? '' : 'requiredActionId')
    ..aOM<$1.Timestamp>(10, _omitFieldNames ? '' : 'at',
        subBuilder: $1.Timestamp.create)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  RfwCardProto clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  RfwCardProto copyWith(void Function(RfwCardProto) updates) =>
      super.copyWith((message) => updates(message as RfwCardProto))
          as RfwCardProto;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static RfwCardProto create() => RfwCardProto._();
  @$core.override
  RfwCardProto createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static RfwCardProto getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<RfwCardProto>(create);
  static RfwCardProto? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get brainId => $_getSZ(0);
  @$pb.TagNumber(1)
  set brainId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasBrainId() => $_has(0);
  @$pb.TagNumber(1)
  void clearBrainId() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get neuronId => $_getSZ(1);
  @$pb.TagNumber(2)
  set neuronId($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasNeuronId() => $_has(1);
  @$pb.TagNumber(2)
  void clearNeuronId() => $_clearField(2);

  @$pb.TagNumber(3)
  $core.String get surfaceId => $_getSZ(2);
  @$pb.TagNumber(3)
  set surfaceId($core.String value) => $_setString(2, value);
  @$pb.TagNumber(3)
  $core.bool hasSurfaceId() => $_has(2);
  @$pb.TagNumber(3)
  void clearSurfaceId() => $_clearField(3);

  @$pb.TagNumber(4)
  $core.String get libraryUri => $_getSZ(3);
  @$pb.TagNumber(4)
  set libraryUri($core.String value) => $_setString(3, value);
  @$pb.TagNumber(4)
  $core.bool hasLibraryUri() => $_has(3);
  @$pb.TagNumber(4)
  void clearLibraryUri() => $_clearField(4);

  @$pb.TagNumber(5)
  $core.String get rootWidget => $_getSZ(4);
  @$pb.TagNumber(5)
  set rootWidget($core.String value) => $_setString(4, value);
  @$pb.TagNumber(5)
  $core.bool hasRootWidget() => $_has(4);
  @$pb.TagNumber(5)
  void clearRootWidget() => $_clearField(5);

  @$pb.TagNumber(6)
  $core.List<$core.int> get dataJson => $_getN(5);
  @$pb.TagNumber(6)
  set dataJson($core.List<$core.int> value) => $_setBytes(5, value);
  @$pb.TagNumber(6)
  $core.bool hasDataJson() => $_has(5);
  @$pb.TagNumber(6)
  void clearDataJson() => $_clearField(6);

  @$pb.TagNumber(7)
  RfwCardProto_RfwLayoutProto get layout => $_getN(6);
  @$pb.TagNumber(7)
  set layout(RfwCardProto_RfwLayoutProto value) => $_setField(7, value);
  @$pb.TagNumber(7)
  $core.bool hasLayout() => $_has(6);
  @$pb.TagNumber(7)
  void clearLayout() => $_clearField(7);

  @$pb.TagNumber(8)
  RfwCardProto_RfwLockProto get lockState => $_getN(7);
  @$pb.TagNumber(8)
  set lockState(RfwCardProto_RfwLockProto value) => $_setField(8, value);
  @$pb.TagNumber(8)
  $core.bool hasLockState() => $_has(7);
  @$pb.TagNumber(8)
  void clearLockState() => $_clearField(8);

  @$pb.TagNumber(9)
  $core.String get requiredActionId => $_getSZ(8);
  @$pb.TagNumber(9)
  set requiredActionId($core.String value) => $_setString(8, value);
  @$pb.TagNumber(9)
  $core.bool hasRequiredActionId() => $_has(8);
  @$pb.TagNumber(9)
  void clearRequiredActionId() => $_clearField(9);

  @$pb.TagNumber(10)
  $1.Timestamp get at => $_getN(9);
  @$pb.TagNumber(10)
  set at($1.Timestamp value) => $_setField(10, value);
  @$pb.TagNumber(10)
  $core.bool hasAt() => $_has(9);
  @$pb.TagNumber(10)
  void clearAt() => $_clearField(10);
  @$pb.TagNumber(10)
  $1.Timestamp ensureAt() => $_ensure(9);
}

class UiViewportSignal_SpringConfig extends $pb.GeneratedMessage {
  factory UiViewportSignal_SpringConfig({
    $core.double? dampingRatio,
    $core.double? naturalFreq,
  }) {
    final result = create();
    if (dampingRatio != null) result.dampingRatio = dampingRatio;
    if (naturalFreq != null) result.naturalFreq = naturalFreq;
    return result;
  }

  UiViewportSignal_SpringConfig._();

  factory UiViewportSignal_SpringConfig.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory UiViewportSignal_SpringConfig.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'UiViewportSignal.SpringConfig',
      package:
          const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain.ui'),
      createEmptyInstance: create)
    ..aD(1, _omitFieldNames ? '' : 'dampingRatio',
        fieldType: $pb.PbFieldType.OF)
    ..aD(2, _omitFieldNames ? '' : 'naturalFreq', fieldType: $pb.PbFieldType.OF)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  UiViewportSignal_SpringConfig clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  UiViewportSignal_SpringConfig copyWith(
          void Function(UiViewportSignal_SpringConfig) updates) =>
      super.copyWith(
              (message) => updates(message as UiViewportSignal_SpringConfig))
          as UiViewportSignal_SpringConfig;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static UiViewportSignal_SpringConfig create() =>
      UiViewportSignal_SpringConfig._();
  @$core.override
  UiViewportSignal_SpringConfig createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static UiViewportSignal_SpringConfig getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<UiViewportSignal_SpringConfig>(create);
  static UiViewportSignal_SpringConfig? _defaultInstance;

  @$pb.TagNumber(1)
  $core.double get dampingRatio => $_getN(0);
  @$pb.TagNumber(1)
  set dampingRatio($core.double value) => $_setFloat(0, value);
  @$pb.TagNumber(1)
  $core.bool hasDampingRatio() => $_has(0);
  @$pb.TagNumber(1)
  void clearDampingRatio() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.double get naturalFreq => $_getN(1);
  @$pb.TagNumber(2)
  set naturalFreq($core.double value) => $_setFloat(1, value);
  @$pb.TagNumber(2)
  $core.bool hasNaturalFreq() => $_has(1);
  @$pb.TagNumber(2)
  void clearNaturalFreq() => $_clearField(2);
}

class UiViewportSignal extends $pb.GeneratedMessage {
  factory UiViewportSignal({
    Point3D? cameraTarget,
    Point3D? cameraRotation,
    $core.double? zoomDepth,
    UiViewportSignal_SpringConfig? cameraSpring,
    $core.double? ambientGlow,
    $core.double? depthOfField,
  }) {
    final result = create();
    if (cameraTarget != null) result.cameraTarget = cameraTarget;
    if (cameraRotation != null) result.cameraRotation = cameraRotation;
    if (zoomDepth != null) result.zoomDepth = zoomDepth;
    if (cameraSpring != null) result.cameraSpring = cameraSpring;
    if (ambientGlow != null) result.ambientGlow = ambientGlow;
    if (depthOfField != null) result.depthOfField = depthOfField;
    return result;
  }

  UiViewportSignal._();

  factory UiViewportSignal.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory UiViewportSignal.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'UiViewportSignal',
      package:
          const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain.ui'),
      createEmptyInstance: create)
    ..aOM<Point3D>(1, _omitFieldNames ? '' : 'cameraTarget',
        subBuilder: Point3D.create)
    ..aOM<Point3D>(2, _omitFieldNames ? '' : 'cameraRotation',
        subBuilder: Point3D.create)
    ..aD(3, _omitFieldNames ? '' : 'zoomDepth', fieldType: $pb.PbFieldType.OF)
    ..aOM<UiViewportSignal_SpringConfig>(
        4, _omitFieldNames ? '' : 'cameraSpring',
        subBuilder: UiViewportSignal_SpringConfig.create)
    ..aD(5, _omitFieldNames ? '' : 'ambientGlow', fieldType: $pb.PbFieldType.OF)
    ..aD(6, _omitFieldNames ? '' : 'depthOfField',
        fieldType: $pb.PbFieldType.OF)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  UiViewportSignal clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  UiViewportSignal copyWith(void Function(UiViewportSignal) updates) =>
      super.copyWith((message) => updates(message as UiViewportSignal))
          as UiViewportSignal;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static UiViewportSignal create() => UiViewportSignal._();
  @$core.override
  UiViewportSignal createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static UiViewportSignal getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<UiViewportSignal>(create);
  static UiViewportSignal? _defaultInstance;

  @$pb.TagNumber(1)
  Point3D get cameraTarget => $_getN(0);
  @$pb.TagNumber(1)
  set cameraTarget(Point3D value) => $_setField(1, value);
  @$pb.TagNumber(1)
  $core.bool hasCameraTarget() => $_has(0);
  @$pb.TagNumber(1)
  void clearCameraTarget() => $_clearField(1);
  @$pb.TagNumber(1)
  Point3D ensureCameraTarget() => $_ensure(0);

  @$pb.TagNumber(2)
  Point3D get cameraRotation => $_getN(1);
  @$pb.TagNumber(2)
  set cameraRotation(Point3D value) => $_setField(2, value);
  @$pb.TagNumber(2)
  $core.bool hasCameraRotation() => $_has(1);
  @$pb.TagNumber(2)
  void clearCameraRotation() => $_clearField(2);
  @$pb.TagNumber(2)
  Point3D ensureCameraRotation() => $_ensure(1);

  @$pb.TagNumber(3)
  $core.double get zoomDepth => $_getN(2);
  @$pb.TagNumber(3)
  set zoomDepth($core.double value) => $_setFloat(2, value);
  @$pb.TagNumber(3)
  $core.bool hasZoomDepth() => $_has(2);
  @$pb.TagNumber(3)
  void clearZoomDepth() => $_clearField(3);

  @$pb.TagNumber(4)
  UiViewportSignal_SpringConfig get cameraSpring => $_getN(3);
  @$pb.TagNumber(4)
  set cameraSpring(UiViewportSignal_SpringConfig value) => $_setField(4, value);
  @$pb.TagNumber(4)
  $core.bool hasCameraSpring() => $_has(3);
  @$pb.TagNumber(4)
  void clearCameraSpring() => $_clearField(4);
  @$pb.TagNumber(4)
  UiViewportSignal_SpringConfig ensureCameraSpring() => $_ensure(3);

  @$pb.TagNumber(5)
  $core.double get ambientGlow => $_getN(4);
  @$pb.TagNumber(5)
  set ambientGlow($core.double value) => $_setFloat(4, value);
  @$pb.TagNumber(5)
  $core.bool hasAmbientGlow() => $_has(4);
  @$pb.TagNumber(5)
  void clearAmbientGlow() => $_clearField(5);

  @$pb.TagNumber(6)
  $core.double get depthOfField => $_getN(5);
  @$pb.TagNumber(6)
  set depthOfField($core.double value) => $_setFloat(5, value);
  @$pb.TagNumber(6)
  $core.bool hasDepthOfField() => $_has(5);
  @$pb.TagNumber(6)
  void clearDepthOfField() => $_clearField(6);
}

class UiSoundSignal extends $pb.GeneratedMessage {
  factory UiSoundSignal({
    $core.String? soundEffectId,
    $core.double? volume,
  }) {
    final result = create();
    if (soundEffectId != null) result.soundEffectId = soundEffectId;
    if (volume != null) result.volume = volume;
    return result;
  }

  UiSoundSignal._();

  factory UiSoundSignal.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory UiSoundSignal.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'UiSoundSignal',
      package:
          const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain.ui'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'soundEffectId')
    ..aD(2, _omitFieldNames ? '' : 'volume', fieldType: $pb.PbFieldType.OF)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  UiSoundSignal clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  UiSoundSignal copyWith(void Function(UiSoundSignal) updates) =>
      super.copyWith((message) => updates(message as UiSoundSignal))
          as UiSoundSignal;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static UiSoundSignal create() => UiSoundSignal._();
  @$core.override
  UiSoundSignal createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static UiSoundSignal getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<UiSoundSignal>(create);
  static UiSoundSignal? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get soundEffectId => $_getSZ(0);
  @$pb.TagNumber(1)
  set soundEffectId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasSoundEffectId() => $_has(0);
  @$pb.TagNumber(1)
  void clearSoundEffectId() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.double get volume => $_getN(1);
  @$pb.TagNumber(2)
  set volume($core.double value) => $_setFloat(1, value);
  @$pb.TagNumber(2)
  $core.bool hasVolume() => $_has(1);
  @$pb.TagNumber(2)
  void clearVolume() => $_clearField(2);
}

const $core.bool _omitFieldNames =
    $core.bool.fromEnvironment('protobuf.omit_field_names');
const $core.bool _omitMessageNames =
    $core.bool.fromEnvironment('protobuf.omit_message_names');
