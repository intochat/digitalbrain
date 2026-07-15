import 'package:fixnum/fixnum.dart';

import '../../core/session/digitalbrain_client.dart';
import '../../grpc/ui.pb.dart' as wire;
import '../../runtime/runtime_errors.dart';
import '../shared/feature_grant_constraint_policy.dart';
import 'feature_studio_models.dart';
import 'feature_studio_validation.dart';

abstract interface class FeatureStudioGateway {
  Future<FeatureStudioDraft> loadDraft(String draftId);

  Future<FeatureStudioDraft> resetPendingInstall({
    required String draftId,
    required String idempotencyId,
  });

  Future<FeatureStudioDraft> reviseBehavior({
    required String draftId,
    required Int64 expectedRevision,
    required String idempotencyId,
    required FeatureStudioBehavior behavior,
    required FeatureStudioSource expectedSource,
  });

  Future<FeatureStudioDraft> reviseSource({
    required String draftId,
    required Int64 expectedRevision,
    required String idempotencyId,
    required FeatureStudioSource source,
    required FeatureStudioBehavior expectedBehavior,
  });

  Future<FeatureStudioDraft> acceptSuggestedChange({
    required String draftId,
    required Int64 expectedRevision,
    required String idempotencyId,
    required FeatureStudioSuggestion suggestion,
  });

  Future<FeatureStudioDraft> rejectSuggestedChange({
    required String draftId,
    required Int64 expectedRevision,
    required String idempotencyId,
    required FeatureStudioSuggestion suggestion,
    required FeatureStudioBehavior expectedBehavior,
    required FeatureStudioSource expectedSource,
    required FeatureStudioVerification? expectedVerification,
  });

  Future<FeatureStudioSuggestion> suggestChange({
    required String draftId,
    required Int64 expectedRevision,
    required String guidance,
    required String suggestionId,
  });

  Future<FeatureStudioVerificationResult> verifyDraft({
    required String draftId,
    required Int64 expectedRevision,
    required String idempotencyId,
    required FeatureStudioBehavior expectedBehavior,
    required FeatureStudioSource expectedSource,
  });

  Future<FeatureStudioAccessReview> reviewAccess({
    required String draftId,
    required Int64 expectedRevision,
    required FeatureStudioDraft expectedDraft,
    required String installationId,
    required FeatureStudioVersion version,
    required FeatureStudioVerification expectedVerification,
    required FeatureStudioBehavior expectedBehavior,
    required FeatureStudioSource expectedSource,
  });

  Future<FeatureStudioInstallSuccess> installVersion({
    required FeatureStudioAccessReview review,
    required Int64 expectedRevision,
    required String decisionId,
    required String idempotencyId,
    required FeatureStudioBehavior expectedBehavior,
    required FeatureStudioSource expectedSource,
  });
}

class GrpcFeatureStudioGateway implements FeatureStudioGateway {
  const GrpcFeatureStudioGateway({required FeatureAuthoringClient client})
    : _client = client;

  final FeatureAuthoringClient _client;

  @override
  Future<FeatureStudioDraft> loadDraft(String draftId) async {
    _validateIdentity(draftId, 'draftId', maximumLength: 128);
    final reply = await _client.getFeatureDraft(
      wire.GetFeatureDraftRequest(draftId: draftId),
    );
    return _mapLoadedDraftReply(reply, draftId);
  }

  @override
  Future<FeatureStudioDraft> resetPendingInstall({
    required String draftId,
    required String idempotencyId,
  }) async {
    _validateIdentity(draftId, 'draftId', maximumLength: 128);
    _validateIdentity(idempotencyId, 'idempotencyId', maximumLength: 256);
    final reply = await _client.resetFeatureDraftInstallation(
      wire.ResetFeatureDraftInstallationRequest(
        draftId: draftId,
        idempotencyId: idempotencyId,
      ),
    );
    if (!reply.hasDraft() ||
        reply.hasRecovery() ||
        reply.draft.hasVerification()) {
      throw const ProtocolException(
        'Pending install reset response retained governed state.',
      );
    }
    final draft = _mapDraftReply(reply, draftId);
    _requireDraftStatus(draft);
    _requireNoVerification(draft);
    if (draft.installationRecovery != null || draft.revision <= Int64.ZERO) {
      throw const ProtocolException(
        'Pending install reset response is invalid.',
      );
    }
    return draft;
  }

  @override
  Future<FeatureStudioDraft> reviseBehavior({
    required String draftId,
    required Int64 expectedRevision,
    required String idempotencyId,
    required FeatureStudioBehavior behavior,
    required FeatureStudioSource expectedSource,
  }) async {
    _validateMutationIdentity(draftId, expectedRevision, idempotencyId);
    _validateOutgoingBehavior(behavior);
    final reply = await _client.reviseFeatureDraft(
      wire.ReviseFeatureDraftRequest(
        draftId: draftId,
        expectedRevision: expectedRevision,
        idempotencyId: idempotencyId,
        reviseBehavior: wire.ReviseFeatureBehaviorInput(
          behavior: _toWireBehavior(behavior),
        ),
      ),
    );
    final draft = _mapRevisionReply(
      reply: reply,
      draftId: draftId,
      requiredRevision: expectedRevision + Int64.ONE,
    );
    _requireBehaviorEcho(draft, behavior);
    _requireSourceEcho(draft, expectedSource);
    _requireDraftStatus(draft);
    _requireNoVerification(draft);
    return draft;
  }

  @override
  Future<FeatureStudioDraft> reviseSource({
    required String draftId,
    required Int64 expectedRevision,
    required String idempotencyId,
    required FeatureStudioSource source,
    required FeatureStudioBehavior expectedBehavior,
  }) async {
    _validateMutationIdentity(draftId, expectedRevision, idempotencyId);
    _validateOutgoingSource(source);
    final reply = await _client.reviseFeatureDraft(
      wire.ReviseFeatureDraftRequest(
        draftId: draftId,
        expectedRevision: expectedRevision,
        idempotencyId: idempotencyId,
        reviseSource: wire.ReviseFeatureSourceInput(
          source: _toWireSource(source),
        ),
      ),
    );
    final draft = _mapRevisionReply(
      reply: reply,
      draftId: draftId,
      requiredRevision: expectedRevision + Int64.ONE,
    );
    _requireBehaviorEcho(draft, expectedBehavior);
    _requireSourceEcho(draft, source);
    _requireDraftStatus(draft);
    _requireNoVerification(draft);
    return draft;
  }

