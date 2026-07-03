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

class UiInputSynapse_InteractionType extends $pb.ProtobufEnum {
  static const UiInputSynapse_InteractionType CLICK =
      UiInputSynapse_InteractionType._(0, _omitEnumNames ? '' : 'CLICK');
  static const UiInputSynapse_InteractionType HOVER_ENTER =
      UiInputSynapse_InteractionType._(1, _omitEnumNames ? '' : 'HOVER_ENTER');
  static const UiInputSynapse_InteractionType HOVER_EXIT =
      UiInputSynapse_InteractionType._(2, _omitEnumNames ? '' : 'HOVER_EXIT');
  static const UiInputSynapse_InteractionType DOUBLE_CLICK =
      UiInputSynapse_InteractionType._(3, _omitEnumNames ? '' : 'DOUBLE_CLICK');
  static const UiInputSynapse_InteractionType TEXT_INPUT =
      UiInputSynapse_InteractionType._(4, _omitEnumNames ? '' : 'TEXT_INPUT');
  static const UiInputSynapse_InteractionType KEY_PRESS =
      UiInputSynapse_InteractionType._(5, _omitEnumNames ? '' : 'KEY_PRESS');
  static const UiInputSynapse_InteractionType DRAG_NODE =
      UiInputSynapse_InteractionType._(6, _omitEnumNames ? '' : 'DRAG_NODE');

  static const $core.List<UiInputSynapse_InteractionType> values =
      <UiInputSynapse_InteractionType>[
    CLICK,
    HOVER_ENTER,
    HOVER_EXIT,
    DOUBLE_CLICK,
    TEXT_INPUT,
    KEY_PRESS,
    DRAG_NODE,
  ];

  static final $core.List<UiInputSynapse_InteractionType?> _byValue =
      $pb.ProtobufEnum.$_initByValueList(values, 6);
  static UiInputSynapse_InteractionType? valueOf($core.int value) =>
      value < 0 || value >= _byValue.length ? null : _byValue[value];

  const UiInputSynapse_InteractionType._(super.value, super.name);
}

class RfwCardProto_RfwLayoutProto extends $pb.ProtobufEnum {
  static const RfwCardProto_RfwLayoutProto CARD =
      RfwCardProto_RfwLayoutProto._(0, _omitEnumNames ? '' : 'CARD');
  static const RfwCardProto_RfwLayoutProto PANEL =
      RfwCardProto_RfwLayoutProto._(1, _omitEnumNames ? '' : 'PANEL');
  static const RfwCardProto_RfwLayoutProto MODAL =
      RfwCardProto_RfwLayoutProto._(2, _omitEnumNames ? '' : 'MODAL');
  static const RfwCardProto_RfwLayoutProto INLINE =
      RfwCardProto_RfwLayoutProto._(3, _omitEnumNames ? '' : 'INLINE');
  static const RfwCardProto_RfwLayoutProto CANVAS =
      RfwCardProto_RfwLayoutProto._(4, _omitEnumNames ? '' : 'CANVAS');

  static const $core.List<RfwCardProto_RfwLayoutProto> values =
      <RfwCardProto_RfwLayoutProto>[
    CARD,
    PANEL,
    MODAL,
    INLINE,
    CANVAS,
  ];

  static final $core.List<RfwCardProto_RfwLayoutProto?> _byValue =
      $pb.ProtobufEnum.$_initByValueList(values, 4);
  static RfwCardProto_RfwLayoutProto? valueOf($core.int value) =>
      value < 0 || value >= _byValue.length ? null : _byValue[value];

  const RfwCardProto_RfwLayoutProto._(super.value, super.name);
}

class RfwCardProto_RfwLockProto extends $pb.ProtobufEnum {
  static const RfwCardProto_RfwLockProto IDLE =
      RfwCardProto_RfwLockProto._(0, _omitEnumNames ? '' : 'IDLE');
  static const RfwCardProto_RfwLockProto BUSY =
      RfwCardProto_RfwLockProto._(1, _omitEnumNames ? '' : 'BUSY');
  static const RfwCardProto_RfwLockProto MODAL_LOCK =
      RfwCardProto_RfwLockProto._(2, _omitEnumNames ? '' : 'MODAL_LOCK');

  static const $core.List<RfwCardProto_RfwLockProto> values =
      <RfwCardProto_RfwLockProto>[
    IDLE,
    BUSY,
    MODAL_LOCK,
  ];

  static final $core.List<RfwCardProto_RfwLockProto?> _byValue =
      $pb.ProtobufEnum.$_initByValueList(values, 2);
  static RfwCardProto_RfwLockProto? valueOf($core.int value) =>
      value < 0 || value >= _byValue.length ? null : _byValue[value];

  const RfwCardProto_RfwLockProto._(super.value, super.name);
}

const $core.bool _omitEnumNames =
    $core.bool.fromEnvironment('protobuf.omit_enum_names');
