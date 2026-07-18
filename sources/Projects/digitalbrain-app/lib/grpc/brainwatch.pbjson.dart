// This is a generated file - do not edit.
//
// Generated from brainwatch.proto.

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

@$core.Deprecated('Use watchSynapsesRequestDescriptor instead')
const WatchSynapsesRequest$json = {
  '1': 'WatchSynapsesRequest',
  '2': [
    {'1': 'brain_id', '3': 1, '4': 1, '5': 9, '10': 'brainId'},
  ],
};

/// Descriptor for `WatchSynapsesRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List watchSynapsesRequestDescriptor =
    $convert.base64Decode(
        'ChRXYXRjaFN5bmFwc2VzUmVxdWVzdBIZCghicmFpbl9pZBgBIAEoCVIHYnJhaW5JZA==');

@$core.Deprecated('Use snapshotRequestDescriptor instead')
const SnapshotRequest$json = {
  '1': 'SnapshotRequest',
  '2': [
    {
      '1': 'since',
      '3': 1,
      '4': 1,
      '5': 11,
      '6': '.google.protobuf.Timestamp',
      '10': 'since'
    },
  ],
};

/// Descriptor for `SnapshotRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List snapshotRequestDescriptor = $convert.base64Decode(
    'Cg9TbmFwc2hvdFJlcXVlc3QSMAoFc2luY2UYASABKAsyGi5nb29nbGUucHJvdG9idWYuVGltZX'
    'N0YW1wUgVzaW5jZQ==');

@$core.Deprecated('Use snapshotResponseDescriptor instead')
const SnapshotResponse$json = {
  '1': 'SnapshotResponse',
  '2': [
    {
      '1': 'nodes',
      '3': 1,
      '4': 3,
      '5': 11,
      '6': '.digitalbrain.NeuronNode',
      '10': 'nodes'
    },
    {
      '1': 'edges',
      '3': 2,
      '4': 3,
      '5': 11,
      '6': '.digitalbrain.SynapseEdge',
      '10': 'edges'
    },
    {'1': 'cursor', '3': 3, '4': 1, '5': 3, '10': 'cursor'},
  ],
};

/// Descriptor for `SnapshotResponse`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List snapshotResponseDescriptor = $convert.base64Decode(
    'ChBTbmFwc2hvdFJlc3BvbnNlEi4KBW5vZGVzGAEgAygLMhguZGlnaXRhbGJyYWluLk5ldXJvbk'
    '5vZGVSBW5vZGVzEi8KBWVkZ2VzGAIgAygLMhkuZGlnaXRhbGJyYWluLlN5bmFwc2VFZGdlUgVl'
    'ZGdlcxIWCgZjdXJzb3IYAyABKANSBmN1cnNvcg==');

@$core.Deprecated('Use watchSinceRequestDescriptor instead')
const WatchSinceRequest$json = {
  '1': 'WatchSinceRequest',
  '2': [
    {'1': 'cursor', '3': 1, '4': 1, '5': 3, '10': 'cursor'},
  ],
};

/// Descriptor for `WatchSinceRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List watchSinceRequestDescriptor = $convert.base64Decode(
    'ChFXYXRjaFNpbmNlUmVxdWVzdBIWCgZjdXJzb3IYASABKANSBmN1cnNvcg==');

@$core.Deprecated('Use watchSinceResponseDescriptor instead')
const WatchSinceResponse$json = {
  '1': 'WatchSinceResponse',
  '2': [
    {
      '1': 'new_nodes',
      '3': 1,
      '4': 3,
      '5': 11,
      '6': '.digitalbrain.NeuronNode',
      '10': 'newNodes'
    },
    {
      '1': 'new_edges',
      '3': 2,
      '4': 3,
      '5': 11,
      '6': '.digitalbrain.SynapseEdge',
      '10': 'newEdges'
    },
    {'1': 'cursor', '3': 3, '4': 1, '5': 3, '10': 'cursor'},
  ],
};

/// Descriptor for `WatchSinceResponse`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List watchSinceResponseDescriptor = $convert.base64Decode(
    'ChJXYXRjaFNpbmNlUmVzcG9uc2USNQoJbmV3X25vZGVzGAEgAygLMhguZGlnaXRhbGJyYWluLk'
    '5ldXJvbk5vZGVSCG5ld05vZGVzEjYKCW5ld19lZGdlcxgCIAMoCzIZLmRpZ2l0YWxicmFpbi5T'
    'eW5hcHNlRWRnZVIIbmV3RWRnZXMSFgoGY3Vyc29yGAMgASgDUgZjdXJzb3I=');