  @override
  Future<FeatureStudioDraft> acceptSuggestedChange({
    required String draftId,
    required Int64 expectedRevision,
    required String idempotencyId,
    required FeatureStudioSuggestion suggestion,
  }) async {
    _validateMutationIdentity(draftId, expectedRevision, idempotencyId);
    _validateSuggestionIdentity(draftId, expectedRevision, suggestion);
    _validateOutgoingBehavior(suggestion.replacementBehavior);
    _validateOutgoingSource(suggestion.replacementSource);
    final reply = await _client.reviseFeatureDraft(
      wire.ReviseFeatureDraftRequest(
        draftId: draftId,
        expectedRevision: expectedRevision,
        idempotencyId: idempotencyId,
        acceptSuggestedChange: wire.AcceptSuggestedChangeInput(
          patch: _toWirePatch(suggestion),
        ),
      ),
    );
    final draft = _mapRevisionReply(
      reply: reply,
      draftId: draftId,
      requiredRevision: expectedRevision + Int64.ONE,
    );
    _requireBehaviorEcho(draft, suggestion.replacementBehavior);
    _requireSourceEcho(draft, suggestion.replacementSource);
    _requireDraftStatus(draft);
    _requireNoVerification(draft);
    return draft;
  }

  @override
  Future<FeatureStudioDraft> rejectSuggestedChange({
    required String draftId,
    required Int64 expectedRevision,
    required String idempotencyId,
    required FeatureStudioSuggestion suggestion,
    required FeatureStudioBehavior expectedBehavior,
    required FeatureStudioSource expectedSource,
    required FeatureStudioVerification? expectedVerification,
  }) async {
    _validateMutationIdentity(draftId, expectedRevision, idempotencyId);
    _validateSuggestionIdentity(draftId, expectedRevision, suggestion);
    final reply = await _client.reviseFeatureDraft(
      wire.ReviseFeatureDraftRequest(
        draftId: draftId,
        expectedRevision: expectedRevision,
        idempotencyId: idempotencyId,
        rejectSuggestedChange: wire.RejectSuggestedChangeInput(
          patchId: suggestion.patchId,
          baseRevision: suggestion.baseRevision,
        ),
      ),
    );
    final draft = _mapRevisionReply(
      reply: reply,
      draftId: draftId,
      requiredRevision: expectedRevision,
    );
    _requireBehaviorEcho(draft, expectedBehavior);
    _requireSourceEcho(draft, expectedSource);
    _requireDraftStatus(draft);
    _requireVerificationEcho(draft.verification, expectedVerification);
    return draft;
  }

  @override
  Future<FeatureStudioSuggestion> suggestChange({
    required String draftId,
    required Int64 expectedRevision,
    required String guidance,
    required String suggestionId,
  }) async {
    _validateIdentity(draftId, 'draftId', maximumLength: 128);
    _validateRevision(expectedRevision);
    _validateText(guidance, 'guidance', maximumLength: 4096);
    _validateIdentity(suggestionId, 'suggestionId', maximumLength: 256);
    final reply = await _client.suggestFeatureChange(
      wire.SuggestFeatureChangeRequest(
        draftId: draftId,
        expectedRevision: expectedRevision,
        guidance: guidance,
        suggestionId: suggestionId,
      ),
    );
    if (!reply.hasPatch()) {
      throw const ProtocolException('Suggestion response is incomplete.');
    }
    return _mapSuggestion(
      reply.patch,
      draftId: draftId,
      expectedRevision: expectedRevision,
    );
  }

  @override
  Future<FeatureStudioVerificationResult> verifyDraft({
    required String draftId,
    required Int64 expectedRevision,
    required String idempotencyId,
    required FeatureStudioBehavior expectedBehavior,
    required FeatureStudioSource expectedSource,
  }) async {
    _validateMutationIdentity(draftId, expectedRevision, idempotencyId);
    final reply = await _client.verifyFeatureDraft(
      wire.VerifyFeatureDraftRequest(
        draftId: draftId,
        expectedRevision: expectedRevision,
        idempotencyId: idempotencyId,
      ),
    );
    if (!reply.hasDraft() || !reply.hasVerification()) {
      throw const ProtocolException('Verification response is incomplete.');
    }
    final verification = _mapVerification(reply.verification);
    final draft = _mapDraft(
      reply.draft,
      expectedDraftId: draftId,
      trustedSource: expectedSource,
      authoritativeVerification: verification,
    );
    _requireBehaviorEcho(draft, expectedBehavior);
    _requireSourceEcho(draft, expectedSource);
    _requireDraftStatus(draft);
    if (verification.verifiedAt.isBefore(draft.createdAt) ||
        verification.isPassing &&
            verification.verifiedAt.isAfter(draft.updatedAt)) {
      throw const ProtocolException(
        'Verification response has an invalid attempt time.',
      );
    }
    if (!reply.hasRelease()) {
      if (draft.revision != expectedRevision ||
          verification.isPassing ||
          verification.releaseDigest != null ||
          draft.verification != null) {
        throw const ProtocolException(
          'Failed verification response is inconsistent.',
        );
      }
      return FeatureStudioVerificationResult(
        draft: draft,
        verification: verification,
        version: null,
      );
    }
    final version = _mapVersionWithTrustedSource(reply.release, expectedSource);
    if (draft.revision != expectedRevision + Int64.ONE ||
        !verification.isPassing ||
        verification.releaseDigest != version.digest ||
        verification.sourceReference != version.sourceReference ||
        draft.verification == null ||
        !_sameVerification(draft.verification, verification)) {
      throw const ProtocolException(
        'Passing verification response is inconsistent.',
      );
    }
    return FeatureStudioVerificationResult(
      draft: draft,
      verification: verification,
      version: version,
    );
  }

