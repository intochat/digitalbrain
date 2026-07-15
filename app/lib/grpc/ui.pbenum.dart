import 'dart:core' as $core;

import 'package:protobuf/protobuf.dart' as $pb;

class FeedAudienceKind extends $pb.ProtobufEnum {
  static const FeedAudienceKind FEED_AUDIENCE_KIND_ACTOR = FeedAudienceKind._(
    0,
    _omitEnumNames ? '' : 'FEED_AUDIENCE_KIND_ACTOR',
  );
  static const FeedAudienceKind FEED_AUDIENCE_KIND_OWNER = FeedAudienceKind._(
    1,
    _omitEnumNames ? '' : 'FEED_AUDIENCE_KIND_OWNER',
  );
  static const FeedAudienceKind FEED_AUDIENCE_KIND_PUBLIC = FeedAudienceKind._(
    2,
    _omitEnumNames ? '' : 'FEED_AUDIENCE_KIND_PUBLIC',
  );

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

class FeatureDraftStatus extends $pb.ProtobufEnum {
  static const FeatureDraftStatus FEATURE_DRAFT_STATUS_UNSPECIFIED =
      FeatureDraftStatus._(
        0,
        _omitEnumNames ? '' : 'FEATURE_DRAFT_STATUS_UNSPECIFIED',
      );
  static const FeatureDraftStatus FEATURE_DRAFT_STATUS_DRAFT =
      FeatureDraftStatus._(
        1,
        _omitEnumNames ? '' : 'FEATURE_DRAFT_STATUS_DRAFT',
      );
  static const FeatureDraftStatus FEATURE_DRAFT_STATUS_INSTALLED =
      FeatureDraftStatus._(
        2,
        _omitEnumNames ? '' : 'FEATURE_DRAFT_STATUS_INSTALLED',
      );

  static const $core.List<FeatureDraftStatus> values = <FeatureDraftStatus>[
    FEATURE_DRAFT_STATUS_UNSPECIFIED,
    FEATURE_DRAFT_STATUS_DRAFT,
    FEATURE_DRAFT_STATUS_INSTALLED,
  ];

  static final $core.List<FeatureDraftStatus?> _byValue =
      $pb.ProtobufEnum.$_initByValueList(values, 2);
  static FeatureDraftStatus? valueOf($core.int value) =>
      value < 0 || value >= _byValue.length ? null : _byValue[value];

  const FeatureDraftStatus._(super.value, super.name);
}

class FeatureSourceKind extends $pb.ProtobufEnum {
  static const FeatureSourceKind FEATURE_SOURCE_KIND_UNSPECIFIED =
      FeatureSourceKind._(
        0,
        _omitEnumNames ? '' : 'FEATURE_SOURCE_KIND_UNSPECIFIED',
      );
  static const FeatureSourceKind FEATURE_SOURCE_KIND_REPOSITORY =
      FeatureSourceKind._(
        1,
        _omitEnumNames ? '' : 'FEATURE_SOURCE_KIND_REPOSITORY',
      );
  static const FeatureSourceKind FEATURE_SOURCE_KIND_RUNTIME_AUTHORED =
      FeatureSourceKind._(
        2,
        _omitEnumNames ? '' : 'FEATURE_SOURCE_KIND_RUNTIME_AUTHORED',
      );

  static const $core.List<FeatureSourceKind> values = <FeatureSourceKind>[
    FEATURE_SOURCE_KIND_UNSPECIFIED,
    FEATURE_SOURCE_KIND_REPOSITORY,
    FEATURE_SOURCE_KIND_RUNTIME_AUTHORED,
  ];

  static final $core.List<FeatureSourceKind?> _byValue =
      $pb.ProtobufEnum.$_initByValueList(values, 2);
  static FeatureSourceKind? valueOf($core.int value) =>
      value < 0 || value >= _byValue.length ? null : _byValue[value];

