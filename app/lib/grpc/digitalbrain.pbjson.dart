// This is a generated file - do not edit.
//
// Generated from digitalbrain.proto.

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

@$core.Deprecated('Use synapseEnvelopeDescriptor instead')
const SynapseEnvelope$json = {
  '1': 'SynapseEnvelope',
  '2': [
    {'1': 'correlation_id', '3': 1, '4': 1, '5': 9, '10': 'correlationId'},
    {'1': 'type_name', '3': 2, '4': 1, '5': 9, '10': 'typeName'},
    {'1': 'payload', '3': 3, '4': 1, '5': 12, '10': 'payload'},
  ],
};

/// Descriptor for `SynapseEnvelope`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List synapseEnvelopeDescriptor = $convert.base64Decode(
    'Cg9TeW5hcHNlRW52ZWxvcGUSJQoOY29ycmVsYXRpb25faWQYASABKAlSDWNvcnJlbGF0aW9uSW'
    'QSGwoJdHlwZV9uYW1lGAIgASgJUgh0eXBlTmFtZRIYCgdwYXlsb2FkGAMgASgMUgdwYXlsb2Fk');

@$core.Deprecated('Use watchHomeFeedRequestDescriptor instead')
const WatchHomeFeedRequest$json = {
  '1': 'WatchHomeFeedRequest',
};

/// Descriptor for `WatchHomeFeedRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List watchHomeFeedRequestDescriptor =
    $convert.base64Decode('ChRXYXRjaEhvbWVGZWVkUmVxdWVzdA==');

@$core.Deprecated('Use rfwCardEnvelopeDescriptor instead')
const RfwCardEnvelope$json = {
  '1': 'RfwCardEnvelope',
  '2': [
    {'1': 'correlation_id', '3': 1, '4': 1, '5': 9, '10': 'correlationId'},
    {'1': 'library_name', '3': 2, '4': 1, '5': 9, '10': 'libraryName'},
    {'1': 'root_widget', '3': 3, '4': 1, '5': 9, '10': 'rootWidget'},
    {'1': 'data_json', '3': 4, '4': 1, '5': 9, '10': 'dataJson'},
    {'1': 'timestamp', '3': 5, '4': 1, '5': 9, '10': 'timestamp'},
    {
      '1': 'caller_neuron_type',
      '3': 6,
      '4': 1,
      '5': 9,
      '10': 'callerNeuronType'
    },
  ],
};

/// Descriptor for `RfwCardEnvelope`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List rfwCardEnvelopeDescriptor = $convert.base64Decode(
    'Cg9SZndDYXJkRW52ZWxvcGUSJQoOY29ycmVsYXRpb25faWQYASABKAlSDWNvcnJlbGF0aW9uSW'
    'QSIQoMbGlicmFyeV9uYW1lGAIgASgJUgtsaWJyYXJ5TmFtZRIfCgtyb290X3dpZGdldBgDIAEo'
    'CVIKcm9vdFdpZGdldBIbCglkYXRhX2pzb24YBCABKAlSCGRhdGFKc29uEhwKCXRpbWVzdGFtcB'
    'gFIAEoCVIJdGltZXN0YW1wEiwKEmNhbGxlcl9uZXVyb25fdHlwZRgGIAEoCVIQY2FsbGVyTmV1'
    'cm9uVHlwZQ==');

@$core.Deprecated('Use transcribeRequestDescriptor instead')
const TranscribeRequest$json = {
  '1': 'TranscribeRequest',
  '2': [
    {'1': 'audio_chunk', '3': 1, '4': 1, '5': 12, '10': 'audioChunk'},
    {'1': 'mime_type', '3': 2, '4': 1, '5': 9, '10': 'mimeType'},
    {'1': 'language_hint', '3': 3, '4': 1, '5': 9, '10': 'languageHint'},
  ],
};

/// Descriptor for `TranscribeRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List transcribeRequestDescriptor = $convert.base64Decode(
    'ChFUcmFuc2NyaWJlUmVxdWVzdBIfCgthdWRpb19jaHVuaxgBIAEoDFIKYXVkaW9DaHVuaxIbCg'
    'ltaW1lX3R5cGUYAiABKAlSCG1pbWVUeXBlEiMKDWxhbmd1YWdlX2hpbnQYAyABKAlSDGxhbmd1'
    'YWdlSGludA==');