  @override
  Future<FeatureStudioAccessReview> reviewAccess({
    required String draftId,
    required Int64 expectedRevision,
    required FeatureStudioDraft expectedDraft,
    required String installationId,
    required FeatureStudioVersion version,
    required FeatureStudioVerification expectedVerification,
    required FeatureStudioBehavior expectedBehavior,
    required FeatureStudioSource expectedSource,
  }) async {
    _validateIdentity(draftId, 'draftId', maximumLength: 128);
    _validateRevision(expectedRevision);
    _validateIdentity(installationId, 'installationId', maximumLength: 256);
    _validateVersion(version, requireSource: true);
    if (!expectedVerification.isPassing ||
        expectedVerification.releaseDigest != version.digest) {
      throw ArgumentError('The expected Verification is inconsistent.');
    }
    final reply = await _client.reviewFeatureAccess(
      wire.ReviewFeatureAccessRequest(
        draftId: draftId,
        expectedRevision: expectedRevision,
        installationId: installationId,
        releaseDigest: version.digest,
      ),
    );
    if (!reply.hasDraft() ||
        !reply.hasRelease() ||
        !reply.hasInstallationId() ||
        reply.installationId != installationId) {
      throw const ProtocolException('Access review response is incomplete.');
    }
    final draft = _mapDraft(
      reply.draft,
      expectedDraftId: draftId,
      trustedSource: expectedSource,
      authoritativeVerification: expectedVerification,
    );
    _requireBehaviorEcho(draft, expectedBehavior);
    _requireSourceEcho(draft, expectedSource);
    _requireImmutableDraftEcho(draft, expectedDraft);
    _requireDraftStatus(draft);
    final reviewedVersion = _mapVersionWithTrustedSource(
      reply.release,
      expectedSource,
    );
    if (draft.revision != expectedRevision ||
        draft.verification == null ||
        !draft.verification!.isPassing ||
        draft.verification!.releaseDigest != version.digest ||
        !_sameVersion(reviewedVersion, version)) {
      throw const ProtocolException('Access review response is inconsistent.');
    }
    final grants = _mapGrants(
      reply.grants,
      requiredCapabilityIds: version.requestedCapabilityIds,
    );
    final subscriptions = _mapSubscriptions(reply.subscriptions);
    final previousVersion = reply.hasPreviousRelease()
        ? await _hydrateVersionSource(
            draftId: draftId,
            installationId: installationId,
            release: reply.previousRelease,
          )
        : null;
    if (previousVersion?.digest == version.digest) {
      throw const ProtocolException('Previous Version is invalid.');
    }
    return FeatureStudioAccessReview(
      draft: draft,
      version: reviewedVersion,
      installationId: reply.installationId,
      grants: grants,
      subscriptions: subscriptions,
      previousVersion: previousVersion,
    );
  }

  @override
  Future<FeatureStudioInstallSuccess> installVersion({
    required FeatureStudioAccessReview review,
    required Int64 expectedRevision,
    required String decisionId,
    required String idempotencyId,
    required FeatureStudioBehavior expectedBehavior,
    required FeatureStudioSource expectedSource,
  }) async {
    _validateMutationIdentity(
      review.draft.draftId,
      expectedRevision,
      idempotencyId,
    );
    _validateIdentity(decisionId, 'decisionId', maximumLength: 256);
    _validateIdentity(
      review.installationId,
      'installationId',
      maximumLength: 256,
    );
    _validateVersion(review.version, requireSource: true);
    final requiredCapabilities = review.version.requestedCapabilityIds;
    _validateGrants(review.grants, requiredCapabilityIds: requiredCapabilities);
    _validateSubscriptions(review.subscriptions);
    final reply = await _client.installFeatureVersion(
      wire.InstallFeatureVersionRequest(
        draftId: review.draft.draftId,
        expectedRevision: expectedRevision,
        installationId: review.installationId,
        releaseDigest: review.version.digest,
        grants: review.grants.map(_toWireGrant),
        subscriptions: review.subscriptions,
        decisionId: decisionId,
        idempotencyId: idempotencyId,
      ),
    );
    if (!reply.hasDraft() ||
        !reply.hasRelease() ||
        !reply.hasInstallationId() ||
        reply.installationId != review.installationId ||
        reply.paused ||
        reply.hasPauseReason()) {
      throw const ProtocolException('Installation response is incomplete.');
    }
    final draft = _mapDraft(
      reply.draft,
      expectedDraftId: review.draft.draftId,
      trustedSource: expectedSource,
      authoritativeVerification: review.draft.verification,
    );
    _requireBehaviorEcho(draft, expectedBehavior);
    _requireSourceEcho(draft, expectedSource);
    _requireImmutableDraftEcho(draft, review.draft);
    final version = _mapVersionWithTrustedSource(reply.release, expectedSource);
    final grants = _mapGrants(
      reply.activeGrants,
      requiredCapabilityIds: requiredCapabilities,
    );
    final subscriptions = _mapSubscriptions(reply.subscriptions);
    if (draft.status != FeatureStudioDraftStatus.installed ||
        draft.revision != expectedRevision + Int64.ONE ||
        draft.verification == null ||
        !draft.verification!.isPassing ||
        draft.verification!.releaseDigest != review.version.digest ||
        draft.installationId != review.installationId ||
        !_sameVersion(version, review.version) ||
        !_sameGrants(grants, review.grants) ||
        !_sameStrings(subscriptions, review.subscriptions) ||
        reply.rollbackAvailable != (review.previousVersion != null)) {
      throw const ProtocolException('Installation response is inconsistent.');
    }
    return FeatureStudioInstallSuccess(
      draft: draft,
      version: version,
      installationId: reply.installationId,
      activeGrants: grants,
      subscriptions: subscriptions,
      rollbackAvailable: reply.rollbackAvailable,
    );
  }

  Future<FeatureStudioDraft> _mapLoadedDraftReply(
    wire.FeatureDraftReply reply,
    String expectedDraftId,
  ) async {
    final preliminaryDraft = _mapDraftReply(reply, expectedDraftId);
    if (!reply.hasRecovery()) {
      if (preliminaryDraft.status == FeatureStudioDraftStatus.installed) {
        throw const ProtocolException(
          'Installed Draft recovery is incomplete.',
        );
      }
      return preliminaryDraft;
    }
    try {
      final value = reply.recovery;
      if (!value.hasVerification() ||
          !value.hasRelease() ||
          !value.hasInstallationId() ||
          !_isCanonicalText(value.installationId, 256)) {
        throw const ProtocolException(
          'Installation recovery response is incomplete.',
        );
      }
      final verification = _mapVerification(value.verification);
      final historicalInstalledRecovery =
          value.installed &&
          preliminaryDraft.status == FeatureStudioDraftStatus.installed &&
          reply.draft.hasVerification() &&
          reply.draft.verification.hasReleaseDigest() &&
          reply.draft.verification.releaseDigest != verification.releaseDigest;
      final draft = _mapDraft(
        reply.draft,
        expectedDraftId: expectedDraftId,
        trustedSource: preliminaryDraft.source,
        authoritativeVerification: historicalInstalledRecovery
            ? null
            : verification,
        retainPersistedVerificationSummary: historicalInstalledRecovery,
      );
      if (verification.releaseDigest != value.release.digest ||
          verification.sourceReference != value.release.sourceReference) {
        throw const ProtocolException(
          'Installation recovery Version is inconsistent.',
        );
      }
      final version = historicalInstalledRecovery
          ? await _hydrateVersionSource(
              draftId: expectedDraftId,
              installationId: value.installationId,
              release: value.release,
            )
          : _mapVersionWithTrustedSource(value.release, draft.source);
      final grants = _mapGrants(
        value.grants,
        requiredCapabilityIds: version.requestedCapabilityIds,
      );
      final subscriptions = _mapSubscriptions(value.subscriptions);
      final previousVersion = value.hasPreviousRelease()
          ? await _hydrateVersionSource(
              draftId: expectedDraftId,
              installationId: value.installationId,
              release: value.previousRelease,
            )
          : null;
      final recovery = FeatureStudioInstallationRecovery(
        installed: value.installed,
        verification: verification,
        version: version,
        installationId: value.installationId,
        grants: grants,
        subscriptions: subscriptions,
        previousVersion: previousVersion,
        decisionId: value.hasDecisionId() ? value.decisionId : null,
        idempotencyId: value.hasIdempotencyId() ? value.idempotencyId : null,
        rollbackAvailable: value.rollbackAvailable,
        paused: value.paused,
        pauseReason: value.hasPauseReason() ? value.pauseReason : null,
      );
      return FeatureStudioDraft(
        draftId: draft.draftId,
        originatingRequest: draft.originatingRequest,
        goal: draft.goal,
        status: draft.status,
        installationId: draft.installationId,
        behavior: draft.behavior,
        source: draft.source,
        verification: draft.verification,
        revision: draft.revision,
        createdAt: draft.createdAt,
        updatedAt: draft.updatedAt,
        installationRecovery: recovery,
      );
    } on TransportException {
      rethrow;
    } on Object {
      throw const ProtocolException(
        'Installation recovery response could not be verified.',
      );
    }
  }

