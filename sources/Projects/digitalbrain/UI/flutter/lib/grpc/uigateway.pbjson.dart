// This is a generated file - do not edit.
//
// Generated from uigateway.proto.

// @dart = 3.3

// ignore_for_file: annotate_overrides, camel_case_types, comment_references
// ignore_for_file: constant_identifier_names
// ignore_for_file: curly_braces_in_flow_control_structures
// ignore_for_file: deprecated_member_use_from_same_package, library_prefixes
// ignore_for_file: non_constant_identifier_names, prefer_relative_imports
// ignore_for_file: unused_import

import 'dart:convert' as $convert;
import 'dart:core' as $core;
import 'dart:typed_data' as $typed_data;

@$core.Deprecated('Use uiInputSynapseDescriptor instead')
const UiInputSynapse$json = {
  '1': 'UiInputSynapse',
  '2': [
    {'1': 'brain_id', '3': 1, '4': 1, '5': 9, '10': 'brainId'},
    {'1': 'element_id', '3': 2, '4': 1, '5': 9, '10': 'elementId'},
    {'1': 'target_neuron_id', '3': 3, '4': 1, '5': 9, '10': 'targetNeuronId'},
    {
      '1': 'interaction',
      '3': 4,
      '4': 1,
      '5': 14,
      '6': '.digitalbrain.ui.UiInputSynapse.InteractionType',
      '10': 'interaction'
    },
    {'1': 'input_payload', '3': 5, '4': 1, '5': 9, '10': 'inputPayload'},
    {
      '1': 'coordinates',
      '3': 6,
      '4': 1,
      '5': 11,
      '6': '.digitalbrain.ui.Point3D',
      '10': 'coordinates'
    },
    {
      '1': 'client_time',
      '3': 7,
      '4': 1,
      '5': 11,
      '6': '.google.protobuf.Timestamp',
      '10': 'clientTime'
    },
  ],
  '4': [UiInputSynapse_InteractionType$json],
};

@$core.Deprecated('Use uiInputSynapseDescriptor instead')
const UiInputSynapse_InteractionType$json = {
  '1': 'InteractionType',
  '2': [
    {'1': 'CLICK', '2': 0},
    {'1': 'HOVER_ENTER', '2': 1},
    {'1': 'HOVER_EXIT', '2': 2},
    {'1': 'DOUBLE_CLICK', '2': 3},
    {'1': 'TEXT_INPUT', '2': 4},
    {'1': 'KEY_PRESS', '2': 5},
    {'1': 'DRAG_NODE', '2': 6},
  ],
};

/// Descriptor for `UiInputSynapse`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List uiInputSynapseDescriptor = $convert.base64Decode(
    'Cg5VaUlucHV0U3luYXBzZRIZCghicmFpbl9pZBgBIAEoCVIHYnJhaW5JZBIdCgplbGVtZW50X2'
    'lkGAIgASgJUgllbGVtZW50SWQSKAoQdGFyZ2V0X25ldXJvbl9pZBgDIAEoCVIOdGFyZ2V0TmV1'
    'cm9uSWQSUQoLaW50ZXJhY3Rpb24YBCABKA4yLy5kaWdpdGFsYnJhaW4udWkuVWlJbnB1dFN5bm'
    'Fwc2UuSW50ZXJhY3Rpb25UeXBlUgtpbnRlcmFjdGlvbhIjCg1pbnB1dF9wYXlsb2FkGAUgASgJ'
    'UgxpbnB1dFBheWxvYWQSOgoLY29vcmRpbmF0ZXMYBiABKAsyGC5kaWdpdGFsYnJhaW4udWkuUG'
    '9pbnQzRFILY29vcmRpbmF0ZXMSOwoLY2xpZW50X3RpbWUYByABKAsyGi5nb29nbGUucHJvdG9i'
    'dWYuVGltZXN0YW1wUgpjbGllbnRUaW1lIn0KD0ludGVyYWN0aW9uVHlwZRIJCgVDTElDSxAAEg'
    '8KC0hPVkVSX0VOVEVSEAESDgoKSE9WRVJfRVhJVBACEhAKDERPVUJMRV9DTElDSxADEg4KClRF'
    'WFRfSU5QVVQQBBINCglLRVlfUFJFU1MQBRINCglEUkFHX05PREUQBg==');

@$core.Deprecated('Use point3DDescriptor instead')
const Point3D$json = {
  '1': 'Point3D',
  '2': [
    {'1': 'x', '3': 1, '4': 1, '5': 2, '10': 'x'},
    {'1': 'y', '3': 2, '4': 1, '5': 2, '10': 'y'},
    {'1': 'z', '3': 3, '4': 1, '5': 2, '10': 'z'},
  ],
};

/// Descriptor for `Point3D`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List point3DDescriptor = $convert.base64Decode(
    'CgdQb2ludDNEEgwKAXgYASABKAJSAXgSDAoBeRgCIAEoAlIBeRIMCgF6GAMgASgCUgF6');

