// This is a generated file - do not edit.
//
// Generated from ui.proto.

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

@$core.Deprecated('Use feedAudienceKindDescriptor instead')
const FeedAudienceKind$json = {
  '1': 'FeedAudienceKind',
  '2': [
    {'1': 'FEED_AUDIENCE_KIND_PRINCIPAL', '2': 0},
    {'1': 'FEED_AUDIENCE_KIND_WORKSPACE', '2': 1},
    {'1': 'FEED_AUDIENCE_KIND_PUBLIC', '2': 2},
  ],
};

/// Descriptor for `FeedAudienceKind`. Decode as a `google.protobuf.EnumDescriptorProto`.
final $typed_data.Uint8List feedAudienceKindDescriptor = $convert.base64Decode(
    'ChBGZWVkQXVkaWVuY2VLaW5kEiAKHEZFRURfQVVESUVOQ0VfS0lORF9QUklOQ0lQQUwQABIgCh'
    'xGRUVEX0FVRElFTkNFX0tJTkRfV09SS1NQQUNFEAESHQoZRkVFRF9BVURJRU5DRV9LSU5EX1BV'
    'QkxJQxAC');

@$core.Deprecated('Use bootstrapSessionRequestDescriptor instead')
const BootstrapSessionRequest$json = {
  '1': 'BootstrapSessionRequest',
  '2': [
    {'1': 'secret', '3': 1, '4': 1, '5': 9, '10': 'secret'},
  ],
};

/// Descriptor for `BootstrapSessionRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List bootstrapSessionRequestDescriptor =
    $convert.base64Decode(
        'ChdCb290c3RyYXBTZXNzaW9uUmVxdWVzdBIWCgZzZWNyZXQYASABKAlSBnNlY3JldA==');

@$core.Deprecated('Use refreshSessionRequestDescriptor instead')
const RefreshSessionRequest$json = {
  '1': 'RefreshSessionRequest',
  '2': [
    {'1': 'refresh_token', '3': 1, '4': 1, '5': 9, '10': 'refreshToken'},
  ],
};

/// Descriptor for `RefreshSessionRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List refreshSessionRequestDescriptor = $convert.base64Decode(
    'ChVSZWZyZXNoU2Vzc2lvblJlcXVlc3QSIwoNcmVmcmVzaF90b2tlbhgBIAEoCVIMcmVmcmVzaF'
    'Rva2Vu');

@$core.Deprecated('Use sessionReplyDescriptor instead')
const SessionReply$json = {
  '1': 'SessionReply',
  '2': [
    {'1': 'access_token', '3': 1, '4': 1, '5': 9, '10': 'accessToken'},
    {'1': 'refresh_token', '3': 2, '4': 1, '5': 9, '10': 'refreshToken'},
    {
      '1': 'access_expires_at_unix_ms',
      '3': 3,
      '4': 1,
      '5': 3,
      '10': 'accessExpiresAtUnixMs'
    },
    {
      '1': 'refresh_expires_at_unix_ms',
      '3': 4,
      '4': 1,
      '5': 3,
      '10': 'refreshExpiresAtUnixMs'
    },
    {'1': 'session_id', '3': 5, '4': 1, '5': 9, '10': 'sessionId'},
    {'1': 'tenant_id', '3': 6, '4': 1, '5': 9, '10': 'tenantId'},
    {'1': 'workspace_id', '3': 7, '4': 1, '5': 9, '10': 'workspaceId'},
    {'1': 'principal_id', '3': 8, '4': 1, '5': 9, '10': 'principalId'},
  ],
};

/// Descriptor for `SessionReply`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List sessionReplyDescriptor = $convert.base64Decode(
    'CgxTZXNzaW9uUmVwbHkSIQoMYWNjZXNzX3Rva2VuGAEgASgJUgthY2Nlc3NUb2tlbhIjCg1yZW'
    'ZyZXNoX3Rva2VuGAIgASgJUgxyZWZyZXNoVG9rZW4SOAoZYWNjZXNzX2V4cGlyZXNfYXRfdW5p'
    'eF9tcxgDIAEoA1IVYWNjZXNzRXhwaXJlc0F0VW5peE1zEjoKGnJlZnJlc2hfZXhwaXJlc19hdF'
    '91bml4X21zGAQgASgDUhZyZWZyZXNoRXhwaXJlc0F0VW5peE1zEh0KCnNlc3Npb25faWQYBSAB'
    'KAlSCXNlc3Npb25JZBIbCgl0ZW5hbnRfaWQYBiABKAlSCHRlbmFudElkEiEKDHdvcmtzcGFjZV'
    '9pZBgHIAEoCVILd29ya3NwYWNlSWQSIQoMcHJpbmNpcGFsX2lkGAggASgJUgtwcmluY2lwYWxJ'
    'ZA==');