  Future<FeatureStudioVersion> _hydrateVersionSource({
    required String draftId,
    required String installationId,
    required wire.FeatureRelease release,
  }) async {
    final metadata = _mapVersion(release, requireSource: false);
    final reply = await _client.getFeatureReleaseSource(
      wire.GetFeatureReleaseSourceRequest(
        featureId: draftId,
        installationId: installationId,
        releaseDigest: metadata.digest,
        sourceReference: metadata.sourceReference,
      ),
    );
    if (!reply.hasFeatureId() ||
        !reply.hasInstallationId() ||
        !reply.hasReleaseDigest() ||
        !reply.hasSourceReference() ||
        !reply.hasSource() ||
        reply.featureId != draftId ||
        reply.installationId != installationId ||
        reply.releaseDigest != metadata.digest ||
        reply.sourceReference != metadata.sourceReference) {
      throw const ProtocolException('Version source response is inconsistent.');
    }
    final source = _mapSource(reply.source);
    if (validateFeatureStudioSource(source).isNotEmpty ||
        metadata.source != null && !_sameSource(metadata.source!, source)) {
      throw const ProtocolException('Version source is invalid.');
    }
    final version = FeatureStudioVersion(
      digest: metadata.digest,
      sourceReference: metadata.sourceReference,
      requestedCapabilityIds: metadata.requestedCapabilityIds,
      dependencies: metadata.dependencies,
      source: source,
    );
    _validateVersion(version, requireSource: true);
    return version;
  }
}

FeatureStudioDraft _mapDraftReply(
  wire.FeatureDraftReply reply,
  String expectedDraftId,
) {
  if (!reply.hasDraft()) {
    throw const ProtocolException('Draft response is incomplete.');
  }
  return _mapDraft(reply.draft, expectedDraftId: expectedDraftId);
}

FeatureStudioDraft _mapRevisionReply({
  required wire.FeatureDraftReply reply,
  required String draftId,
  required Int64 requiredRevision,
}) {
  if (reply.hasRecovery()) {
    throw const ProtocolException(
      'Draft revision response retained installation recovery.',
    );
  }
  final draft = _mapDraftReply(reply, draftId);
  if (draft.revision != requiredRevision) {
    throw const ProtocolException('Draft response has an invalid revision.');
  }
  return draft;
}

FeatureStudioDraft _mapDraft(
  wire.FeatureDraft draft, {
  required String expectedDraftId,
  FeatureStudioSource? trustedSource,
  FeatureStudioVerification? authoritativeVerification,
  bool retainPersistedVerificationSummary = false,
}) {
  if (!draft.hasDraftId() ||
      draft.draftId != expectedDraftId ||
      !draft.hasOriginatingRequest() ||
      !draft.hasBehavior() ||
      !draft.hasSource() && trustedSource == null ||
      !draft.hasStatus() ||
      !draft.hasCreatedAtUnixMs() ||
      !draft.hasUpdatedAtUnixMs()) {
    throw const ProtocolException('Draft response is incomplete.');
  }
  final origin = draft.originatingRequest;
  if (!_isCanonicalText(draft.draftId, 128) ||
      !origin.hasOperationId() ||
      !_isCanonicalText(origin.operationId, 256) ||
      (origin.hasConversationId() &&
          !_isCanonicalText(origin.conversationId, 256)) ||
      !origin.hasText() ||
      !_isCanonicalText(origin.text, 4096) ||
      !draft.hasGoal() ||
      !_isCanonicalText(draft.goal, 4096) ||
      draft.revision.isNegative ||
      !_isValidTimestamp(draft.createdAtUnixMs) ||
      !_isValidTimestamp(draft.updatedAtUnixMs) ||
      draft.createdAtUnixMs > draft.updatedAtUnixMs) {
    throw const ProtocolException('Draft response contains invalid metadata.');
  }
  final status = switch (draft.status) {
    wire.FeatureDraftStatus.FEATURE_DRAFT_STATUS_DRAFT =>
      FeatureStudioDraftStatus.draft,
    wire.FeatureDraftStatus.FEATURE_DRAFT_STATUS_INSTALLED =>
      FeatureStudioDraftStatus.installed,
    _ => throw const ProtocolException('Draft response has invalid status.'),
  };
  final installationId = draft.hasInstallationId()
      ? draft.installationId
      : null;
  if (status == FeatureStudioDraftStatus.installed && installationId == null ||
      installationId != null && !_isCanonicalText(installationId, 256)) {
    throw const ProtocolException(
      'Draft response has invalid installation identity.',
    );
  }
  final behavior = _mapBehavior(draft.behavior);
  final wireSource = draft.hasSource() ? _mapSource(draft.source) : null;
  if (wireSource != null &&
      trustedSource != null &&
      !_sameSource(wireSource, trustedSource)) {
    throw const ProtocolException('Draft response changed Code.');
  }
  final source = trustedSource ?? wireSource!;
  _validateIncomingAggregates(behavior, source);
  FeatureStudioVerification? verification;
  if (draft.hasVerification()) {
    if (authoritativeVerification != null) {
      verification = _mapVerificationSummary(
        draft.verification,
        authoritativeVerification,
      );
    } else if (draft.verification.scenarios.isEmpty &&
        draft.verification.artifacts.isEmpty) {
      verification = _mapPersistedVerificationSummary(
        draft.verification,
        createdAtUnixMs: draft.createdAtUnixMs,
        updatedAtUnixMs: draft.updatedAtUnixMs,
      );
      if (!retainPersistedVerificationSummary) verification = null;
    } else {
      verification = _mapVerification(draft.verification);
    }
  } else if (authoritativeVerification?.isPassing == true) {
    throw const ProtocolException('Verification response is incomplete.');
  }
  if (verification != null &&
      (verification.verifiedAt.millisecondsSinceEpoch <
              draft.createdAtUnixMs.toInt() ||
          verification.verifiedAt.millisecondsSinceEpoch >
              draft.updatedAtUnixMs.toInt())) {
    throw const ProtocolException('Verification result is invalid.');
  }
  return FeatureStudioDraft(
    draftId: draft.draftId,
    originatingRequest: FeatureStudioOriginatingRequest(
      operationId: origin.operationId,
      conversationId: origin.conversationId,
      text: origin.text,
    ),
    goal: draft.goal,
    status: status,
    installationId: installationId,
    behavior: behavior,
    source: source,
    verification: verification,
    revision: draft.revision,
    createdAt: DateTime.fromMillisecondsSinceEpoch(
      draft.createdAtUnixMs.toInt(),
      isUtc: true,
    ),
    updatedAt: DateTime.fromMillisecondsSinceEpoch(
      draft.updatedAtUnixMs.toInt(),
      isUtc: true,
    ),
  );
}