@$core.Deprecated('Use transcribeResponseDescriptor instead')
const TranscribeResponse$json = {
  '1': 'TranscribeResponse',
  '2': [
    {'1': 'transcript', '3': 1, '4': 1, '5': 9, '10': 'transcript'},
    {
      '1': 'detected_language',
      '3': 2,
      '4': 1,
      '5': 9,
      '10': 'detectedLanguage'
    },
    {'1': 'correlation_id', '3': 3, '4': 1, '5': 9, '10': 'correlationId'},
  ],
};

/// Descriptor for `TranscribeResponse`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List transcribeResponseDescriptor = $convert.base64Decode(
    'ChJUcmFuc2NyaWJlUmVzcG9uc2USHgoKdHJhbnNjcmlwdBgBIAEoCVIKdHJhbnNjcmlwdBIrCh'
    'FkZXRlY3RlZF9sYW5ndWFnZRgCIAEoCVIQZGV0ZWN0ZWRMYW5ndWFnZRIlCg5jb3JyZWxhdGlv'
    'bl9pZBgDIAEoCVINY29ycmVsYXRpb25JZA==');

@$core.Deprecated('Use aiHealthRequestDescriptor instead')
const AiHealthRequest$json = {
  '1': 'AiHealthRequest',
};

/// Descriptor for `AiHealthRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List aiHealthRequestDescriptor =
    $convert.base64Decode('Cg9BaUhlYWx0aFJlcXVlc3Q=');

@$core.Deprecated('Use aiHealthResponseDescriptor instead')
const AiHealthResponse$json = {
  '1': 'AiHealthResponse',
  '2': [
    {'1': 'live', '3': 1, '4': 1, '5': 8, '10': 'live'},
    {'1': 'reason', '3': 2, '4': 1, '5': 9, '10': 'reason'},
    {'1': 'model', '3': 3, '4': 1, '5': 9, '10': 'model'},
  ],
};

/// Descriptor for `AiHealthResponse`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List aiHealthResponseDescriptor = $convert.base64Decode(
    'ChBBaUhlYWx0aFJlc3BvbnNlEhIKBGxpdmUYASABKAhSBGxpdmUSFgoGcmVhc29uGAIgASgJUg'
    'ZyZWFzb24SFAoFbW9kZWwYAyABKAlSBW1vZGVs');

@$core.Deprecated('Use submitPromptRequestDescriptor instead')
const SubmitPromptRequest$json = {
  '1': 'SubmitPromptRequest',
  '2': [
    {'1': 'user_id', '3': 1, '4': 1, '5': 9, '10': 'userId'},
    {'1': 'text', '3': 2, '4': 1, '5': 9, '10': 'text'},
    {'1': 'correlation_id', '3': 3, '4': 1, '5': 9, '10': 'correlationId'},
  ],
};

/// Descriptor for `SubmitPromptRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List submitPromptRequestDescriptor = $convert.base64Decode(
    'ChNTdWJtaXRQcm9tcHRSZXF1ZXN0EhcKB3VzZXJfaWQYASABKAlSBnVzZXJJZBISCgR0ZXh0GA'
    'IgASgJUgR0ZXh0EiUKDmNvcnJlbGF0aW9uX2lkGAMgASgJUg1jb3JyZWxhdGlvbklk');

@$core.Deprecated('Use submitPromptReplyDescriptor instead')
const SubmitPromptReply$json = {
  '1': 'SubmitPromptReply',
  '2': [
    {'1': 'correlation_id', '3': 1, '4': 1, '5': 9, '10': 'correlationId'},
  ],
};

/// Descriptor for `SubmitPromptReply`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List submitPromptReplyDescriptor = $convert.base64Decode(
    'ChFTdWJtaXRQcm9tcHRSZXBseRIlCg5jb3JyZWxhdGlvbl9pZBgBIAEoCVINY29ycmVsYXRpb2'
    '5JZA==');

