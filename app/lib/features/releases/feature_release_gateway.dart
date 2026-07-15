import 'package:fixnum/fixnum.dart';

import '../../core/session/digitalbrain_client.dart';
import '../../grpc/ui.pb.dart' as wire;
import '../../grpc/ui.pbenum.dart' as wire_enums;
import '../../runtime/runtime_errors.dart';
import 'feature_release_models.dart';

abstract interface class FeatureReleaseGateway {
  Future<FeatureReleaseDetails> loadFeature(
    String featureId, {
    String? expectedActiveDigest,
  });

  Future<FeatureReleaseDetails> rollbackFeature({
    required FeatureReleaseDetails current,
    required String idempotencyId,
  });
}

class GrpcFeatureReleaseGateway implements FeatureReleaseGateway {
  const GrpcFeatureReleaseGateway({required FeatureAuthoringClient client})
    : _client = client;

  final FeatureAuthoringClient _client;

  @override
  Future<FeatureReleaseDetails> loadFeature(
    String featureId, {
    String? expectedActiveDigest,
  }) async {
    _requireIdentity(featureId, 'featureId', 128);
    if (expectedActiveDigest != null &&
        !isCanonicalFeatureReleaseDigest(expectedActiveDigest)) {
      throw ArgumentError.value(
        expectedActiveDigest,
        'expectedActiveDigest',
        'Invalid Feature Version digest.',
      );
    }
    final reply = await _client.getFeature(
      wire.GetFeatureRequest(featureId: featureId),
    );
    return _hydrateReply(
      reply,
      expectedFeatureId: featureId,
      expectedActiveDigest: expectedActiveDigest,
    );
  }

  @override
  Future<FeatureReleaseDetails> rollbackFeature({
    required FeatureReleaseDetails current,
    required String idempotencyId,
  }) async {
    _requireIdentity(current.featureId, 'featureId', 128);
    _requireIdentity(idempotencyId, 'idempotencyId', 256);
    final target = current.previousVersion;
    if (target == null) {
      throw const PreconditionException('No previous Version is available.');
    }
    if (current.revision == Int64.MAX_VALUE) {
      throw const PreconditionException('Feature revision cannot advance.');
    }
    final expectedRevision = current.revision + 1;
    final reply = await _client.rollbackFeatureVersion(
      wire.RollbackFeatureVersionRequest(
        featureId: current.featureId,
        expectedActiveDigest: current.activeVersion.digest,
        targetDigest: target.digest,
        idempotencyId: idempotencyId,
        expectedRevision: current.revision,
      ),
    );
    return _hydrateReply(
      reply,
      expectedFeatureId: current.featureId,
      expectedInstallationId: current.installationId,
      expectedRevision: expectedRevision,
    );
  }

  Future<FeatureReleaseDetails> _hydrateReply(
    wire.FeatureReply reply, {
    required String expectedFeatureId,
    String? expectedInstallationId,
    Int64? expectedRevision,
    String? expectedActiveDigest,
  }) async {
    final metadata = _mapReplyMetadata(
      reply,
      expectedFeatureId: expectedFeatureId,
      expectedInstallationId: expectedInstallationId,
      expectedRevision: expectedRevision,
      expectedActiveDigest: expectedActiveDigest,
    );
    final activeVersion = await _hydrateVersion(
      expectedFeatureId,
      metadata.installationId,
      metadata.activeVersion,
    );
    final previousVersion = metadata.previousVersion == null
        ? null
        : await _hydrateVersion(
            expectedFeatureId,
            metadata.installationId,
            metadata.previousVersion!,
          );
    try {
      return FeatureReleaseDetails(
        featureId: expectedFeatureId,
        installationId: metadata.installationId,
        revision: metadata.revision,
        originatingRequest: metadata.originatingRequest,
        activeVersion: activeVersion,
        previousVersion: previousVersion,
        activeGrants: metadata.activeGrants,
        subscriptions: metadata.subscriptions,
        paused: metadata.paused,
        pauseReason: metadata.pauseReason,
      );
    } on ProtocolException {
      rethrow;
    } on Object {
      throw const ProtocolException('Feature response could not be verified.');
    }
  }