@$core.Deprecated('Use watchSurfaceFeedRequestDescriptor instead')
const WatchSurfaceFeedRequest$json = {
  '1': 'WatchSurfaceFeedRequest',
  '2': [
    {'1': 'after_sequence', '3': 1, '4': 1, '5': 3, '10': 'afterSequence'},
    {
      '1': 'audience',
      '3': 2,
      '4': 1,
      '5': 14,
      '6': '.digitalbrain.v2.ui.FeedAudienceKind',
      '10': 'audience'
    },
    {
      '1': 'client_capabilities',
      '3': 3,
      '4': 3,
      '5': 9,
      '10': 'clientCapabilities'
    },
    {'1': 'max_batch_size', '3': 4, '4': 1, '5': 5, '10': 'maxBatchSize'},
  ],
};

/// Descriptor for `WatchSurfaceFeedRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List watchSurfaceFeedRequestDescriptor = $convert.base64Decode(
    'ChdXYXRjaFN1cmZhY2VGZWVkUmVxdWVzdBIlCg5hZnRlcl9zZXF1ZW5jZRgBIAEoA1INYWZ0ZX'
    'JTZXF1ZW5jZRJACghhdWRpZW5jZRgCIAEoDjIkLmRpZ2l0YWxicmFpbi52Mi51aS5GZWVkQXVk'
    'aWVuY2VLaW5kUghhdWRpZW5jZRIvChNjbGllbnRfY2FwYWJpbGl0aWVzGAMgAygJUhJjbGllbn'
    'RDYXBhYmlsaXRpZXMSJAoObWF4X2JhdGNoX3NpemUYBCABKAVSDG1heEJhdGNoU2l6ZQ==');

@$core.Deprecated('Use surfaceFeedEventDescriptor instead')
const SurfaceFeedEvent$json = {
  '1': 'SurfaceFeedEvent',
  '2': [
    {'1': 'surface_json', '3': 1, '4': 1, '5': 9, '9': 0, '10': 'surfaceJson'},
    {
      '1': 'reset',
      '3': 2,
      '4': 1,
      '5': 11,
      '6': '.digitalbrain.v2.ui.SurfaceFeedReset',
      '9': 0,
      '10': 'reset'
    },
  ],
  '8': [
    {'1': 'event'},
  ],
};

/// Descriptor for `SurfaceFeedEvent`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List surfaceFeedEventDescriptor = $convert.base64Decode(
    'ChBTdXJmYWNlRmVlZEV2ZW50EiMKDHN1cmZhY2VfanNvbhgBIAEoCUgAUgtzdXJmYWNlSnNvbh'
    'I8CgVyZXNldBgCIAEoCzIkLmRpZ2l0YWxicmFpbi52Mi51aS5TdXJmYWNlRmVlZFJlc2V0SABS'
    'BXJlc2V0QgcKBWV2ZW50');

@$core.Deprecated('Use surfaceFeedResetDescriptor instead')
const SurfaceFeedReset$json = {
  '1': 'SurfaceFeedReset',
  '2': [
    {'1': 'reason', '3': 1, '4': 1, '5': 9, '10': 'reason'},
    {'1': 'resume_sequence', '3': 2, '4': 1, '5': 3, '10': 'resumeSequence'},
    {'1': 'snapshot_json', '3': 3, '4': 3, '5': 9, '10': 'snapshotJson'},
  ],
};

/// Descriptor for `SurfaceFeedReset`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List surfaceFeedResetDescriptor = $convert.base64Decode(
    'ChBTdXJmYWNlRmVlZFJlc2V0EhYKBnJlYXNvbhgBIAEoCVIGcmVhc29uEicKD3Jlc3VtZV9zZX'
    'F1ZW5jZRgCIAEoA1IOcmVzdW1lU2VxdWVuY2USIwoNc25hcHNob3RfanNvbhgDIAMoCVIMc25h'
    'cHNob3RKc29u');

@$core.Deprecated('Use acknowledgeSurfaceFeedRequestDescriptor instead')
const AcknowledgeSurfaceFeedRequest$json = {
  '1': 'AcknowledgeSurfaceFeedRequest',
  '2': [
    {
      '1': 'audience',
      '3': 1,
      '4': 1,
      '5': 14,
      '6': '.digitalbrain.v2.ui.FeedAudienceKind',
      '10': 'audience'
    },
    {'1': 'sequence', '3': 2, '4': 1, '5': 3, '10': 'sequence'},
  ],
};