@$core.Deprecated('Use flutterPerfSampleProtoDescriptor instead')
const FlutterPerfSampleProto$json = {
  '1': 'FlutterPerfSampleProto',
  '2': [
    {'1': 'client_id', '3': 1, '4': 1, '5': 9, '10': 'clientId'},
    {'1': 'sample_window_id', '3': 2, '4': 1, '5': 9, '10': 'sampleWindowId'},
    {'1': 'frame_count', '3': 3, '4': 1, '5': 5, '10': 'frameCount'},
    {'1': 'p50_frame_ms', '3': 4, '4': 1, '5': 1, '10': 'p50FrameMs'},
    {'1': 'p95_frame_ms', '3': 5, '4': 1, '5': 1, '10': 'p95FrameMs'},
    {'1': 'jank_pct', '3': 6, '4': 1, '5': 1, '10': 'jankPct'},
    {'1': 'widget_count', '3': 7, '4': 1, '5': 5, '10': 'widgetCount'},
    {
      '1': 'glow_painter_count',
      '3': 8,
      '4': 1,
      '5': 5,
      '10': 'glowPainterCount'
    },
    {
      '1': 'rebuilds_per_second',
      '3': 9,
      '4': 1,
      '5': 5,
      '10': 'rebuildsPerSecond'
    },
    {'1': 'platform', '3': 10, '4': 1, '5': 9, '10': 'platform'},
    {'1': 'timestamp', '3': 11, '4': 1, '5': 9, '10': 'timestamp'},
  ],
};

/// Descriptor for `FlutterPerfSampleProto`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List flutterPerfSampleProtoDescriptor = $convert.base64Decode(
    'ChZGbHV0dGVyUGVyZlNhbXBsZVByb3RvEhsKCWNsaWVudF9pZBgBIAEoCVIIY2xpZW50SWQSKA'
    'oQc2FtcGxlX3dpbmRvd19pZBgCIAEoCVIOc2FtcGxlV2luZG93SWQSHwoLZnJhbWVfY291bnQY'
    'AyABKAVSCmZyYW1lQ291bnQSIAoMcDUwX2ZyYW1lX21zGAQgASgBUgpwNTBGcmFtZU1zEiAKDH'
    'A5NV9mcmFtZV9tcxgFIAEoAVIKcDk1RnJhbWVNcxIZCghqYW5rX3BjdBgGIAEoAVIHamFua1Bj'
    'dBIhCgx3aWRnZXRfY291bnQYByABKAVSC3dpZGdldENvdW50EiwKEmdsb3dfcGFpbnRlcl9jb3'
    'VudBgIIAEoBVIQZ2xvd1BhaW50ZXJDb3VudBIuChNyZWJ1aWxkc19wZXJfc2Vjb25kGAkgASgF'
    'UhFyZWJ1aWxkc1BlclNlY29uZBIaCghwbGF0Zm9ybRgKIAEoCVIIcGxhdGZvcm0SHAoJdGltZX'
    'N0YW1wGAsgASgJUgl0aW1lc3RhbXA=');

@$core.Deprecated('Use flutterPerfAckDescriptor instead')
const FlutterPerfAck$json = {
  '1': 'FlutterPerfAck',
};

/// Descriptor for `FlutterPerfAck`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List flutterPerfAckDescriptor =
    $convert.base64Decode('Cg5GbHV0dGVyUGVyZkFjaw==');

@$core.Deprecated('Use watchVisualLoadHintRequestDescriptor instead')
const WatchVisualLoadHintRequest$json = {
  '1': 'WatchVisualLoadHintRequest',
  '2': [
    {'1': 'client_id', '3': 1, '4': 1, '5': 9, '10': 'clientId'},
  ],
};

/// Descriptor for `WatchVisualLoadHintRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List watchVisualLoadHintRequestDescriptor =
    $convert.base64Decode(
        'ChpXYXRjaFZpc3VhbExvYWRIaW50UmVxdWVzdBIbCgljbGllbnRfaWQYASABKAlSCGNsaWVudE'
        'lk');

