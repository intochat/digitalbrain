// This is a generated file - do not edit.
//
// Generated from brainregistry.proto.

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

@$core.Deprecated('Use listBrainsRequestDescriptor instead')
const ListBrainsRequest$json = {
  '1': 'ListBrainsRequest',
};

/// Descriptor for `ListBrainsRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List listBrainsRequestDescriptor =
    $convert.base64Decode('ChFMaXN0QnJhaW5zUmVxdWVzdA==');

@$core.Deprecated('Use brainsResultDescriptor instead')
const BrainsResult$json = {
  '1': 'BrainsResult',
  '2': [
    {
      '1': 'brains',
      '3': 1,
      '4': 3,
      '5': 11,
      '6': '.digitalbrain.BrainSummary',
      '10': 'brains'
    },
  ],
};

/// Descriptor for `BrainsResult`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List brainsResultDescriptor = $convert.base64Decode(
    'CgxCcmFpbnNSZXN1bHQSMgoGYnJhaW5zGAEgAygLMhouZGlnaXRhbGJyYWluLkJyYWluU3VtbW'
    'FyeVIGYnJhaW5z');

@$core.Deprecated('Use watchActivityRequestDescriptor instead')
const WatchActivityRequest$json = {
  '1': 'WatchActivityRequest',
};

/// Descriptor for `WatchActivityRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List watchActivityRequestDescriptor =
    $convert.base64Decode('ChRXYXRjaEFjdGl2aXR5UmVxdWVzdA==');

@$core.Deprecated('Use brainActivityDeltaDescriptor instead')
const BrainActivityDelta$json = {
  '1': 'BrainActivityDelta',
  '2': [
    {'1': 'brain_id', '3': 1, '4': 1, '5': 9, '10': 'brainId'},
    {
      '1': 'synapses_per_second',
      '3': 2,
      '4': 1,
      '5': 1,
      '10': 'synapsesPerSecond'
    },
    {
      '1': 'neuron_count_delta',
      '3': 3,
      '4': 1,
      '5': 5,
      '10': 'neuronCountDelta'
    },
    {'1': 'version_bump', '3': 4, '4': 1, '5': 9, '10': 'versionBump'},
  ],
};

/// Descriptor for `BrainActivityDelta`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List brainActivityDeltaDescriptor = $convert.base64Decode(
    'ChJCcmFpbkFjdGl2aXR5RGVsdGESGQoIYnJhaW5faWQYASABKAlSB2JyYWluSWQSLgoTc3luYX'
    'BzZXNfcGVyX3NlY29uZBgCIAEoAVIRc3luYXBzZXNQZXJTZWNvbmQSLAoSbmV1cm9uX2NvdW50'
    'X2RlbHRhGAMgASgFUhBuZXVyb25Db3VudERlbHRhEiEKDHZlcnNpb25fYnVtcBgEIAEoCVILdm'
    'Vyc2lvbkJ1bXA=');

@$core.Deprecated('Use createBrainCommandDescriptor instead')
const CreateBrainCommand$json = {
  '1': 'CreateBrainCommand',
  '2': [
    {'1': 'name', '3': 1, '4': 1, '5': 9, '10': 'name'},
    {'1': 'seed_template', '3': 2, '4': 1, '5': 9, '10': 'seedTemplate'},
  ],
};

/// Descriptor for `CreateBrainCommand`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List createBrainCommandDescriptor = $convert.base64Decode(
    'ChJDcmVhdGVCcmFpbkNvbW1hbmQSEgoEbmFtZRgBIAEoCVIEbmFtZRIjCg1zZWVkX3RlbXBsYX'
    'RlGAIgASgJUgxzZWVkVGVtcGxhdGU=');

@$core.Deprecated('Use brainCreatedDescriptor instead')
const BrainCreated$json = {
  '1': 'BrainCreated',
  '2': [
    {'1': 'brain_id', '3': 1, '4': 1, '5': 9, '10': 'brainId'},
    {'1': 'success', '3': 2, '4': 1, '5': 8, '10': 'success'},
    {'1': 'error_message', '3': 3, '4': 1, '5': 9, '10': 'errorMessage'},
  ],
};

/// Descriptor for `BrainCreated`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List brainCreatedDescriptor = $convert.base64Decode(
    'CgxCcmFpbkNyZWF0ZWQSGQoIYnJhaW5faWQYASABKAlSB2JyYWluSWQSGAoHc3VjY2VzcxgCIA'
    'EoCFIHc3VjY2VzcxIjCg1lcnJvcl9tZXNzYWdlGAMgASgJUgxlcnJvck1lc3NhZ2U=');

@$core.Deprecated('Use renameBrainCommandDescriptor instead')
const RenameBrainCommand$json = {
  '1': 'RenameBrainCommand',
  '2': [
    {'1': 'brain_id', '3': 1, '4': 1, '5': 9, '10': 'brainId'},
    {'1': 'new_name', '3': 2, '4': 1, '5': 9, '10': 'newName'},
  ],
};