FeatureStudioVerification _mapVerification(wire.FeatureVerification value) {
  if (!value.hasTotal() ||
      !value.hasVerifiedAtUnixMs() ||
      !value.hasSourceReference() ||
      (value.hasReleaseDigest() && !_isCanonicalDigest(value.releaseDigest)) ||
      !_isCanonicalSourceReference(value.sourceReference) ||
      value.total <= 0 ||
      value.total > 1024 ||
      value.passed < 0 ||
      value.failed < 0 ||
      value.skipped < 0 ||
      value.passed + value.failed + value.skipped != value.total ||
      !_isValidTimestamp(value.verifiedAtUnixMs) ||
      value.scenarios.length != value.total) {
    throw const ProtocolException('Verification result is invalid.');
  }
  final scenarios = <FeatureStudioVerificationScenario>[];
  final scenarioIds = <String>{};
  var passed = 0;
  var failed = 0;
  var skipped = 0;
  for (final scenario in value.scenarios) {
    if (!scenario.hasScenarioId() ||
        !scenario.hasName() ||
        !scenario.hasOutcome() ||
        !_isCanonicalText(scenario.scenarioId, 256) ||
        !_isCanonicalText(scenario.name, 512) ||
        !scenarioIds.add(scenario.scenarioId) ||
        scenario.durationMilliseconds.isNegative ||
        scenario.durationMilliseconds > Int64(70_000) ||
        (scenario.hasSafeFailure() &&
            !_isCanonicalText(scenario.safeFailure, 4096))) {
      throw const ProtocolException('Verification scenario is invalid.');
    }
    final outcome = switch (scenario.outcome) {
      wire.FeatureScenarioOutcome.FEATURE_SCENARIO_OUTCOME_PASSED =>
        FeatureStudioScenarioOutcome.passed,
      wire.FeatureScenarioOutcome.FEATURE_SCENARIO_OUTCOME_FAILED =>
        FeatureStudioScenarioOutcome.failed,
      wire.FeatureScenarioOutcome.FEATURE_SCENARIO_OUTCOME_SKIPPED =>
        FeatureStudioScenarioOutcome.skipped,
      _ => throw const ProtocolException(
        'Verification scenario outcome is invalid.',
      ),
    };
    if (outcome == FeatureStudioScenarioOutcome.passed &&
            scenario.hasSafeFailure() ||
        outcome == FeatureStudioScenarioOutcome.failed &&
            !scenario.hasSafeFailure()) {
      throw const ProtocolException('Verification scenario is inconsistent.');
    }
    switch (outcome) {
      case FeatureStudioScenarioOutcome.passed:
        passed++;
        break;
      case FeatureStudioScenarioOutcome.failed:
        failed++;
        break;
      case FeatureStudioScenarioOutcome.skipped:
        skipped++;
        break;
    }
    scenarios.add(
      FeatureStudioVerificationScenario(
        scenarioId: scenario.scenarioId,
        name: scenario.name,
        outcome: outcome,
        safeFailure: scenario.hasSafeFailure() ? scenario.safeFailure : null,
        durationMilliseconds: scenario.durationMilliseconds.toInt(),
      ),
    );
  }
  if (passed != value.passed ||
      failed != value.failed ||
      skipped != value.skipped) {
    throw const ProtocolException('Verification totals are inconsistent.');
  }
  final artifacts = <FeatureStudioVerificationArtifact>[];
  if (value.artifacts.length > 32) {
    throw const ProtocolException('Verification artifacts are invalid.');
  }
  final artifactNames = <String>{};
  for (final artifact in value.artifacts) {
    if (!artifact.hasName() ||
        !artifact.hasMediaType() ||
        !artifact.hasDigest() ||
        !_isCanonicalText(artifact.name, 256) ||
        !artifactNames.add(artifact.name) ||
        !_isCanonicalText(artifact.mediaType, 128) ||
        artifact.sizeBytes.isNegative ||
        artifact.sizeBytes > Int64(1_048_576) ||
        !_isCanonicalSourceReference(artifact.digest)) {
      throw const ProtocolException('Verification artifact is invalid.');
    }
    artifacts.add(
      FeatureStudioVerificationArtifact(
        name: artifact.name,
        mediaType: artifact.mediaType,
        sizeBytes: artifact.sizeBytes.toInt(),
        digest: artifact.digest,
      ),
    );
  }
  return FeatureStudioVerification(
    releaseDigest: value.hasReleaseDigest() ? value.releaseDigest : null,
    total: value.total,
    passed: value.passed,
    failed: value.failed,
    skipped: value.skipped,
    verifiedAt: DateTime.fromMillisecondsSinceEpoch(
      value.verifiedAtUnixMs.toInt(),
      isUtc: true,
    ),
    sourceReference: value.sourceReference,
    scenarios: scenarios,
    artifacts: artifacts,
  );
}

void _validatePersistedVerificationSummary(
  wire.FeatureVerification value, {
  required Int64 createdAtUnixMs,
  required Int64 updatedAtUnixMs,
}) {
  if (!value.hasReleaseDigest() ||
      !_isCanonicalDigest(value.releaseDigest) ||
      !value.hasSourceReference() ||
      !_isCanonicalSourceReference(value.sourceReference) ||
      !value.hasTotal() ||
      !value.hasVerifiedAtUnixMs() ||
      value.total <= 0 ||
      value.total > 1024 ||
      value.passed < 0 ||
      value.failed < 0 ||
      value.skipped < 0 ||
      value.passed + value.failed + value.skipped != value.total ||
      !_isValidTimestamp(value.verifiedAtUnixMs) ||
      value.verifiedAtUnixMs < createdAtUnixMs ||
      value.verifiedAtUnixMs > updatedAtUnixMs) {
    throw const ProtocolException('Persisted Verification summary is invalid.');
  }
}