@$core.Deprecated('Use neuronDetailRequestDescriptor instead')
const NeuronDetailRequest$json = {
  '1': 'NeuronDetailRequest',
  '2': [
    {'1': 'neuron_id', '3': 1, '4': 1, '5': 9, '10': 'neuronId'},
    {
      '1': 'since',
      '3': 2,
      '4': 1,
      '5': 11,
      '6': '.google.protobuf.Timestamp',
      '10': 'since'
    },
  ],
};

/// Descriptor for `NeuronDetailRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List neuronDetailRequestDescriptor = $convert.base64Decode(
    'ChNOZXVyb25EZXRhaWxSZXF1ZXN0EhsKCW5ldXJvbl9pZBgBIAEoCVIIbmV1cm9uSWQSMAoFc2'
    'luY2UYAiABKAsyGi5nb29nbGUucHJvdG9idWYuVGltZXN0YW1wUgVzaW5jZQ==');

@$core.Deprecated('Use neuronDetailResponseDescriptor instead')
const NeuronDetailResponse$json = {
  '1': 'NeuronDetailResponse',
  '2': [
    {'1': 'neuron_id', '3': 1, '4': 1, '5': 9, '10': 'neuronId'},
    {
      '1': 'first_seen_at',
      '3': 2,
      '4': 1,
      '5': 11,
      '6': '.google.protobuf.Timestamp',
      '10': 'firstSeenAt'
    },
    {
      '1': 'last_seen_at',
      '3': 3,
      '4': 1,
      '5': 11,
      '6': '.google.protobuf.Timestamp',
      '10': 'lastSeenAt'
    },
    {
      '1': 'recent',
      '3': 4,
      '4': 3,
      '5': 11,
      '6': '.digitalbrain.SynapseEdge',
      '10': 'recent'
    },
  ],
};

/// Descriptor for `NeuronDetailResponse`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List neuronDetailResponseDescriptor = $convert.base64Decode(
    'ChROZXVyb25EZXRhaWxSZXNwb25zZRIbCgluZXVyb25faWQYASABKAlSCG5ldXJvbklkEj4KDW'
    'ZpcnN0X3NlZW5fYXQYAiABKAsyGi5nb29nbGUucHJvdG9idWYuVGltZXN0YW1wUgtmaXJzdFNl'
    'ZW5BdBI8CgxsYXN0X3NlZW5fYXQYAyABKAsyGi5nb29nbGUucHJvdG9idWYuVGltZXN0YW1wUg'
    'psYXN0U2VlbkF0EjEKBnJlY2VudBgEIAMoCzIZLmRpZ2l0YWxicmFpbi5TeW5hcHNlRWRnZVIG'
    'cmVjZW50');

@$core.Deprecated('Use synapseDetailRequestDescriptor instead')
const SynapseDetailRequest$json = {
  '1': 'SynapseDetailRequest',
  '2': [
    {'1': 'correlation_id', '3': 1, '4': 1, '5': 9, '10': 'correlationId'},
  ],
};

/// Descriptor for `SynapseDetailRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List synapseDetailRequestDescriptor = $convert.base64Decode(
    'ChRTeW5hcHNlRGV0YWlsUmVxdWVzdBIlCg5jb3JyZWxhdGlvbl9pZBgBIAEoCVINY29ycmVsYX'
    'Rpb25JZA==');

@$core.Deprecated('Use synapseDetailResponseDescriptor instead')
const SynapseDetailResponse$json = {
  '1': 'SynapseDetailResponse',
  '2': [
    {
      '1': 'chain',
      '3': 1,
      '4': 3,
      '5': 11,
      '6': '.digitalbrain.SynapseEdge',
      '10': 'chain'
    },
  ],
};

/// Descriptor for `SynapseDetailResponse`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List synapseDetailResponseDescriptor = $convert.base64Decode(
    'ChVTeW5hcHNlRGV0YWlsUmVzcG9uc2USLwoFY2hhaW4YASADKAsyGS5kaWdpdGFsYnJhaW4uU3'
    'luYXBzZUVkZ2VSBWNoYWlu');

@$core.Deprecated('Use neuronFeatureRequestDescriptor instead')
const NeuronFeatureRequest$json = {
  '1': 'NeuronFeatureRequest',
  '2': [
    {'1': 'neuron_id', '3': 1, '4': 1, '5': 9, '10': 'neuronId'},
  ],
};