/// Descriptor for `RenameBrainCommand`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List renameBrainCommandDescriptor = $convert.base64Decode(
    'ChJSZW5hbWVCcmFpbkNvbW1hbmQSGQoIYnJhaW5faWQYASABKAlSB2JyYWluSWQSGQoIbmV3X2'
    '5hbWUYAiABKAlSB25ld05hbWU=');

@$core.Deprecated('Use brainRenamedDescriptor instead')
const BrainRenamed$json = {
  '1': 'BrainRenamed',
  '2': [
    {'1': 'brain_id', '3': 1, '4': 1, '5': 9, '10': 'brainId'},
    {'1': 'success', '3': 2, '4': 1, '5': 8, '10': 'success'},
    {'1': 'error_message', '3': 3, '4': 1, '5': 9, '10': 'errorMessage'},
  ],
};

/// Descriptor for `BrainRenamed`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List brainRenamedDescriptor = $convert.base64Decode(
    'CgxCcmFpblJlbmFtZWQSGQoIYnJhaW5faWQYASABKAlSB2JyYWluSWQSGAoHc3VjY2VzcxgCIA'
    'EoCFIHc3VjY2VzcxIjCg1lcnJvcl9tZXNzYWdlGAMgASgJUgxlcnJvck1lc3NhZ2U=');

@$core.Deprecated('Use deleteBrainCommandDescriptor instead')
const DeleteBrainCommand$json = {
  '1': 'DeleteBrainCommand',
  '2': [
    {'1': 'brain_id', '3': 1, '4': 1, '5': 9, '10': 'brainId'},
  ],
};

/// Descriptor for `DeleteBrainCommand`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List deleteBrainCommandDescriptor =
    $convert.base64Decode(
        'ChJEZWxldGVCcmFpbkNvbW1hbmQSGQoIYnJhaW5faWQYASABKAlSB2JyYWluSWQ=');

@$core.Deprecated('Use brainDeletedDescriptor instead')
const BrainDeleted$json = {
  '1': 'BrainDeleted',
  '2': [
    {'1': 'brain_id', '3': 1, '4': 1, '5': 9, '10': 'brainId'},
    {'1': 'success', '3': 2, '4': 1, '5': 8, '10': 'success'},
    {'1': 'error_message', '3': 3, '4': 1, '5': 9, '10': 'errorMessage'},
  ],
};

/// Descriptor for `BrainDeleted`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List brainDeletedDescriptor = $convert.base64Decode(
    'CgxCcmFpbkRlbGV0ZWQSGQoIYnJhaW5faWQYASABKAlSB2JyYWluSWQSGAoHc3VjY2VzcxgCIA'
    'EoCFIHc3VjY2VzcxIjCg1lcnJvcl9tZXNzYWdlGAMgASgJUgxlcnJvck1lc3NhZ2U=');

@$core.Deprecated('Use brainSummaryDescriptor instead')
const BrainSummary$json = {
  '1': 'BrainSummary',
  '2': [
    {'1': 'id', '3': 1, '4': 1, '5': 9, '10': 'id'},
    {'1': 'name', '3': 2, '4': 1, '5': 9, '10': 'name'},
    {'1': 'version', '3': 3, '4': 1, '5': 9, '10': 'version'},
    {'1': 'brand_color', '3': 4, '4': 1, '5': 9, '10': 'brandColor'},
    {'1': 'neuron_count', '3': 5, '4': 1, '5': 5, '10': 'neuronCount'},
    {
      '1': 'last_activity_at',
      '3': 6,
      '4': 1,
      '5': 11,
      '6': '.google.protobuf.Timestamp',
      '10': 'lastActivityAt'
    },
    {
      '1': 'installed_bundle_count',
      '3': 7,
      '4': 1,
      '5': 5,
      '10': 'installedBundleCount'
    },
    {'1': 'capability_tags', '3': 8, '4': 3, '5': 9, '10': 'capabilityTags'},
  ],
};

/// Descriptor for `BrainSummary`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List brainSummaryDescriptor = $convert.base64Decode(
    'CgxCcmFpblN1bW1hcnkSDgoCaWQYASABKAlSAmlkEhIKBG5hbWUYAiABKAlSBG5hbWUSGAoHdm'
    'Vyc2lvbhgDIAEoCVIHdmVyc2lvbhIfCgticmFuZF9jb2xvchgEIAEoCVIKYnJhbmRDb2xvchIh'
    'CgxuZXVyb25fY291bnQYBSABKAVSC25ldXJvbkNvdW50EkQKEGxhc3RfYWN0aXZpdHlfYXQYBi'
    'ABKAsyGi5nb29nbGUucHJvdG9idWYuVGltZXN0YW1wUg5sYXN0QWN0aXZpdHlBdBI0ChZpbnN0'
    'YWxsZWRfYnVuZGxlX2NvdW50GAcgASgFUhRpbnN0YWxsZWRCdW5kbGVDb3VudBInCg9jYXBhYm'
    'lsaXR5X3RhZ3MYCCADKAlSDmNhcGFiaWxpdHlUYWdz');
