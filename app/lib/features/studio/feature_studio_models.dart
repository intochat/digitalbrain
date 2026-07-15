import 'package:fixnum/fixnum.dart';

import '../shared/feature_grant_constraint_policy.dart';

enum FeatureStudioDraftStatus { draft, installed }

class FeatureStudioOriginatingRequest {
  const FeatureStudioOriginatingRequest({
    required this.operationId,
    required this.conversationId,
    required this.text,
  });

  final String operationId;
  final String conversationId;
  final String text;
}

class FeatureStudioScenario {
  const FeatureStudioScenario({
    required this.scenarioId,
    required this.name,
    required this.given,
    required this.when,
    required this.then,
  });

  final String scenarioId;
  final String name;
  final String given;
  final String when;
  final String then;
}

class FeatureStudioBehavior {
  FeatureStudioBehavior({required Iterable<FeatureStudioScenario> scenarios})
    : scenarios = List.unmodifiable(scenarios);

  final List<FeatureStudioScenario> scenarios;
}

class FeatureStudioSourceFile {
  const FeatureStudioSourceFile({required this.path, required this.content});

  final String path;
  final String content;
}

class FeatureStudioSource {
  FeatureStudioSource({
    required this.implementationProjectPath,
    required this.scenarioProjectPath,
    required Iterable<FeatureStudioSourceFile> files,
  }) : files = List.unmodifiable(files);

  final String implementationProjectPath;
  final String scenarioProjectPath;
  final List<FeatureStudioSourceFile> files;
}

enum FeatureStudioScenarioOutcome { passed, failed, skipped }

class FeatureStudioVerificationScenario {
  const FeatureStudioVerificationScenario({
    required this.scenarioId,
    required this.name,
    required this.outcome,
    required this.safeFailure,
    required this.durationMilliseconds,
  });

  final String scenarioId;
  final String name;
  final FeatureStudioScenarioOutcome outcome;
  final String? safeFailure;
  final int durationMilliseconds;
}

class FeatureStudioVerificationArtifact {
  const FeatureStudioVerificationArtifact({
    required this.name,
    required this.mediaType,
    required this.sizeBytes,
    required this.digest,
  });

  final String name;
  final String mediaType;
  final int sizeBytes;
  final String digest;
}

class FeatureStudioVerification {
  FeatureStudioVerification({
    required this.releaseDigest,
    required this.total,
    required this.passed,
    required this.failed,
    required this.skipped,
    required this.verifiedAt,
    this.sourceReference = '',
    Iterable<FeatureStudioVerificationScenario> scenarios = const [],
    Iterable<FeatureStudioVerificationArtifact> artifacts = const [],
  }) : scenarios = List.unmodifiable(scenarios),
       artifacts = List.unmodifiable(artifacts);

  final String? releaseDigest;
  final int total;
  final int passed;
  final int failed;
  final int skipped;
  final DateTime verifiedAt;
  final String sourceReference;
  final List<FeatureStudioVerificationScenario> scenarios;
  final List<FeatureStudioVerificationArtifact> artifacts;

  bool get isPassing =>
      total > 0 && passed == total && failed == 0 && skipped == 0;
}

class FeatureStudioVersion {
  FeatureStudioVersion({
    required this.digest,
    required this.sourceReference,
    required Iterable<String> requestedCapabilityIds,
    required Iterable<String> dependencies,
    required this.source,
  }) : requestedCapabilityIds = List.unmodifiable(requestedCapabilityIds),
       dependencies = List.unmodifiable(dependencies);

  final String digest;
  final String sourceReference;
  final List<String> requestedCapabilityIds;
  final List<String> dependencies;
  final FeatureStudioSource? source;
}

enum FeatureStudioVersionDiffStatus {
  noPreviousVersion,
  sourceUnavailable,
  compared,
}

enum FeatureStudioVersionFileChangeKind { added, changed, removed }

enum FeatureStudioVersionCoordinateKind {
  implementationProjectPath,
  scenarioProjectPath,
}

class FeatureStudioVersionCoordinateChange {
  const FeatureStudioVersionCoordinateChange({
    required this.kind,
    required this.previousValue,
    required this.currentValue,
  });

  final FeatureStudioVersionCoordinateKind kind;
  final String previousValue;
  final String currentValue;
}

class FeatureStudioVersionFileChange {
  const FeatureStudioVersionFileChange({
    required this.kind,
    required this.path,
    required this.previousContent,
    required this.currentContent,
  });