/// Descriptor for `NeuronFeatureRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List neuronFeatureRequestDescriptor =
    $convert.base64Decode(
        'ChROZXVyb25GZWF0dXJlUmVxdWVzdBIbCgluZXVyb25faWQYASABKAlSCG5ldXJvbklk');

@$core.Deprecated('Use neuronFeatureResponseDescriptor instead')
const NeuronFeatureResponse$json = {
  '1': 'NeuronFeatureResponse',
  '2': [
    {'1': 'feature_text', '3': 1, '4': 1, '5': 9, '10': 'featureText'},
    {'1': 'source_file', '3': 2, '4': 1, '5': 9, '10': 'sourceFile'},
  ],
};

/// Descriptor for `NeuronFeatureResponse`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List neuronFeatureResponseDescriptor = $convert.base64Decode(
    'ChVOZXVyb25GZWF0dXJlUmVzcG9uc2USIQoMZmVhdHVyZV90ZXh0GAEgASgJUgtmZWF0dXJlVG'
    'V4dBIfCgtzb3VyY2VfZmlsZRgCIAEoCVIKc291cmNlRmlsZQ==');

@$core.Deprecated('Use neuronNodeDescriptor instead')
const NeuronNode$json = {
  '1': 'NeuronNode',
  '2': [
    {'1': 'id', '3': 1, '4': 1, '5': 9, '10': 'id'},
    {
      '1': 'first_seen_at',
      '3': 2,
      '4': 1,
      '5': 11,
      '6': '.google.protobuf.Timestamp',
      '10': 'firstSeenAt'
    },
    {
      '1': 'last_seen_at',
      '3': 3,
      '4': 1,
      '5': 11,
      '6': '.google.protobuf.Timestamp',
      '10': 'lastSeenAt'
    },
    {'1': 'domain', '3': 5, '4': 1, '5': 9, '10': 'domain'},
  ],
  '9': [
    {'1': 4, '2': 5},
  ],
};

/// Descriptor for `NeuronNode`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List neuronNodeDescriptor = $convert.base64Decode(
    'CgpOZXVyb25Ob2RlEg4KAmlkGAEgASgJUgJpZBI+Cg1maXJzdF9zZWVuX2F0GAIgASgLMhouZ2'
    '9vZ2xlLnByb3RvYnVmLlRpbWVzdGFtcFILZmlyc3RTZWVuQXQSPAoMbGFzdF9zZWVuX2F0GAMg'
    'ASgLMhouZ29vZ2xlLnByb3RvYnVmLlRpbWVzdGFtcFIKbGFzdFNlZW5BdBIWCgZkb21haW4YBS'
    'ABKAlSBmRvbWFpbkoECAQQBQ==');

@$core.Deprecated('Use synapseEdgeDescriptor instead')
const SynapseEdge$json = {
  '1': 'SynapseEdge',
  '2': [
    {
      '1': 'at',
      '3': 1,
      '4': 1,
      '5': 11,
      '6': '.google.protobuf.Timestamp',
      '10': 'at'
    },
    {'1': 'from_id', '3': 2, '4': 1, '5': 9, '10': 'fromId'},
    {'1': 'to_id', '3': 3, '4': 1, '5': 9, '10': 'toId'},
    {'1': 'type_name', '3': 4, '4': 1, '5': 9, '10': 'typeName'},
    {'1': 'method_name', '3': 5, '4': 1, '5': 9, '10': 'methodName'},
    {'1': 'correlation_id', '3': 6, '4': 1, '5': 9, '10': 'correlationId'},
    {'1': 'payload', '3': 7, '4': 1, '5': 12, '10': 'payload'},
  ],
};

/// Descriptor for `SynapseEdge`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List synapseEdgeDescriptor = $convert.base64Decode(
    'CgtTeW5hcHNlRWRnZRIqCgJhdBgBIAEoCzIaLmdvb2dsZS5wcm90b2J1Zi5UaW1lc3RhbXBSAm'
    'F0EhcKB2Zyb21faWQYAiABKAlSBmZyb21JZBITCgV0b19pZBgDIAEoCVIEdG9JZBIbCgl0eXBl'
    'X25hbWUYBCABKAlSCHR5cGVOYW1lEh8KC21ldGhvZF9uYW1lGAUgASgJUgptZXRob2ROYW1lEi'
    'UKDmNvcnJlbGF0aW9uX2lkGAYgASgJUg1jb3JyZWxhdGlvbklkEhgKB3BheWxvYWQYByABKAxS'
    'B3BheWxvYWQ=');