FeatureStudioVerification _mapPersistedVerificationSummary(
  wire.FeatureVerification value, {
  required Int64 createdAtUnixMs,
  required Int64 updatedAtUnixMs,
}) {
  _validatePersistedVerificationSummary(
    value,
    createdAtUnixMs: createdAtUnixMs,
    updatedAtUnixMs: updatedAtUnixMs,
  );
  return FeatureStudioVerification(
    releaseDigest: value.releaseDigest,
    sourceReference: value.sourceReference,
    total: value.total,
    passed: value.passed,
    failed: value.failed,
    skipped: value.skipped,
    verifiedAt: DateTime.fromMillisecondsSinceEpoch(
      value.verifiedAtUnixMs.toInt(),
      isUtc: true,
    ),
  );
}

FeatureStudioVerification _mapVerificationSummary(
  wire.FeatureVerification value,
  FeatureStudioVerification authoritative,
) {
  if (value.scenarios.isNotEmpty || value.artifacts.isNotEmpty) {
    final echoed = _mapVerification(value);
    if (!_sameVerification(echoed, authoritative)) {
      throw const ProtocolException(
        'Verification response summary is inconsistent.',
      );
    }
    return authoritative;
  }
  if (!value.hasTotal() ||
      !value.hasVerifiedAtUnixMs() ||
      value.total <= 0 ||
      value.total > 1024 ||
      value.passed < 0 ||
      value.failed < 0 ||
      value.skipped < 0 ||
      value.passed + value.failed + value.skipped != value.total ||
      !_isValidTimestamp(value.verifiedAtUnixMs) ||
      value.hasReleaseDigest() != (authoritative.releaseDigest != null) ||
      value.hasReleaseDigest() &&
          (value.releaseDigest != authoritative.releaseDigest ||
              !_isCanonicalDigest(value.releaseDigest)) ||
      value.hasSourceReference() &&
          (value.sourceReference != authoritative.sourceReference ||
              !_isCanonicalSourceReference(value.sourceReference)) ||
      value.total != authoritative.total ||
      value.passed != authoritative.passed ||
      value.failed != authoritative.failed ||
      value.skipped != authoritative.skipped ||
      value.verifiedAtUnixMs.toInt() !=
          authoritative.verifiedAt.millisecondsSinceEpoch) {
    throw const ProtocolException(
      'Verification response summary is inconsistent.',
    );
  }
  return authoritative;
}

FeatureStudioVersion _mapVersionWithTrustedSource(
  wire.FeatureRelease value,
  FeatureStudioSource trustedSource,
) {
  final metadata = _mapVersion(value, requireSource: false);
  if (metadata.source != null &&
      !_sameSource(metadata.source!, trustedSource)) {
    throw const ProtocolException('Version response changed Code.');
  }
  final version = FeatureStudioVersion(
    digest: metadata.digest,
    sourceReference: metadata.sourceReference,
    requestedCapabilityIds: metadata.requestedCapabilityIds,
    dependencies: metadata.dependencies,
    source: trustedSource,
  );
  _validateVersion(version, requireSource: true);
  return version;
}

FeatureStudioVersion _mapVersion(
  wire.FeatureRelease value, {
  required bool requireSource,
}) {
  if (!value.hasDigest() ||
      !value.hasSourceKind() ||
      !value.hasSourceReference() ||
      !_isCanonicalDigest(value.digest) ||
      !_isCanonicalSourceReference(value.sourceReference) ||
      value.sourceKind !=
          wire.FeatureSourceKind.FEATURE_SOURCE_KIND_RUNTIME_AUTHORED ||
      (requireSource && !value.hasSource())) {
    throw const ProtocolException('Version response is invalid.');
  }
  final source = value.hasSource() ? _mapSource(value.source) : null;
  if (source != null && validateFeatureStudioSource(source).isNotEmpty) {
    throw const ProtocolException('Version source is invalid.');
  }
  final version = FeatureStudioVersion(
    digest: value.digest,
    sourceReference: value.sourceReference,
    requestedCapabilityIds: value.requestedCapabilityIds,
    dependencies: value.dependencies,
    source: source,
  );
  _validateVersion(version, requireSource: requireSource);
  return version;
}

List<FeatureStudioGrant> _mapGrants(
  Iterable<wire.FeatureGrant> values, {
  required List<String> requiredCapabilityIds,
}) {
  final grants = values.map(_mapGrant).toList(growable: false);
  _validateGrants(grants, requiredCapabilityIds: requiredCapabilityIds);
  return grants;
}

FeatureStudioGrant _mapGrant(wire.FeatureGrant value) {
  if (!value.hasCapabilityId() ||
      !value.hasCapabilityVersion() ||
      !value.hasConstraintsJson() ||
      !_isCanonicalText(value.capabilityId, 256) ||
      value.capabilityVersion < 1 ||
      value.hasProvider() != value.hasConnectionId() ||
      (value.hasProvider() &&
          (!_isCanonicalText(value.provider, 64) ||
              !_isCanonicalText(value.connectionId, 256)))) {
    throw const ProtocolException('Access grant is invalid.');
  }
  final summary = FeatureGrantConstraintPolicy.summarize(
    constraintsJson: value.constraintsJson,
    capabilityId: value.capabilityId,
  );
  if (summary == null) {
    throw const ProtocolException('Access grant constraint is invalid.');
  }
  return FeatureStudioGrant(
    capabilityId: value.capabilityId,
    capabilityVersion: value.capabilityVersion,
    provider: value.hasProvider() ? value.provider : null,
    connectionId: value.hasConnectionId() ? value.connectionId : null,
    constraintsJson: value.constraintsJson,
    constraintSummary: summary,
  );
}

List<String> _mapSubscriptions(Iterable<String> values) {
  final subscriptions = values.toList(growable: false);
  if (subscriptions.isEmpty ||
      !_isCanonicalStrings(
        subscriptions,
        maximumItems: 64,
        maximumLength: 256,
      )) {
    throw const ProtocolException('Automation subscription is invalid.');
  }
  return subscriptions;
}

wire.FeatureGrant _toWireGrant(FeatureStudioGrant grant) => wire.FeatureGrant(
  capabilityId: grant.capabilityId,
  capabilityVersion: grant.capabilityVersion,
  connectionId: grant.connectionId,
  constraintsJson: grant.constraintsJson,
  provider: grant.provider,
);