  final FeatureStudioVersionFileChangeKind kind;
  final String path;
  final String? previousContent;
  final String? currentContent;
}

class FeatureStudioVersionDiff {
  FeatureStudioVersionDiff({
    required this.status,
    Iterable<FeatureStudioVersionFileChange> files = const [],
    Iterable<FeatureStudioVersionCoordinateChange> coordinateChanges = const [],
  }) : files = List.unmodifiable(files),
       coordinateChanges = List.unmodifiable(coordinateChanges);

  final FeatureStudioVersionDiffStatus status;
  final List<FeatureStudioVersionFileChange> files;
  final List<FeatureStudioVersionCoordinateChange> coordinateChanges;
}

FeatureStudioVersionDiff buildFeatureStudioVersionDiff({
  required FeatureStudioVersion currentVersion,
  required FeatureStudioVersion? previousVersion,
}) {
  if (previousVersion == null) {
    return FeatureStudioVersionDiff(
      status: FeatureStudioVersionDiffStatus.noPreviousVersion,
    );
  }
  final currentSource = currentVersion.source;
  final previousSource = previousVersion.source;
  if (currentSource == null || previousSource == null) {
    return FeatureStudioVersionDiff(
      status: FeatureStudioVersionDiffStatus.sourceUnavailable,
    );
  }
  final coordinateChanges = <FeatureStudioVersionCoordinateChange>[
    if (previousSource.implementationProjectPath !=
        currentSource.implementationProjectPath)
      FeatureStudioVersionCoordinateChange(
        kind: FeatureStudioVersionCoordinateKind.implementationProjectPath,
        previousValue: previousSource.implementationProjectPath,
        currentValue: currentSource.implementationProjectPath,
      ),
    if (previousSource.scenarioProjectPath != currentSource.scenarioProjectPath)
      FeatureStudioVersionCoordinateChange(
        kind: FeatureStudioVersionCoordinateKind.scenarioProjectPath,
        previousValue: previousSource.scenarioProjectPath,
        currentValue: currentSource.scenarioProjectPath,
      ),
  ];
  final previousByPath = {
    for (final file in previousSource.files) file.path: file,
  };
  final currentByPath = {
    for (final file in currentSource.files) file.path: file,
  };
  final changes = <FeatureStudioVersionFileChange>[];
  for (final current in currentSource.files) {
    final previous = previousByPath[current.path];
    if (previous == null) {
      changes.add(
        FeatureStudioVersionFileChange(
          kind: FeatureStudioVersionFileChangeKind.added,
          path: current.path,
          previousContent: null,
          currentContent: current.content,
        ),
      );
    } else if (previous.content != current.content) {
      changes.add(
        FeatureStudioVersionFileChange(
          kind: FeatureStudioVersionFileChangeKind.changed,
          path: current.path,
          previousContent: previous.content,
          currentContent: current.content,
        ),
      );
    }
  }
  for (final previous in previousSource.files) {
    if (!currentByPath.containsKey(previous.path)) {
      changes.add(
        FeatureStudioVersionFileChange(
          kind: FeatureStudioVersionFileChangeKind.removed,
          path: previous.path,
          previousContent: previous.content,
          currentContent: null,
        ),
      );
    }
  }
  return FeatureStudioVersionDiff(
    status: FeatureStudioVersionDiffStatus.compared,
    files: changes,
    coordinateChanges: coordinateChanges,
  );
}

class FeatureStudioVerificationResult {
  const FeatureStudioVerificationResult({
    required this.draft,
    required this.verification,
    required this.version,
  });

  final FeatureStudioDraft draft;
  final FeatureStudioVerification verification;
  final FeatureStudioVersion? version;
}

class FeatureStudioGrant {
  const FeatureStudioGrant({
    required this.capabilityId,
    required this.capabilityVersion,
    required this.provider,
    required this.connectionId,
    required this.constraintsJson,
    required this.constraintSummary,
  });

  final String capabilityId;
  final int capabilityVersion;
  final String? provider;
  final String? connectionId;
  final String constraintsJson;
  final String constraintSummary;
}

class FeatureStudioAccessReview {
  FeatureStudioAccessReview({
    required this.draft,
    required this.version,
    required this.installationId,
    required Iterable<FeatureStudioGrant> grants,
    required Iterable<String> subscriptions,
    required this.previousVersion,
  }) : grants = List.unmodifiable(grants),
       subscriptions = List.unmodifiable(subscriptions);

  final FeatureStudioDraft draft;
  final FeatureStudioVersion version;
  final String installationId;
  final List<FeatureStudioGrant> grants;
  final List<String> subscriptions;
  final FeatureStudioVersion? previousVersion;
}

