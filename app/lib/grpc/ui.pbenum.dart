


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