@$core.Deprecated('Use uiStateSignalDescriptor instead')
const UiStateSignal$json = {
  '1': 'UiStateSignal',
  '2': [
    {
      '1': 'rfw_card',
      '3': 1,
      '4': 1,
      '5': 11,
      '6': '.digitalbrain.ui.RfwCardProto',
      '9': 0,
      '10': 'rfwCard'
    },
    {
      '1': 'viewport',
      '3': 2,
      '4': 1,
      '5': 11,
      '6': '.digitalbrain.ui.UiViewportSignal',
      '9': 0,
      '10': 'viewport'
    },
    {
      '1': 'sound',
      '3': 3,
      '4': 1,
      '5': 11,
      '6': '.digitalbrain.ui.UiSoundSignal',
      '9': 0,
      '10': 'sound'
    },
  ],
  '8': [
    {'1': 'payload'},
  ],
};

/// Descriptor for `UiStateSignal`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List uiStateSignalDescriptor = $convert.base64Decode(
    'Cg1VaVN0YXRlU2lnbmFsEjoKCHJmd19jYXJkGAEgASgLMh0uZGlnaXRhbGJyYWluLnVpLlJmd0'
    'NhcmRQcm90b0gAUgdyZndDYXJkEj8KCHZpZXdwb3J0GAIgASgLMiEuZGlnaXRhbGJyYWluLnVp'
    'LlVpVmlld3BvcnRTaWduYWxIAFIIdmlld3BvcnQSNgoFc291bmQYAyABKAsyHi5kaWdpdGFsYn'
    'JhaW4udWkuVWlTb3VuZFNpZ25hbEgAUgVzb3VuZEIJCgdwYXlsb2Fk');

@$core.Deprecated('Use rfwCardProtoDescriptor instead')
const RfwCardProto$json = {
  '1': 'RfwCardProto',
  '2': [
    {'1': 'brain_id', '3': 1, '4': 1, '5': 9, '10': 'brainId'},
    {'1': 'neuron_id', '3': 2, '4': 1, '5': 9, '10': 'neuronId'},
    {'1': 'surface_id', '3': 3, '4': 1, '5': 9, '10': 'surfaceId'},
    {'1': 'library_uri', '3': 4, '4': 1, '5': 9, '10': 'libraryUri'},
    {'1': 'root_widget', '3': 5, '4': 1, '5': 9, '10': 'rootWidget'},
    {'1': 'data_json', '3': 6, '4': 1, '5': 12, '10': 'dataJson'},
    {
      '1': 'layout',
      '3': 7,
      '4': 1,
      '5': 14,
      '6': '.digitalbrain.ui.RfwCardProto.RfwLayoutProto',
      '10': 'layout'
    },
    {
      '1': 'lock_state',
      '3': 8,
      '4': 1,
      '5': 14,
      '6': '.digitalbrain.ui.RfwCardProto.RfwLockProto',
      '10': 'lockState'
    },
    {
      '1': 'required_action_id',
      '3': 9,
      '4': 1,
      '5': 9,
      '10': 'requiredActionId'
    },
    {
      '1': 'at',
      '3': 10,
      '4': 1,
      '5': 11,
      '6': '.google.protobuf.Timestamp',
      '10': 'at'
    },
  ],
  '4': [RfwCardProto_RfwLayoutProto$json, RfwCardProto_RfwLockProto$json],
};

@$core.Deprecated('Use rfwCardProtoDescriptor instead')
const RfwCardProto_RfwLayoutProto$json = {
  '1': 'RfwLayoutProto',
  '2': [
    {'1': 'CARD', '2': 0},
    {'1': 'PANEL', '2': 1},
    {'1': 'MODAL', '2': 2},
    {'1': 'INLINE', '2': 3},
    {'1': 'CANVAS', '2': 4},
  ],
};

@$core.Deprecated('Use rfwCardProtoDescriptor instead')
const RfwCardProto_RfwLockProto$json = {
  '1': 'RfwLockProto',
  '2': [
    {'1': 'IDLE', '2': 0},
    {'1': 'BUSY', '2': 1},
    {'1': 'MODAL_LOCK', '2': 2},
  ],
};

