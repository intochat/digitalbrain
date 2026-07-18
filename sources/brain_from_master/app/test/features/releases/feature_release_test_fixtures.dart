import 'package:digitalbrain_flutter/features/releases/feature_release_models.dart';
import 'package:digitalbrain_flutter/grpc/ui.pb.dart' as wire;
import 'package:digitalbrain_flutter/grpc/ui.pbenum.dart' as wire_enums;
import 'package:fixnum/fixnum.dart';

String releaseDigest(String character) => List.filled(64, character).join();

String sourceReference(String character) =>
    'sha256:${releaseDigest(character)}';

FeatureReleaseVersion releaseVersion(
  String character, {
  FeatureReleaseSourceKind sourceKind =
      FeatureReleaseSourceKind.runtimeAuthored,
  String? sourceContentCharacter,
}) => FeatureReleaseVersion(
  digest: releaseDigest(character),
  sourceReference: sourceReference(character),
  sourceKind: sourceKind,
  requestedCapabilityIds: const [
    'digitalbrain.integration.email.read',
    'digitalbrain.model.generate',
  ],
  dependencies: const ['Google connection'],
  source: FeatureReleaseSourceSnapshot(
    implementationProjectPath: 'Feature/Feature.csproj',
    scenarioProjectPath: 'Feature.Tests/Feature.Tests.csproj',
    files: [
      const FeatureReleaseSourceFile(
        path: 'Feature/Feature.csproj',
        content: '<Project Sdk="Microsoft.NET.Sdk" />',
      ),
      const FeatureReleaseSourceFile(
        path: 'Feature.Tests/Feature.Tests.csproj',
        content: '<Project Sdk="Microsoft.NET.Sdk" />',
      ),
      FeatureReleaseSourceFile(
        path: 'Feature/Feature.cs',
        content: 'source-${sourceContentCharacter ?? character}',
      ),
    ],
  ),
);

FeatureReleaseDetails releaseDetails({
  String featureId = 'feature-a',
  String activeCharacter = 'a',
  String previousCharacter = 'b',
  bool withPrevious = true,
  bool paused = false,
  String installationId = 'installation-a',
  String operationId = 'operation-a',
  String? conversationId = 'conversation-a',
  String originatingText = 'Research Acme',
  Int64? revision,
  String? activeSourceContentCharacter,
}) => FeatureReleaseDetails(
  featureId: featureId,
  installationId: installationId,
  revision: revision ?? Int64(12),
  originatingRequest: FeatureReleaseOriginatingRequest(
    operationId: operationId,
    conversationId: conversationId,
    text: originatingText,
  ),
  activeVersion: releaseVersion(
    activeCharacter,
    sourceContentCharacter: activeSourceContentCharacter,
  ),
  previousVersion: withPrevious ? releaseVersion(previousCharacter) : null,
  activeGrants: const [
    FeatureReleaseGrant(
      capabilityId: 'digitalbrain.integration.email.read',
      capabilityVersion: 1,
      provider: 'Google',
      connectionId: 'connection-acme',
      constraintsJson:
          '{"allowedToolIds":["digitalbrain.integration.email.read"],"payload":{"mailbox":"inbox","limit":25}}',
      constraintSummary:
          'Only digitalbrain.integration.email.read; input limit must equal 25; input mailbox must equal "inbox"',
    ),
    FeatureReleaseGrant(
      capabilityId: 'digitalbrain.model.generate',
      capabilityVersion: 1,
      provider: null,
      connectionId: null,
      constraintsJson: '{"allowedToolIds":["digitalbrain.model.generate"]}',
      constraintSummary: 'Only digitalbrain.model.generate',
    ),
  ],
  subscriptions: const ['manual', 'schedule:weekday'],
  paused: paused,
  pauseReason: paused ? 'Connection was revoked' : null,
);

wire.FeatureReply wireReleaseDetails({
  String activeCharacter = 'a',
  String previousCharacter = 'b',
  bool withPrevious = true,
  bool paused = false,
  String installationId = 'installation-a',
  Int64? revision,
}) => wire.FeatureReply(
  featureId: 'feature-a',
  originatingRequest: wire.OriginatingRequest(
    operationId: 'operation-a',
    conversationId: 'conversation-a',
    text: 'Research Acme',
  ),
  activeRelease: wireReleaseMetadata(activeCharacter),
  previousRelease: withPrevious ? wireReleaseMetadata(previousCharacter) : null,
  activeGrants: [
    wire.FeatureGrant(
      capabilityId: 'digitalbrain.integration.email.read',
      capabilityVersion: 1,
      provider: 'Google',
      connectionId: 'connection-acme',
      constraintsJson:
          '{"allowedToolIds":["digitalbrain.integration.email.read"],"payload":{"mailbox":"inbox","limit":25}}',
    ),
    wire.FeatureGrant(
      capabilityId: 'digitalbrain.model.generate',
      capabilityVersion: 1,
      constraintsJson: '{"allowedToolIds":["digitalbrain.model.generate"]}',
    ),
  ],
  subscriptions: const ['manual', 'schedule:weekday'],
  rollbackAvailable: withPrevious,
  paused: paused,
  pauseReason: paused ? 'Connection was revoked' : null,
  installationId: installationId,
  revision: revision ?? Int64(12),
);

wire.FeatureRelease wireReleaseMetadata(String character) =>
    wire.FeatureRelease(
      digest: releaseDigest(character),
      sourceKind:
          wire_enums.FeatureSourceKind.FEATURE_SOURCE_KIND_RUNTIME_AUTHORED,
      requestedCapabilityIds: const [
        'digitalbrain.integration.email.read',
        'digitalbrain.model.generate',
      ],
      dependencies: const ['Google connection'],
      sourceReference: sourceReference(character),
    );

wire.FeatureReleaseSourceReply wireReleaseSource(
  String character, {
  String featureId = 'feature-a',
  String installationId = 'installation-a',
  String? releaseDigestOverride,
  String? sourceReferenceOverride,
  String? sourceContentCharacter,
}) => wire.FeatureReleaseSourceReply(
  featureId: featureId,
  installationId: installationId,
  releaseDigest: releaseDigestOverride ?? releaseDigest(character),
  sourceReference: sourceReferenceOverride ?? sourceReference(character),
  source: wireSourceSnapshot(
    character,
    sourceContentCharacter: sourceContentCharacter,
  ),
);

wire.FeatureSourceSnapshot wireSourceSnapshot(
  String character, {
  String? sourceContentCharacter,
}) => wire.FeatureSourceSnapshot(
  implementationProjectPath: 'Feature/Feature.csproj',
  scenarioProjectPath: 'Feature.Tests/Feature.Tests.csproj',
  files: [
    wire.FeatureSourceFile(
      path: 'Feature/Feature.csproj',
      content: '<Project Sdk="Microsoft.NET.Sdk" />',
    ),
    wire.FeatureSourceFile(
      path: 'Feature.Tests/Feature.Tests.csproj',
      content: '<Project Sdk="Microsoft.NET.Sdk" />',
    ),
    wire.FeatureSourceFile(
      path: 'Feature/Feature.cs',
      content: 'source-${sourceContentCharacter ?? character}',
    ),
  ],
);