  const FeatureSourceKind._(super.value, super.name);
}

class FeatureScenarioOutcome extends $pb.ProtobufEnum {
  static const FeatureScenarioOutcome FEATURE_SCENARIO_OUTCOME_UNSPECIFIED =
      FeatureScenarioOutcome._(
        0,
        _omitEnumNames ? '' : 'FEATURE_SCENARIO_OUTCOME_UNSPECIFIED',
      );
  static const FeatureScenarioOutcome FEATURE_SCENARIO_OUTCOME_PASSED =
      FeatureScenarioOutcome._(
        1,
        _omitEnumNames ? '' : 'FEATURE_SCENARIO_OUTCOME_PASSED',
      );
  static const FeatureScenarioOutcome FEATURE_SCENARIO_OUTCOME_FAILED =
      FeatureScenarioOutcome._(
        2,
        _omitEnumNames ? '' : 'FEATURE_SCENARIO_OUTCOME_FAILED',
      );
  static const FeatureScenarioOutcome FEATURE_SCENARIO_OUTCOME_SKIPPED =
      FeatureScenarioOutcome._(
        3,
        _omitEnumNames ? '' : 'FEATURE_SCENARIO_OUTCOME_SKIPPED',
      );

  static const $core.List<FeatureScenarioOutcome> values =
      <FeatureScenarioOutcome>[
        FEATURE_SCENARIO_OUTCOME_UNSPECIFIED,
        FEATURE_SCENARIO_OUTCOME_PASSED,
        FEATURE_SCENARIO_OUTCOME_FAILED,
        FEATURE_SCENARIO_OUTCOME_SKIPPED,
      ];

  static final $core.List<FeatureScenarioOutcome?> _byValue =
      $pb.ProtobufEnum.$_initByValueList(values, 3);
  static FeatureScenarioOutcome? valueOf($core.int value) =>
      value < 0 || value >= _byValue.length ? null : _byValue[value];

  const FeatureScenarioOutcome._(super.value, super.name);
}

class FeatureRunOrigin extends $pb.ProtobufEnum {
  static const FeatureRunOrigin FEATURE_RUN_ORIGIN_UNSPECIFIED =
      FeatureRunOrigin._(
        0,
        _omitEnumNames ? '' : 'FEATURE_RUN_ORIGIN_UNSPECIFIED',
      );
  static const FeatureRunOrigin FEATURE_RUN_ORIGIN_CHAT = FeatureRunOrigin._(
    1,
    _omitEnumNames ? '' : 'FEATURE_RUN_ORIGIN_CHAT',
  );
  static const FeatureRunOrigin FEATURE_RUN_ORIGIN_DIRECT = FeatureRunOrigin._(
    2,
    _omitEnumNames ? '' : 'FEATURE_RUN_ORIGIN_DIRECT',
  );
  static const FeatureRunOrigin FEATURE_RUN_ORIGIN_SCHEDULE =
      FeatureRunOrigin._(
        3,
        _omitEnumNames ? '' : 'FEATURE_RUN_ORIGIN_SCHEDULE',
      );
  static const FeatureRunOrigin FEATURE_RUN_ORIGIN_EVENT = FeatureRunOrigin._(
    4,
    _omitEnumNames ? '' : 'FEATURE_RUN_ORIGIN_EVENT',
  );

  static const $core.List<FeatureRunOrigin> values = <FeatureRunOrigin>[
    FEATURE_RUN_ORIGIN_UNSPECIFIED,
    FEATURE_RUN_ORIGIN_CHAT,
    FEATURE_RUN_ORIGIN_DIRECT,
    FEATURE_RUN_ORIGIN_SCHEDULE,
    FEATURE_RUN_ORIGIN_EVENT,
  ];

  static final $core.List<FeatureRunOrigin?> _byValue =
      $pb.ProtobufEnum.$_initByValueList(values, 4);
  static FeatureRunOrigin? valueOf($core.int value) =>
      value < 0 || value >= _byValue.length ? null : _byValue[value];