FeatureStudioSuggestion _mapSuggestion(
  wire.FeatureDraftPatch patch, {
  required String draftId,
  required Int64 expectedRevision,
}) {
  if (!patch.hasPatchId() ||
      !_isCanonicalText(patch.patchId, 256) ||
      !patch.hasDraftId() ||
      patch.draftId != draftId ||
      !patch.hasBaseRevision() ||
      patch.baseRevision != expectedRevision ||
      !patch.hasSummary() ||
      !_isCanonicalText(patch.summary, 2048) ||
      !patch.hasReplacementBehavior() ||
      !patch.hasReplacementSource()) {
    throw const ProtocolException('Suggestion response is incomplete.');
  }
  final behavior = _mapBehavior(patch.replacementBehavior);
  final source = _mapSource(patch.replacementSource);
  _validateIncomingAggregates(behavior, source);
  return FeatureStudioSuggestion(
    patchId: patch.patchId,
    draftId: patch.draftId,
    baseRevision: patch.baseRevision,
    summary: patch.summary,
    replacementBehavior: behavior,
    replacementSource: source,
  );
}

FeatureStudioBehavior _mapBehavior(wire.FeatureBehavior behavior) =>
    FeatureStudioBehavior(
      scenarios: behavior.scenarios.map(
        (scenario) => FeatureStudioScenario(
          scenarioId: scenario.scenarioId,
          name: scenario.name,
          given: scenario.given,
          when: scenario.when,
          then: scenario.then,
        ),
      ),
    );

FeatureStudioSource _mapSource(wire.FeatureSourceSnapshot source) =>
    FeatureStudioSource(
      implementationProjectPath: source.implementationProjectPath,
      scenarioProjectPath: source.scenarioProjectPath,
      files: source.files.map(
        (file) =>
            FeatureStudioSourceFile(path: file.path, content: file.content),
      ),
    );

wire.FeatureBehavior _toWireBehavior(FeatureStudioBehavior behavior) =>
    wire.FeatureBehavior(
      scenarios: behavior.scenarios.map(
        (scenario) => wire.FeatureScenario(
          scenarioId: scenario.scenarioId,
          name: scenario.name,
          given: scenario.given,
          when: scenario.when,
          then: scenario.then,
        ),
      ),
    );

wire.FeatureSourceSnapshot _toWireSource(FeatureStudioSource source) =>
    wire.FeatureSourceSnapshot(
      implementationProjectPath: source.implementationProjectPath,
      scenarioProjectPath: source.scenarioProjectPath,
      files: source.files.map(
        (file) =>
            wire.FeatureSourceFile(path: file.path, content: file.content),
      ),
    );

wire.FeatureDraftPatch _toWirePatch(FeatureStudioSuggestion suggestion) =>
    wire.FeatureDraftPatch(
      patchId: suggestion.patchId,
      draftId: suggestion.draftId,
      baseRevision: suggestion.baseRevision,
      summary: suggestion.summary,
      replacementBehavior: _toWireBehavior(suggestion.replacementBehavior),
      replacementSource: _toWireSource(suggestion.replacementSource),
    );

void _validateIncomingAggregates(
  FeatureStudioBehavior behavior,
  FeatureStudioSource source,
) {
  if (validateFeatureStudioBehavior(behavior).isNotEmpty ||
      validateFeatureStudioSource(source).isNotEmpty) {
    throw const ProtocolException('Draft response contains invalid content.');
  }
}

void _validateOutgoingBehavior(FeatureStudioBehavior behavior) {
  if (validateFeatureStudioBehavior(behavior).isNotEmpty) {
    throw ArgumentError.value(behavior, 'behavior');
  }
}

void _validateOutgoingSource(FeatureStudioSource source) {
  if (validateFeatureStudioSource(source).isNotEmpty) {
    throw ArgumentError.value(source, 'source');
  }
}

void _validateVersion(
  FeatureStudioVersion version, {
  required bool requireSource,
}) {
  if (!_isCanonicalDigest(version.digest) ||
      !_isCanonicalSourceReference(version.sourceReference) ||
      !_isCanonicalStrings(
        version.requestedCapabilityIds,
        maximumItems: 32,
        maximumLength: 256,
      ) ||
      !_isCanonicalStrings(
        version.dependencies,
        maximumItems: 64,
        maximumLength: 256,
      ) ||
      requireSource && version.source == null ||
      version.source != null &&
          validateFeatureStudioSource(version.source!).isNotEmpty) {
    throw ArgumentError.value(version, 'version');
  }
}

void _validateGrants(
  Iterable<FeatureStudioGrant> values, {
  required List<String> requiredCapabilityIds,
}) {
  final grants = values.toList(growable: false);
  if (grants.length != requiredCapabilityIds.length || grants.length > 32) {
    throw ArgumentError.value(values, 'grants');
  }
  final identities = <String>[];
  for (final grant in grants) {
    final validated = _mapGrant(_toWireGrant(grant));
    if (validated.constraintSummary != grant.constraintSummary) {
      throw ArgumentError.value(values, 'grants');
    }
    identities.add(grant.capabilityId);
  }
  final expected = requiredCapabilityIds.toList()..sort();
  final actual = identities..sort();
  if (!_sameStrings(actual, expected)) {
    throw ArgumentError.value(values, 'grants');
  }
}

void _validateSubscriptions(Iterable<String> values) {
  final subscriptions = values.toList(growable: false);
  if (subscriptions.isEmpty ||
      !_isCanonicalStrings(
        subscriptions,
        maximumItems: 64,
        maximumLength: 256,
      )) {
    throw ArgumentError.value(values, 'subscriptions');
  }
}

void _validateMutationIdentity(
  String draftId,
  Int64 expectedRevision,
  String idempotencyId,
) {
  _validateIdentity(draftId, 'draftId', maximumLength: 128);
  _validateRevision(expectedRevision);
  _validateIdentity(idempotencyId, 'idempotencyId', maximumLength: 256);
}

void _validateSuggestionIdentity(
  String draftId,
  Int64 expectedRevision,
  FeatureStudioSuggestion suggestion,
) {
  if (suggestion.draftId != draftId ||
      suggestion.baseRevision != expectedRevision ||
      !_isCanonicalText(suggestion.patchId, 256) ||
      !_isCanonicalText(suggestion.summary, 2048)) {
    throw ArgumentError.value(suggestion, 'suggestion');
  }
}

void _validateIdentity(
  String value,
  String name, {
  required int maximumLength,
}) {
  if (!_isCanonicalText(value, maximumLength)) {
    throw ArgumentError.value(value, name);
  }
}

void _validateText(String value, String name, {required int maximumLength}) {
  if (!_isCanonicalText(value, maximumLength)) {
    throw ArgumentError.value(value, name);
  }
}

void _validateRevision(Int64 value) {
  if (value.isNegative) {
    throw ArgumentError.value(value, 'expectedRevision');
  }
}

bool _isCanonicalText(String value, int maximumLength) =>
    value.isNotEmpty &&
    value.length <= maximumLength &&
    value.trim() == value &&
    !value.runes.any(_isControl);