class FeatureStudioInstallSuccess {
  FeatureStudioInstallSuccess({
    required this.draft,
    required this.version,
    required this.installationId,
    required Iterable<FeatureStudioGrant> activeGrants,
    required Iterable<String> subscriptions,
    required this.rollbackAvailable,
  }) : activeGrants = List.unmodifiable(activeGrants),
       subscriptions = List.unmodifiable(subscriptions);

  final FeatureStudioDraft draft;
  final FeatureStudioVersion version;
  final String installationId;
  final List<FeatureStudioGrant> activeGrants;
  final List<String> subscriptions;
  final bool rollbackAvailable;

  FeatureStudioOriginatingRequest get originalRequest =>
      draft.originatingRequest;
}

class FeatureStudioInstallationRecovery {
  FeatureStudioInstallationRecovery({
    required this.installed,
    required this.verification,
    required this.version,
    required this.installationId,
    required Iterable<FeatureStudioGrant> grants,
    required Iterable<String> subscriptions,
    required this.previousVersion,
    required this.decisionId,
    required this.idempotencyId,
    required this.rollbackAvailable,
    required this.paused,
    required this.pauseReason,
  }) : grants = List.unmodifiable(grants),
       subscriptions = List.unmodifiable(subscriptions) {
    if (!_isRecoveryIdentity(installationId, 256) ||
        !verification.isPassing ||
        verification.releaseDigest != version.digest ||
        verification.sourceReference != version.sourceReference ||
        verification.scenarios.length != verification.total ||
        version.source == null ||
        !_validRecoveryAuthority(version, this.grants, this.subscriptions) ||
        previousVersion?.digest == version.digest ||
        previousVersion != null && previousVersion!.source == null) {
      throw ArgumentError('Invalid installation recovery authority.');
    }
    if (installed) {
      if (decisionId != null ||
          idempotencyId != null ||
          rollbackAvailable != (previousVersion != null) ||
          paused != (pauseReason != null) ||
          paused && (rollbackAvailable || previousVersion != null) ||
          pauseReason != null && !_isRecoveryText(pauseReason!, 4096)) {
        throw ArgumentError('Invalid installed recovery state.');
      }
    } else if (!_isRecoveryIdentity(decisionId, 256) ||
        !_isRecoveryIdentity(idempotencyId, 256) ||
        rollbackAvailable ||
        paused ||
        pauseReason != null) {
      throw ArgumentError('Invalid reserved recovery state.');
    }
  }

  final bool installed;
  final FeatureStudioVerification verification;
  final FeatureStudioVersion version;
  final String installationId;
  final List<FeatureStudioGrant> grants;
  final List<String> subscriptions;
  final FeatureStudioVersion? previousVersion;
  final String? decisionId;
  final String? idempotencyId;
  final bool rollbackAvailable;
  final bool paused;
  final String? pauseReason;
}

class FeatureStudioSuggestion {
  const FeatureStudioSuggestion({
    required this.patchId,
    required this.draftId,
    required this.baseRevision,
    required this.summary,
    required this.replacementBehavior,
    required this.replacementSource,
  });

  final String patchId;
  final String draftId;
  final Int64 baseRevision;
  final String summary;
  final FeatureStudioBehavior replacementBehavior;
  final FeatureStudioSource replacementSource;
}

enum FeatureStudioDiffKind { addition, removal }

enum FeatureStudioDiffArea { behavior, source }

class FeatureStudioDiffEntry {
  const FeatureStudioDiffEntry({
    required this.kind,
    required this.area,
    required this.identity,
    required this.displayLabel,
    required this.value,
  });

  final FeatureStudioDiffKind kind;
  final FeatureStudioDiffArea area;
  final String identity;
  final String displayLabel;
  final String value;
}

class FeatureStudioSuggestionDiff {
  FeatureStudioSuggestionDiff({
    required Iterable<FeatureStudioDiffEntry> entries,
  }) : entries = List.unmodifiable(entries);

  final List<FeatureStudioDiffEntry> entries;
}