  const FeatureRunOrigin._(super.value, super.name);
}

class FeatureRunStatus extends $pb.ProtobufEnum {
  static const FeatureRunStatus FEATURE_RUN_STATUS_UNSPECIFIED =
      FeatureRunStatus._(
        0,
        _omitEnumNames ? '' : 'FEATURE_RUN_STATUS_UNSPECIFIED',
      );
  static const FeatureRunStatus FEATURE_RUN_STATUS_QUEUED = FeatureRunStatus._(
    1,
    _omitEnumNames ? '' : 'FEATURE_RUN_STATUS_QUEUED',
  );
  static const FeatureRunStatus FEATURE_RUN_STATUS_RUNNING = FeatureRunStatus._(
    2,
    _omitEnumNames ? '' : 'FEATURE_RUN_STATUS_RUNNING',
  );
  static const FeatureRunStatus FEATURE_RUN_STATUS_WAITING_FOR_APPROVAL =
      FeatureRunStatus._(
        3,
        _omitEnumNames ? '' : 'FEATURE_RUN_STATUS_WAITING_FOR_APPROVAL',
      );
  static const FeatureRunStatus FEATURE_RUN_STATUS_COMPLETED =
      FeatureRunStatus._(
        4,
        _omitEnumNames ? '' : 'FEATURE_RUN_STATUS_COMPLETED',
      );
  static const FeatureRunStatus FEATURE_RUN_STATUS_FAILED = FeatureRunStatus._(
    5,
    _omitEnumNames ? '' : 'FEATURE_RUN_STATUS_FAILED',
  );
  static const FeatureRunStatus FEATURE_RUN_STATUS_PARKED = FeatureRunStatus._(
    6,
    _omitEnumNames ? '' : 'FEATURE_RUN_STATUS_PARKED',
  );

  static const $core.List<FeatureRunStatus> values = <FeatureRunStatus>[
    FEATURE_RUN_STATUS_UNSPECIFIED,
    FEATURE_RUN_STATUS_QUEUED,
    FEATURE_RUN_STATUS_RUNNING,
    FEATURE_RUN_STATUS_WAITING_FOR_APPROVAL,
    FEATURE_RUN_STATUS_COMPLETED,
    FEATURE_RUN_STATUS_FAILED,
    FEATURE_RUN_STATUS_PARKED,
  ];

  static final $core.List<FeatureRunStatus?> _byValue =
      $pb.ProtobufEnum.$_initByValueList(values, 6);
  static FeatureRunStatus? valueOf($core.int value) =>
      value < 0 || value >= _byValue.length ? null : _byValue[value];

  const FeatureRunStatus._(super.value, super.name);
}

class FeatureRunAuthorityState extends $pb.ProtobufEnum {
  static const FeatureRunAuthorityState
  FEATURE_RUN_AUTHORITY_STATE_UNSPECIFIED = FeatureRunAuthorityState._(
    0,
    _omitEnumNames ? '' : 'FEATURE_RUN_AUTHORITY_STATE_UNSPECIFIED',
  );
  static const FeatureRunAuthorityState FEATURE_RUN_AUTHORITY_STATE_AUTHORIZED =
      FeatureRunAuthorityState._(
        1,
        _omitEnumNames ? '' : 'FEATURE_RUN_AUTHORITY_STATE_AUTHORIZED',
      );
  static const FeatureRunAuthorityState
  FEATURE_RUN_AUTHORITY_STATE_WAITING_FOR_APPROVAL = FeatureRunAuthorityState._(
    2,
    _omitEnumNames ? '' : 'FEATURE_RUN_AUTHORITY_STATE_WAITING_FOR_APPROVAL',
  );
  static const FeatureRunAuthorityState FEATURE_RUN_AUTHORITY_STATE_PAUSED =
      FeatureRunAuthorityState._(
        3,
        _omitEnumNames ? '' : 'FEATURE_RUN_AUTHORITY_STATE_PAUSED',
      );

  static const $core.List<FeatureRunAuthorityState> values =
      <FeatureRunAuthorityState>[
        FEATURE_RUN_AUTHORITY_STATE_UNSPECIFIED,
        FEATURE_RUN_AUTHORITY_STATE_AUTHORIZED,
        FEATURE_RUN_AUTHORITY_STATE_WAITING_FOR_APPROVAL,
        FEATURE_RUN_AUTHORITY_STATE_PAUSED,
      ];

  static final $core.List<FeatureRunAuthorityState?> _byValue =
      $pb.ProtobufEnum.$_initByValueList(values, 3);
  static FeatureRunAuthorityState? valueOf($core.int value) =>
      value < 0 || value >= _byValue.length ? null : _byValue[value];

  const FeatureRunAuthorityState._(super.value, super.name);
}

const $core.bool _omitEnumNames = $core.bool.fromEnvironment(
  'protobuf.omit_enum_names',
);