bool _isControl(int rune) => rune < 32 || (rune >= 127 && rune <= 159);

bool _isCanonicalDigest(String value) =>
    RegExp(r'^[0-9a-f]{64}$').hasMatch(value);

bool _isCanonicalSourceReference(String value) =>
    value.startsWith('sha256:') && _isCanonicalDigest(value.substring(7));

bool _isValidTimestamp(Int64 value) =>
    !value.isNegative && value <= Int64(253402300799999);

void _requireBehaviorEcho(
  FeatureStudioDraft draft,
  FeatureStudioBehavior expected,
) {
  if (!_sameBehavior(draft.behavior, expected)) {
    throw const ProtocolException('Draft response changed Behavior.');
  }
}

void _requireSourceEcho(
  FeatureStudioDraft draft,
  FeatureStudioSource expected,
) {
  if (!_sameSource(draft.source, expected)) {
    throw const ProtocolException('Draft response changed Code.');
  }
}

void _requireImmutableDraftEcho(
  FeatureStudioDraft actual,
  FeatureStudioDraft expected,
) {
  final left = actual.originatingRequest;
  final right = expected.originatingRequest;
  if (actual.draftId != expected.draftId ||
      actual.goal != expected.goal ||
      actual.createdAt != expected.createdAt ||
      left.operationId != right.operationId ||
      left.conversationId != right.conversationId ||
      left.text != right.text) {
    throw const ProtocolException('Draft response changed immutable metadata.');
  }
}

void _requireDraftStatus(FeatureStudioDraft draft) {
  if (draft.status != FeatureStudioDraftStatus.draft) {
    throw const ProtocolException('Draft response changed status.');
  }
}

void _requireNoVerification(FeatureStudioDraft draft) {
  if (draft.verification != null) {
    throw const ProtocolException(
      'Draft response retained stale verification.',
    );
  }
}

void _requireVerificationEcho(
  FeatureStudioVerification? actual,
  FeatureStudioVerification? expected,
) {
  if (!_sameVerification(actual, expected)) {
    throw const ProtocolException('Draft response changed verification.');
  }
}

bool _sameVerification(
  FeatureStudioVerification? left,
  FeatureStudioVerification? right,
) {
  if (identical(left, right)) return true;
  if (left == null || right == null) return false;
  return left.releaseDigest == right.releaseDigest &&
      left.sourceReference == right.sourceReference &&
      left.total == right.total &&
      left.passed == right.passed &&
      left.failed == right.failed &&
      left.skipped == right.skipped &&
      left.verifiedAt == right.verifiedAt &&
      _sameVerificationScenarios(left.scenarios, right.scenarios) &&
      _sameVerificationArtifacts(left.artifacts, right.artifacts);
}

bool _sameVerificationScenarios(
  List<FeatureStudioVerificationScenario> left,
  List<FeatureStudioVerificationScenario> right,
) {
  if (left.length != right.length) return false;
  for (var index = 0; index < left.length; index++) {
    final a = left[index];
    final b = right[index];
    if (a.scenarioId != b.scenarioId ||
        a.name != b.name ||
        a.outcome != b.outcome ||
        a.safeFailure != b.safeFailure ||
        a.durationMilliseconds != b.durationMilliseconds) {
      return false;
    }
  }
  return true;
}

bool _sameVerificationArtifacts(
  List<FeatureStudioVerificationArtifact> left,
  List<FeatureStudioVerificationArtifact> right,
) {
  if (left.length != right.length) return false;
  for (var index = 0; index < left.length; index++) {
    final a = left[index];
    final b = right[index];
    if (a.name != b.name ||
        a.mediaType != b.mediaType ||
        a.sizeBytes != b.sizeBytes ||
        a.digest != b.digest) {
      return false;
    }
  }
  return true;
}

bool _sameVersion(FeatureStudioVersion left, FeatureStudioVersion right) =>
    left.digest == right.digest &&
    left.sourceReference == right.sourceReference &&
    _sameStrings(left.requestedCapabilityIds, right.requestedCapabilityIds) &&
    _sameStrings(left.dependencies, right.dependencies) &&
    _sameNullableSource(left.source, right.source);

bool _sameNullableSource(
  FeatureStudioSource? left,
  FeatureStudioSource? right,
) {
  if (identical(left, right)) return true;
  if (left == null || right == null) return false;
  return _sameSource(left, right);
}

bool _sameGrants(
  Iterable<FeatureStudioGrant> left,
  Iterable<FeatureStudioGrant> right,
) {
  final a = left.toList()
    ..sort((x, y) => x.capabilityId.compareTo(y.capabilityId));
  final b = right.toList()
    ..sort((x, y) => x.capabilityId.compareTo(y.capabilityId));
  if (a.length != b.length) return false;
  for (var index = 0; index < a.length; index++) {
    if (a[index].capabilityId != b[index].capabilityId ||
        a[index].capabilityVersion != b[index].capabilityVersion ||
        a[index].provider != b[index].provider ||
        a[index].connectionId != b[index].connectionId ||
        a[index].constraintsJson != b[index].constraintsJson) {
      return false;
    }
  }
  return true;
}

bool _sameStrings(Iterable<String> left, Iterable<String> right) {
  final a = left.toList()..sort();
  final b = right.toList()..sort();
  if (a.length != b.length) return false;
  for (var index = 0; index < a.length; index++) {
    if (a[index] != b[index]) return false;
  }
  return true;
}

bool _isCanonicalStrings(
  Iterable<String> values, {
  required int maximumItems,
  required int maximumLength,
}) {
  final list = values.toList(growable: false);
  return list.length <= maximumItems &&
      list.toSet().length == list.length &&
      list.every((value) => _isCanonicalText(value, maximumLength));
}

bool _sameBehavior(FeatureStudioBehavior left, FeatureStudioBehavior right) {
  if (left.scenarios.length != right.scenarios.length) return false;
  for (var index = 0; index < left.scenarios.length; index++) {
    final a = left.scenarios[index];
    final b = right.scenarios[index];
    if (a.scenarioId != b.scenarioId ||
        a.name != b.name ||
        a.given != b.given ||
        a.when != b.when ||
        a.then != b.then) {
      return false;
    }
  }
  return true;
}

bool _sameSource(FeatureStudioSource left, FeatureStudioSource right) {
  if (left.implementationProjectPath != right.implementationProjectPath ||
      left.scenarioProjectPath != right.scenarioProjectPath ||
      left.files.length != right.files.length) {
    return false;
  }
  for (var index = 0; index < left.files.length; index++) {
    final a = left.files[index];
    final b = right.files[index];
    if (a.path != b.path || a.content != b.content) return false;
  }
  return true;
}