/// Descriptor for `AcknowledgeSurfaceFeedRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List acknowledgeSurfaceFeedRequestDescriptor =
    $convert.base64Decode(
        'Ch1BY2tub3dsZWRnZVN1cmZhY2VGZWVkUmVxdWVzdBJACghhdWRpZW5jZRgBIAEoDjIkLmRpZ2'
        'l0YWxicmFpbi52Mi51aS5GZWVkQXVkaWVuY2VLaW5kUghhdWRpZW5jZRIaCghzZXF1ZW5jZRgC'
        'IAEoA1IIc2VxdWVuY2U=');

@$core.Deprecated('Use acknowledgeSurfaceFeedReplyDescriptor instead')
const AcknowledgeSurfaceFeedReply$json = {
  '1': 'AcknowledgeSurfaceFeedReply',
  '2': [
    {
      '1': 'acknowledged_sequence',
      '3': 1,
      '4': 1,
      '5': 3,
      '10': 'acknowledgedSequence'
    },
  ],
};

/// Descriptor for `AcknowledgeSurfaceFeedReply`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List acknowledgeSurfaceFeedReplyDescriptor =
    $convert.base64Decode(
        'ChtBY2tub3dsZWRnZVN1cmZhY2VGZWVkUmVwbHkSMwoVYWNrbm93bGVkZ2VkX3NlcXVlbmNlGA'
        'EgASgDUhRhY2tub3dsZWRnZWRTZXF1ZW5jZQ==');

@$core.Deprecated('Use submitActionRequestDescriptor instead')
const SubmitActionRequest$json = {
  '1': 'SubmitActionRequest',
  '2': [
    {'1': 'binding_id', '3': 1, '4': 1, '5': 9, '10': 'bindingId'},
    {'1': 'action_token', '3': 2, '4': 1, '5': 9, '10': 'actionToken'},
    {'1': 'surface_id', '3': 3, '4': 1, '5': 9, '10': 'surfaceId'},
    {'1': 'surface_revision', '3': 4, '4': 1, '5': 5, '10': 'surfaceRevision'},
    {'1': 'input_json', '3': 5, '4': 1, '5': 9, '10': 'inputJson'},
  ],
};

/// Descriptor for `SubmitActionRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List submitActionRequestDescriptor = $convert.base64Decode(
    'ChNTdWJtaXRBY3Rpb25SZXF1ZXN0Eh0KCmJpbmRpbmdfaWQYASABKAlSCWJpbmRpbmdJZBIhCg'
    'xhY3Rpb25fdG9rZW4YAiABKAlSC2FjdGlvblRva2VuEh0KCnN1cmZhY2VfaWQYAyABKAlSCXN1'
    'cmZhY2VJZBIpChBzdXJmYWNlX3JldmlzaW9uGAQgASgFUg9zdXJmYWNlUmV2aXNpb24SHQoKaW'
    '5wdXRfanNvbhgFIAEoCVIJaW5wdXRKc29u');

@$core.Deprecated('Use submitActionReplyDescriptor instead')
const SubmitActionReply$json = {
  '1': 'SubmitActionReply',
  '2': [
    {'1': 'operation_id', '3': 1, '4': 1, '5': 9, '10': 'operationId'},
    {'1': 'idempotency_key', '3': 2, '4': 1, '5': 9, '10': 'idempotencyKey'},
  ],
};

/// Descriptor for `SubmitActionReply`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List submitActionReplyDescriptor = $convert.base64Decode(
    'ChFTdWJtaXRBY3Rpb25SZXBseRIhCgxvcGVyYXRpb25faWQYASABKAlSC29wZXJhdGlvbklkEi'
    'cKD2lkZW1wb3RlbmN5X2tleRgCIAEoCVIOaWRlbXBvdGVuY3lLZXk=');

@$core.Deprecated('Use logoutSessionRequestDescriptor instead')
const LogoutSessionRequest$json = {
  '1': 'LogoutSessionRequest',
  '2': [
    {'1': 'refresh_token', '3': 1, '4': 1, '5': 9, '10': 'refreshToken'},
  ],
};

/// Descriptor for `LogoutSessionRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List logoutSessionRequestDescriptor = $convert.base64Decode(
    'ChRMb2dvdXRTZXNzaW9uUmVxdWVzdBIjCg1yZWZyZXNoX3Rva2VuGAEgASgJUgxyZWZyZXNoVG'
    '9rZW4=');

@$core.Deprecated('Use logoutSessionReplyDescriptor instead')
const LogoutSessionReply$json = {
  '1': 'LogoutSessionReply',
};

/// Descriptor for `LogoutSessionReply`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List logoutSessionReplyDescriptor =
    $convert.base64Decode('ChJMb2dvdXRTZXNzaW9uUmVwbHk=');