  Future<FeatureReleaseVersion> _hydrateVersion(
    String featureId,
    String installationId,
    _FeatureReleaseMetadata metadata,
  ) async {
    final sourceReply = await _client.getFeatureReleaseSource(
      wire.GetFeatureReleaseSourceRequest(
        featureId: featureId,
        installationId: installationId,
        releaseDigest: metadata.digest,
        sourceReference: metadata.sourceReference,
      ),
    );
    try {
      if (!sourceReply.hasFeatureId() ||
          sourceReply.featureId != featureId ||
          !sourceReply.hasInstallationId() ||
          sourceReply.installationId != installationId ||
          !sourceReply.hasReleaseDigest() ||
          sourceReply.releaseDigest != metadata.digest ||
          !sourceReply.hasSourceReference() ||
          sourceReply.sourceReference != metadata.sourceReference ||
          !sourceReply.hasSource()) {
        throw const ProtocolException(
          'Feature release source coordinates are invalid.',
        );
      }
      return FeatureReleaseVersion(
        digest: metadata.digest,
        sourceReference: metadata.sourceReference,
        sourceKind: metadata.sourceKind,
        requestedCapabilityIds: metadata.requestedCapabilityIds,
        dependencies: metadata.dependencies,
        source: _mapSource(sourceReply.source),
      );
    } on ProtocolException {
      rethrow;
    } on Object {
      throw const ProtocolException(
        'Feature release source could not be verified.',
      );
    }
  }
}

_FeatureReplyMetadata _mapReplyMetadata(
  wire.FeatureReply reply, {
  required String expectedFeatureId,
  String? expectedInstallationId,
  Int64? expectedRevision,
  String? expectedActiveDigest,
}) {
  try {
    if (!reply.hasFeatureId() ||
        reply.featureId != expectedFeatureId ||
        !reply.hasInstallationId() ||
        (expectedInstallationId != null &&
            reply.installationId != expectedInstallationId) ||
        reply.revision <= Int64.ZERO ||
        (expectedRevision != null && reply.revision != expectedRevision) ||
        !reply.hasOriginatingRequest() ||
        !reply.hasActiveRelease() ||
        (expectedActiveDigest != null &&
            reply.activeRelease.digest != expectedActiveDigest) ||
        (reply.paused && reply.hasPreviousRelease()) ||
        reply.rollbackAvailable != reply.hasPreviousRelease()) {
      throw const ProtocolException('Feature response is incomplete.');
    }
    final origin = reply.originatingRequest;
    if (!origin.hasOperationId() || !origin.hasText()) {
      throw const ProtocolException('Feature origin is incomplete.');
    }
    if (reply.paused != reply.hasPauseReason() ||
        (reply.paused && reply.pauseReason.trim().isEmpty)) {
      throw const ProtocolException('Paused Feature response is incomplete.');
    }
    return _FeatureReplyMetadata(
      installationId: reply.installationId,
      revision: reply.revision,
      originatingRequest: FeatureReleaseOriginatingRequest(
        operationId: origin.operationId,
        conversationId: origin.hasConversationId()
            ? origin.conversationId
            : null,
        text: origin.text,
      ),
      activeVersion: _mapVersionMetadata(reply.activeRelease),
      previousVersion: reply.hasPreviousRelease()
          ? _mapVersionMetadata(reply.previousRelease)
          : null,
      activeGrants: reply.activeGrants.map(_mapGrant).toList(growable: false),
      subscriptions: List.unmodifiable(reply.subscriptions),
      paused: reply.paused,
      pauseReason: reply.paused ? reply.pauseReason : null,
    );
  } on ProtocolException {
    rethrow;
  } on Object {
    throw const ProtocolException('Feature response could not be verified.');
  }
}

