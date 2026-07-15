import 'package:fixnum/fixnum.dart';

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

class FeatureStudioVerification {
  const FeatureStudioVerification({
    required this.releaseDigest,
    required this.total,
    required this.passed,
    required this.failed,
    required this.skipped,
    required this.verifiedAt,
  });

  final String releaseDigest;
  final int total;
  final int passed;
  final int failed;
  final int skipped;
  final DateTime verifiedAt;
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
  const FeatureStudioDraft({
    required this.draftId,
    required this.originatingRequest,
    required this.goal,
    required this.status,
    required this.behavior,
    required this.source,
    required this.verification,
    required this.revision,
    required this.createdAt,
    required this.updatedAt,
  });

  final String draftId;
  final FeatureStudioOriginatingRequest originatingRequest;
  final String goal;
  final FeatureStudioDraftStatus status;
  final FeatureStudioBehavior behavior;
  final FeatureStudioSource source;
  final FeatureStudioVerification? verification;
  final Int64 revision;
  final DateTime createdAt;
  final DateTime updatedAt;
}
