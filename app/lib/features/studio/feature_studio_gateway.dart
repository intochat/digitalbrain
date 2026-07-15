import 'package:fixnum/fixnum.dart';

import '../../core/session/digitalbrain_client.dart';
import '../../grpc/ui.pb.dart' as wire;
import '../../runtime/runtime_errors.dart';
import 'feature_studio_models.dart';
import 'feature_studio_validation.dart';

abstract interface class FeatureStudioGateway {
  Future<FeatureStudioDraft> loadDraft(String draftId);

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

  Future<FeatureStudioDraft> verifyDraft({
    required String draftId,
    required Int64 expectedRevision,
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
    return _mapDraftReply(reply, draftId);
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
  Future<FeatureStudioDraft> verifyDraft({
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
    if (!reply.hasDraft() || !reply.hasRelease()) {
      throw const ProtocolException('Verification response is incomplete.');
    }
    final release = reply.release;
    if (!release.hasDigest() ||
        !release.hasSourceKind() ||
        !_isCanonicalDigest(release.digest) ||
        release.sourceKind !=
            wire.FeatureSourceKind.FEATURE_SOURCE_KIND_RUNTIME_AUTHORED) {
      throw const ProtocolException('Verification release is invalid.');
    }
    final draft = _mapDraft(reply.draft, expectedDraftId: draftId);
    _requireBehaviorEcho(draft, expectedBehavior);
    _requireSourceEcho(draft, expectedSource);
    _requireDraftStatus(draft);
    final verification = draft.verification;
    if (draft.revision != expectedRevision + Int64.ONE ||
        verification == null ||
        verification.releaseDigest != release.digest ||
        verification.total <= 0 ||
        verification.passed != verification.total ||
        verification.failed != 0 ||
        verification.skipped != 0) {
      throw const ProtocolException('Verification response is inconsistent.');
    }
    return draft;
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
  final draft = _mapDraftReply(reply, draftId);
  if (draft.revision != requiredRevision) {
    throw const ProtocolException('Draft response has an invalid revision.');
  }
  return draft;
}

FeatureStudioDraft _mapDraft(
  wire.FeatureDraft draft, {
  required String expectedDraftId,
}) {
  if (!draft.hasDraftId() ||
      draft.draftId != expectedDraftId ||
      !draft.hasOriginatingRequest() ||
      !draft.hasBehavior() ||
      !draft.hasSource() ||
      !draft.hasStatus() ||
      !draft.hasRevision() ||
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
  final behavior = _mapBehavior(draft.behavior);
  final source = _mapSource(draft.source);
  _validateIncomingAggregates(behavior, source);
  final verification = draft.hasVerification()
      ? _mapVerification(draft.verification)
      : null;
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
  if (!value.hasReleaseDigest() ||
      !value.hasTotal() ||
      !value.hasPassed() ||
      !value.hasFailed() ||
      !value.hasSkipped() ||
      !value.hasVerifiedAtUnixMs() ||
      !_isCanonicalDigest(value.releaseDigest) ||
      value.total <= 0 ||
      value.passed < 0 ||
      value.failed < 0 ||
      value.skipped < 0 ||
      value.passed + value.failed + value.skipped != value.total) {
    throw const ProtocolException('Verification result is invalid.');
  }
  return FeatureStudioVerification(
    releaseDigest: value.releaseDigest,
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
      left.total == right.total &&
      left.passed == right.passed &&
      left.failed == right.failed &&
      left.skipped == right.skipped &&
      left.verifiedAt == right.verifiedAt;
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