/// Descriptor for `RfwCardProto`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List rfwCardProtoDescriptor = $convert.base64Decode(
    'CgxSZndDYXJkUHJvdG8SGQoIYnJhaW5faWQYASABKAlSB2JyYWluSWQSGwoJbmV1cm9uX2lkGA'
    'IgASgJUghuZXVyb25JZBIdCgpzdXJmYWNlX2lkGAMgASgJUglzdXJmYWNlSWQSHwoLbGlicmFy'
    'eV91cmkYBCABKAlSCmxpYnJhcnlVcmkSHwoLcm9vdF93aWRnZXQYBSABKAlSCnJvb3RXaWRnZX'
    'QSGwoJZGF0YV9qc29uGAYgASgMUghkYXRhSnNvbhJECgZsYXlvdXQYByABKA4yLC5kaWdpdGFs'
    'YnJhaW4udWkuUmZ3Q2FyZFByb3RvLlJmd0xheW91dFByb3RvUgZsYXlvdXQSSQoKbG9ja19zdG'
    'F0ZRgIIAEoDjIqLmRpZ2l0YWxicmFpbi51aS5SZndDYXJkUHJvdG8uUmZ3TG9ja1Byb3RvUgls'
    'b2NrU3RhdGUSLAoScmVxdWlyZWRfYWN0aW9uX2lkGAkgASgJUhByZXF1aXJlZEFjdGlvbklkEi'
    'oKAmF0GAogASgLMhouZ29vZ2xlLnByb3RvYnVmLlRpbWVzdGFtcFICYXQiSAoOUmZ3TGF5b3V0'
    'UHJvdG8SCAoEQ0FSRBAAEgkKBVBBTkVMEAESCQoFTU9EQUwQAhIKCgZJTkxJTkUQAxIKCgZDQU'
    '5WQVMQBCIyCgxSZndMb2NrUHJvdG8SCAoESURMRRAAEggKBEJVU1kQARIOCgpNT0RBTF9MT0NL'
    'EAI=');

@$core.Deprecated('Use uiViewportSignalDescriptor instead')
const UiViewportSignal$json = {
  '1': 'UiViewportSignal',
  '2': [
    {
      '1': 'camera_target',
      '3': 1,
      '4': 1,
      '5': 11,
      '6': '.digitalbrain.ui.Point3D',
      '10': 'cameraTarget'
    },
    {
      '1': 'camera_rotation',
      '3': 2,
      '4': 1,
      '5': 11,
      '6': '.digitalbrain.ui.Point3D',
      '10': 'cameraRotation'
    },
    {'1': 'zoom_depth', '3': 3, '4': 1, '5': 2, '10': 'zoomDepth'},
    {
      '1': 'camera_spring',
      '3': 4,
      '4': 1,
      '5': 11,
      '6': '.digitalbrain.ui.UiViewportSignal.SpringConfig',
      '10': 'cameraSpring'
    },
    {'1': 'ambient_glow', '3': 5, '4': 1, '5': 2, '10': 'ambientGlow'},
    {'1': 'depth_of_field', '3': 6, '4': 1, '5': 2, '10': 'depthOfField'},
  ],
  '3': [UiViewportSignal_SpringConfig$json],
};

@$core.Deprecated('Use uiViewportSignalDescriptor instead')
const UiViewportSignal_SpringConfig$json = {
  '1': 'SpringConfig',
  '2': [
    {'1': 'damping_ratio', '3': 1, '4': 1, '5': 2, '10': 'dampingRatio'},
    {'1': 'natural_freq', '3': 2, '4': 1, '5': 2, '10': 'naturalFreq'},
  ],
};

/// Descriptor for `UiViewportSignal`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List uiViewportSignalDescriptor = $convert.base64Decode(
    'ChBVaVZpZXdwb3J0U2lnbmFsEj0KDWNhbWVyYV90YXJnZXQYASABKAsyGC5kaWdpdGFsYnJhaW'
    '4udWkuUG9pbnQzRFIMY2FtZXJhVGFyZ2V0EkEKD2NhbWVyYV9yb3RhdGlvbhgCIAEoCzIYLmRp'
    'Z2l0YWxicmFpbi51aS5Qb2ludDNEUg5jYW1lcmFSb3RhdGlvbhIdCgp6b29tX2RlcHRoGAMgAS'
    'gCUgl6b29tRGVwdGgSUwoNY2FtZXJhX3NwcmluZxgEIAEoCzIuLmRpZ2l0YWxicmFpbi51aS5V'
    'aVZpZXdwb3J0U2lnbmFsLlNwcmluZ0NvbmZpZ1IMY2FtZXJhU3ByaW5nEiEKDGFtYmllbnRfZ2'
    'xvdxgFIAEoAlILYW1iaWVudEdsb3cSJAoOZGVwdGhfb2ZfZmllbGQYBiABKAJSDGRlcHRoT2ZG'
    'aWVsZBpWCgxTcHJpbmdDb25maWcSIwoNZGFtcGluZ19yYXRpbxgBIAEoAlIMZGFtcGluZ1JhdG'
    'lvEiEKDG5hdHVyYWxfZnJlcRgCIAEoAlILbmF0dXJhbEZyZXE=');

@$core.Deprecated('Use uiSoundSignalDescriptor instead')
const UiSoundSignal$json = {
  '1': 'UiSoundSignal',
  '2': [
    {'1': 'sound_effect_id', '3': 1, '4': 1, '5': 9, '10': 'soundEffectId'},
    {'1': 'volume', '3': 2, '4': 1, '5': 2, '10': 'volume'},
  ],
};

/// Descriptor for `UiSoundSignal`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List uiSoundSignalDescriptor = $convert.base64Decode(
    'Cg1VaVNvdW5kU2lnbmFsEiYKD3NvdW5kX2VmZmVjdF9pZBgBIAEoCVINc291bmRFZmZlY3RJZB'
    'IWCgZ2b2x1bWUYAiABKAJSBnZvbHVtZQ==');