@$core.Deprecated('Use visualLoadHintProtoDescriptor instead')
const VisualLoadHintProto$json = {
  '1': 'VisualLoadHintProto',
  '2': [
    {'1': 'client_id', '3': 1, '4': 1, '5': 9, '10': 'clientId'},
    {'1': 'tier', '3': 2, '4': 1, '5': 9, '10': 'tier'},
    {'1': 'reason', '3': 3, '4': 1, '5': 9, '10': 'reason'},
    {'1': 'timestamp', '3': 4, '4': 1, '5': 9, '10': 'timestamp'},
  ],
};

/// Descriptor for `VisualLoadHintProto`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List visualLoadHintProtoDescriptor = $convert.base64Decode(
    'ChNWaXN1YWxMb2FkSGludFByb3RvEhsKCWNsaWVudF9pZBgBIAEoCVIIY2xpZW50SWQSEgoEdG'
    'llchgCIAEoCVIEdGllchIWCgZyZWFzb24YAyABKAlSBnJlYXNvbhIcCgl0aW1lc3RhbXAYBCAB'
    'KAlSCXRpbWVzdGFtcA==');

@$core.Deprecated('Use getLatestCardRequestDescriptor instead')
const GetLatestCardRequest$json = {
  '1': 'GetLatestCardRequest',
  '2': [
    {'1': 'neuron_id', '3': 1, '4': 1, '5': 9, '10': 'neuronId'},
  ],
};

/// Descriptor for `GetLatestCardRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List getLatestCardRequestDescriptor =
    $convert.base64Decode(
        'ChRHZXRMYXRlc3RDYXJkUmVxdWVzdBIbCgluZXVyb25faWQYASABKAlSCG5ldXJvbklk');

@$core.Deprecated('Use getLatestCardReplyDescriptor instead')
const GetLatestCardReply$json = {
  '1': 'GetLatestCardReply',
  '2': [
    {
      '1': 'card',
      '3': 1,
      '4': 1,
      '5': 11,
      '6': '.digitalbrain.RfwCardEnvelope',
      '10': 'card'
    },
    {'1': 'has_card', '3': 2, '4': 1, '5': 8, '10': 'hasCard'},
  ],
};

/// Descriptor for `GetLatestCardReply`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List getLatestCardReplyDescriptor = $convert.base64Decode(
    'ChJHZXRMYXRlc3RDYXJkUmVwbHkSMQoEY2FyZBgBIAEoCzIdLmRpZ2l0YWxicmFpbi5SZndDYX'
    'JkRW52ZWxvcGVSBGNhcmQSGQoIaGFzX2NhcmQYAiABKAhSB2hhc0NhcmQ=');

@$core.Deprecated('Use rfwLayoutRequestDescriptor instead')
const RfwLayoutRequest$json = {
  '1': 'RfwLayoutRequest',
  '2': [
    {'1': 'layout_name', '3': 1, '4': 1, '5': 9, '10': 'layoutName'},
    {'1': 'neuron_id', '3': 2, '4': 1, '5': 9, '10': 'neuronId'},
  ],
};

/// Descriptor for `RfwLayoutRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List rfwLayoutRequestDescriptor = $convert.base64Decode(
    'ChBSZndMYXlvdXRSZXF1ZXN0Eh8KC2xheW91dF9uYW1lGAEgASgJUgpsYXlvdXROYW1lEhsKCW'
    '5ldXJvbl9pZBgCIAEoCVIIbmV1cm9uSWQ=');

@$core.Deprecated('Use rfwLayoutReplyDescriptor instead')
const RfwLayoutReply$json = {
  '1': 'RfwLayoutReply',
  '2': [
    {'1': 'rfw_template', '3': 1, '4': 1, '5': 9, '10': 'rfwTemplate'},
    {'1': 'data_json', '3': 2, '4': 1, '5': 9, '10': 'dataJson'},
  ],
};

/// Descriptor for `RfwLayoutReply`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List rfwLayoutReplyDescriptor = $convert.base64Decode(
    'Cg5SZndMYXlvdXRSZXBseRIhCgxyZndfdGVtcGxhdGUYASABKAlSC3Jmd1RlbXBsYXRlEhsKCW'
    'RhdGFfanNvbhgCIAEoCVIIZGF0YUpzb24=');
