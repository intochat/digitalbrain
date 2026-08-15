import 'behavior_scenario_models.dart';

final class BehaviorRevision {
  const BehaviorRevision({
    required this.role,
    this.artifactHash,
    this.signatureHex,
    required this.status,
    required this.isActive,
  });

  final String role;
  final String? artifactHash;
  final String? signatureHex;
  final String status;
  final bool isActive;

  factory BehaviorRevision.fromJson(Map<String, Object?> json) {
    return BehaviorRevision(
      role: json['role'] as String,
      artifactHash: json['artifactHash'] as String?,
      signatureHex: json['signatureHex'] as String?,
      status: json['status'] as String,
      isActive: json['isActive'] as bool? ?? false,
    );
  }
}

final class BehaviorDocument {
  const BehaviorDocument({
    required this.behaviorId,
    required this.status,
    required this.runState,
    required this.activationGateOpen,
    this.proposedArtifactHash,
    this.activeArtifactHash,
    this.priorArtifactHash,
    this.lastCompileFailure,
    required this.testsPassed,
    required this.isApproved,
    this.lastExecutionOutcome,
    required this.programSource,
    required this.featureName,
    required this.featureText,
    required this.displayName,
    required this.description,
    required this.overview,
    this.activeSignatureHex,
    required this.activeTaskCount,
    required this.scenarios,
    required this.bindings,
    required this.revisions,
  });

  final String behaviorId;
  final String status;
  final String runState;
  final bool activationGateOpen;
  final String? proposedArtifactHash;
  final String? activeArtifactHash;
  final String? priorArtifactHash;
  final String? lastCompileFailure;
  final bool testsPassed;
  final bool isApproved;
  final String? lastExecutionOutcome;
  final String programSource;
  final String featureName;
  final String featureText;
  final String displayName;
  final String description;
  final String overview;
  final String? activeSignatureHex;
  final int activeTaskCount;
  final List<BehaviorScenario> scenarios;
  final List<BehaviorBinding> bindings;
  final List<BehaviorRevision> revisions;

  bool get isRunning => runState == 'Running' && activationGateOpen;
  bool get isStopping => runState == 'Stopping';
  bool get isStopped => runState == 'Stopped';
  bool get canStop => status == 'Active' && (isRunning || isStopping);
  bool get canStart => status == 'Active' && (isStopped || isStopping);

  factory BehaviorDocument.fromJson(Map<String, Object?> json) {
    return BehaviorDocument(
      behaviorId: json['behaviorId'] as String,
      status: json['status'] as String,
      runState: json['runState'] as String? ?? 'Idle',
      activationGateOpen: json['activationGateOpen'] as bool? ?? false,
      proposedArtifactHash: json['proposedArtifactHash'] as String?,
      activeArtifactHash: json['activeArtifactHash'] as String?,
      priorArtifactHash: json['priorArtifactHash'] as String?,
      lastCompileFailure: json['lastCompileFailure'] as String?,
      testsPassed: json['testsPassed'] as bool? ?? false,
      isApproved: json['isApproved'] as bool? ?? false,
      lastExecutionOutcome: json['lastExecutionOutcome'] as String?,
      programSource: json['programSource'] as String? ?? '',
      featureName: json['featureName'] as String? ?? 'install',
      featureText: json['featureText'] as String? ?? '',
      displayName: json['displayName'] as String? ?? '',
      description: json['description'] as String? ?? '',
      overview: json['overview'] as String? ?? '',
      activeSignatureHex: json['activeSignatureHex'] as String?,
      activeTaskCount: (json['activeTaskCount'] as num?)?.toInt() ?? 0,
      scenarios: (json['scenarios'] as List<Object?>? ?? const [])
          .map(
            (item) => BehaviorScenario.fromJson(
              Map<String, Object?>.from(item! as Map),
            ),
          )
          .toList(growable: false),
      bindings: (json['bindings'] as List<Object?>? ?? const [])
          .map(
            (item) => BehaviorBinding.fromJson(
              Map<String, Object?>.from(item! as Map),
            ),
          )
          .toList(growable: false),
      revisions: (json['revisions'] as List<Object?>? ?? const [])
          .map(
            (item) => BehaviorRevision.fromJson(
              Map<String, Object?>.from(item! as Map),
            ),
          )
          .toList(growable: false),
    );
  }
}

final class BehaviorChangeProposal {
  const BehaviorChangeProposal({
    required this.proposalId,
    required this.behaviorId,
    required this.requestText,
    required this.proposedFeatureText,
    required this.proposedFeatureName,
    required this.status,
    this.diffSummary,
  });

  final String proposalId;
  final String behaviorId;
  final String requestText;
  final String proposedFeatureText;
  final String proposedFeatureName;
  final String status;
  final String? diffSummary;

  bool get awaitsScenarioApproval => status == 'awaiting-scenario-approval';

  factory BehaviorChangeProposal.fromJson(Map<String, Object?> json) {
    return BehaviorChangeProposal(
      proposalId: json['proposalId'] as String,
      behaviorId: json['behaviorId'] as String,
      requestText: json['requestText'] as String,
      proposedFeatureText: json['proposedFeatureText'] as String,
      proposedFeatureName: json['proposedFeatureName'] as String,
      status: json['status'] as String,
      diffSummary: json['diffSummary'] as String?,
    );
  }
}

final class BehaviorRunOnceResult {
  const BehaviorRunOnceResult({
    required this.succeeded,
    required this.outcome,
    required this.document,
  });

  final bool succeeded;
  final String outcome;
  final BehaviorDocument document;

  factory BehaviorRunOnceResult.fromJson(Map<String, Object?> json) {
    return BehaviorRunOnceResult(
      succeeded: json['succeeded'] as bool? ?? false,
      outcome: json['outcome'] as String? ?? '',
      document: BehaviorDocument.fromJson(
        Map<String, Object?>.from(json['document']! as Map),
      ),
    );
  }
}
