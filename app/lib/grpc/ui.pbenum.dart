// This is a generated file - do not edit.
//
// Generated from ui.proto.

// @dart = 3.3

// ignore_for_file: annotate_overrides, camel_case_types, comment_references
// ignore_for_file: constant_identifier_names
// ignore_for_file: curly_braces_in_flow_control_structures
// ignore_for_file: deprecated_member_use_from_same_package, library_prefixes
// ignore_for_file: non_constant_identifier_names, prefer_relative_imports

import 'dart:core' as $core;

import 'package:protobuf/protobuf.dart' as $pb;

class FeedAudienceKind extends $pb.ProtobufEnum {
  static const FeedAudienceKind FEED_AUDIENCE_KIND_ACTOR =
      FeedAudienceKind._(0, _omitEnumNames ? '' : 'FEED_AUDIENCE_KIND_ACTOR');
  static const FeedAudienceKind FEED_AUDIENCE_KIND_OWNER =
      FeedAudienceKind._(1, _omitEnumNames ? '' : 'FEED_AUDIENCE_KIND_OWNER');
  static const FeedAudienceKind FEED_AUDIENCE_KIND_PUBLIC =
      FeedAudienceKind._(2, _omitEnumNames ? '' : 'FEED_AUDIENCE_KIND_PUBLIC');

  static const $core.List<FeedAudienceKind> values = <FeedAudienceKind>[
    FEED_AUDIENCE_KIND_ACTOR,
    FEED_AUDIENCE_KIND_OWNER,
    FEED_AUDIENCE_KIND_PUBLIC,
  ];

  static final $core.List<FeedAudienceKind?> _byValue =
      $pb.ProtobufEnum.$_initByValueList(values, 2);
  static FeedAudienceKind? valueOf($core.int value) =>
      value < 0 || value >= _byValue.length ? null : _byValue[value];

  const FeedAudienceKind._(super.value, super.name);
}

const $core.bool _omitEnumNames =
    $core.bool.fromEnvironment('protobuf.omit_enum_names');