class FeatureStudioDraft {
  FeatureStudioDraft({
    required this.draftId,
    required this.originatingRequest,
    required this.goal,
    required this.status,
    required this.installationId,
    required this.behavior,
    required this.source,
    required this.verification,
    required this.revision,
    required this.createdAt,
    required this.updatedAt,
    this.installationRecovery,
  }) {
    if (status == FeatureStudioDraftStatus.installed &&
        installationId == null) {
      throw ArgumentError('Invalid Draft installation identity.');
    }
    final recovery = installationRecovery;
    if (recovery == null) return;
    final draftVerification = verification;
    final historicalInstalledRecovery =
        recovery.installed &&
        draftVerification != null &&
        draftVerification.releaseDigest != recovery.verification.releaseDigest;
    if (recovery.installed != (status == FeatureStudioDraftStatus.installed) ||
        recovery.installed && installationId != recovery.installationId ||
        draftVerification == null ||
        !draftVerification.isPassing ||
        !historicalInstalledRecovery &&
            (!_sameRecoveryVerification(
                  draftVerification,
                  recovery.verification,
                ) ||
                !_sameRecoverySource(source, recovery.version.source!))) {
      throw ArgumentError('Invalid Draft installation recovery.');
    }
  }

  final String draftId;
  final FeatureStudioOriginatingRequest originatingRequest;
  final String goal;
  final FeatureStudioDraftStatus status;
  final String? installationId;
  final FeatureStudioBehavior behavior;
  final FeatureStudioSource source;
  final FeatureStudioVerification? verification;
  final Int64 revision;
  final DateTime createdAt;
  final DateTime updatedAt;
  final FeatureStudioInstallationRecovery? installationRecovery;
}

bool _validRecoveryAuthority(
  FeatureStudioVersion version,
  List<FeatureStudioGrant> grants,
  List<String> subscriptions,
) {
  if (grants.length != version.requestedCapabilityIds.length ||
      grants.length > 32 ||
      subscriptions.isEmpty ||
      subscriptions.length > 64 ||
      subscriptions.toSet().length != subscriptions.length ||
      subscriptions.any((value) => !_isRecoveryIdentity(value, 256))) {
    return false;
  }
  final expected = version.requestedCapabilityIds.toList()..sort();
  if (expected.toSet().length != expected.length) return false;
  final actual = <String>[];
  for (final grant in grants) {
    final summary = FeatureGrantConstraintPolicy.summarize(
      constraintsJson: grant.constraintsJson,
      capabilityId: grant.capabilityId,
    );
    if (grant.capabilityVersion < 1 ||
        (grant.provider == null) != (grant.connectionId == null) ||
        grant.provider != null && !_isRecoveryIdentity(grant.provider, 64) ||
        grant.connectionId != null &&
            !_isRecoveryIdentity(grant.connectionId, 256) ||
        summary == null ||
        summary != grant.constraintSummary) {
      return false;
    }
    actual.add(grant.capabilityId);
  }
  actual.sort();
  if (actual.toSet().length != actual.length) return false;
  if (expected.length != actual.length) return false;
  for (var index = 0; index < expected.length; index++) {
    if (expected[index] != actual[index]) return false;
  }
  return true;
}

bool _sameRecoverySource(FeatureStudioSource left, FeatureStudioSource right) {
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

bool _sameRecoveryVerification(
  FeatureStudioVerification left,
  FeatureStudioVerification right,
) {
  if (left.releaseDigest != right.releaseDigest ||
      left.sourceReference != right.sourceReference ||
      left.total != right.total ||
      left.passed != right.passed ||
      left.failed != right.failed ||
      left.skipped != right.skipped ||
      left.verifiedAt != right.verifiedAt ||
      left.scenarios.length != right.scenarios.length ||
      left.artifacts.length != right.artifacts.length) {
    return false;
  }
  for (var index = 0; index < left.scenarios.length; index++) {
    final a = left.scenarios[index];
    final b = right.scenarios[index];
    if (a.scenarioId != b.scenarioId ||
        a.name != b.name ||
        a.outcome != b.outcome ||
        a.safeFailure != b.safeFailure ||
        a.durationMilliseconds != b.durationMilliseconds) {
      return false;
    }
  }
  for (var index = 0; index < left.artifacts.length; index++) {
    final a = left.artifacts[index];
    final b = right.artifacts[index];
    if (a.name != b.name ||
        a.mediaType != b.mediaType ||
        a.sizeBytes != b.sizeBytes ||
        a.digest != b.digest) {
      return false;
    }
  }
  return true;
}

bool _isRecoveryIdentity(String? value, int maximumLength) =>
    value != null && _isRecoveryText(value, maximumLength);

bool _isRecoveryText(String value, int maximumLength) =>
    value.isNotEmpty &&
    value.length <= maximumLength &&
    value.trim() == value &&
    !value.runes.any(
      (character) => character < 32 || (character >= 127 && character <= 159),
    );