_FeatureReleaseMetadata _mapVersionMetadata(wire.FeatureRelease value) {
  if (!value.hasDigest() ||
      !value.hasSourceKind() ||
      !value.hasSourceReference() ||
      value.requestedCapabilityIds.length > 32) {
    throw const ProtocolException('Feature Version metadata is incomplete.');
  }
  final sourceKind = switch (value.sourceKind) {
    wire_enums.FeatureSourceKind.FEATURE_SOURCE_KIND_REPOSITORY =>
      FeatureReleaseSourceKind.repository,
    wire_enums.FeatureSourceKind.FEATURE_SOURCE_KIND_RUNTIME_AUTHORED =>
      FeatureReleaseSourceKind.runtimeAuthored,
    _ => throw const ProtocolException('Feature source kind is invalid.'),
  };
  return _FeatureReleaseMetadata(
    digest: value.digest,
    sourceReference: value.sourceReference,
    sourceKind: sourceKind,
    requestedCapabilityIds: List.unmodifiable(value.requestedCapabilityIds),
    dependencies: List.unmodifiable(value.dependencies),
  );
}

FeatureReleaseSourceSnapshot _mapSource(wire.FeatureSourceSnapshot source) {
  if (!source.hasImplementationProjectPath() ||
      !source.hasScenarioProjectPath()) {
    throw const ProtocolException('Feature source is incomplete.');
  }
  final files = source.files
      .map((file) {
        if (!file.hasPath()) {
          throw const ProtocolException('Feature source file is incomplete.');
        }
        return FeatureReleaseSourceFile(path: file.path, content: file.content);
      })
      .toList(growable: false);
  return FeatureReleaseSourceSnapshot(
    implementationProjectPath: source.implementationProjectPath,
    scenarioProjectPath: source.scenarioProjectPath,
    files: files,
  );
}

class _FeatureReplyMetadata {
  const _FeatureReplyMetadata({
    required this.installationId,
    required this.revision,
    required this.originatingRequest,
    required this.activeVersion,
    required this.previousVersion,
    required this.activeGrants,
    required this.subscriptions,
    required this.paused,
    required this.pauseReason,
  });

  final String installationId;
  final Int64 revision;
  final FeatureReleaseOriginatingRequest originatingRequest;
  final _FeatureReleaseMetadata activeVersion;
  final _FeatureReleaseMetadata? previousVersion;
  final List<FeatureReleaseGrant> activeGrants;
  final List<String> subscriptions;
  final bool paused;
  final String? pauseReason;
}

class _FeatureReleaseMetadata {
  const _FeatureReleaseMetadata({
    required this.digest,
    required this.sourceReference,
    required this.sourceKind,
    required this.requestedCapabilityIds,
    required this.dependencies,
  });

  final String digest;
  final String sourceReference;
  final FeatureReleaseSourceKind sourceKind;
  final List<String> requestedCapabilityIds;
  final List<String> dependencies;
}

FeatureReleaseGrant _mapGrant(wire.FeatureGrant value) {
  if (!value.hasCapabilityId() ||
      !value.hasCapabilityVersion() ||
      !value.hasConstraintsJson() ||
      value.hasProvider() != value.hasConnectionId()) {
    throw const ProtocolException('Feature grant is incomplete.');
  }
  final constraintSummary = FeatureGrantConstraintPolicy.summarize(
    constraintsJson: value.constraintsJson,
    capabilityId: value.capabilityId,
  );
  if (constraintSummary == null) {
    throw const ProtocolException('Feature grant constraints are invalid.');
  }
  return FeatureReleaseGrant(
    capabilityId: value.capabilityId,
    capabilityVersion: value.capabilityVersion,
    provider: value.hasProvider() ? value.provider : null,
    connectionId: value.hasConnectionId() ? value.connectionId : null,
    constraintsJson: value.constraintsJson,
    constraintSummary: constraintSummary,
  );
}

void _requireIdentity(String value, String name, int maximumLength) {
  if (value.isEmpty ||
      value.length > maximumLength ||
      value.trim() != value ||
      value.runes.any(
        (character) => character < 32 || (character >= 127 && character <= 159),
      )) {
    throw ArgumentError.value(value, name, 'Invalid identity.');
  }
}
