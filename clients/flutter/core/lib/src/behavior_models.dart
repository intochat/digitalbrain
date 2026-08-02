final class BehaviorLibraryItem {
  const BehaviorLibraryItem({
    required this.behaviorId,
    required this.displayName,
    required this.description,
    required this.status,
    required this.runState,
    required this.activationGateOpen,
    this.activeArtifactHash,
    required this.overview,
    required this.scenarioTitles,
    required this.health,
  });

  final String behaviorId;
  final String displayName;
  final String description;
  final String status;
  final String runState;
  final bool activationGateOpen;
  final String? activeArtifactHash;
  final String overview;
  final List<String> scenarioTitles;
  final String health;

  bool get isRunning => runState == 'Running' && activationGateOpen;
  bool get isStopped => runState == 'Stopped' || runState == 'Stopping';
  bool get isDraft => status == 'Empty' || health == 'draft';

  factory BehaviorLibraryItem.fromJson(Map<String, Object?> json) {
    return BehaviorLibraryItem(
      behaviorId: json['behaviorId'] as String,
      displayName: json['displayName'] as String,
      description: json['description'] as String,
      status: json['status'] as String,
      runState: json['runState'] as String,
      activationGateOpen: json['activationGateOpen'] as bool? ?? false,
      activeArtifactHash: json['activeArtifactHash'] as String?,
      overview: json['overview'] as String? ?? '',
      scenarioTitles: (json['scenarioTitles'] as List<Object?>? ?? const [])
          .map((item) => item as String)
          .toList(growable: false),
      health: json['health'] as String? ?? 'pending',
    );
  }
}

final class BehaviorLibraryDocument {
  const BehaviorLibraryDocument({required this.items});

  final List<BehaviorLibraryItem> items;

  factory BehaviorLibraryDocument.fromJson(Map<String, Object?> json) {
    return BehaviorLibraryDocument(
      items: (json['items'] as List<Object?>? ?? const [])
          .map(
            (item) => BehaviorLibraryItem.fromJson(
              Map<String, Object?>.from(item! as Map),
            ),
          )
          .toList(growable: false),
    );
  }
}

final class BehaviorScenario {
  const BehaviorScenario({
    required this.scenarioId,
    required this.title,
    required this.bindingKey,
    this.passed,
    this.detail,
  });

  final String scenarioId;
  final String title;
  final String bindingKey;
  final bool? passed;
  final String? detail;

  factory BehaviorScenario.fromJson(Map<String, Object?> json) {
    return BehaviorScenario(
      scenarioId: json['scenarioId'] as String,
      title: json['title'] as String,
      bindingKey: json['bindingKey'] as String,
      passed: json['passed'] as bool?,
      detail: json['detail'] as String?,
    );
  }
}

final class BehaviorBinding {
  const BehaviorBinding({
    required this.bindingId,
    required this.sourceModule,
    required this.sourceSynapse,
    required this.targetCase,
    required this.contractVersion,
    required this.enabled,
    required this.configurationHint,
  });

  final String bindingId;
  final String sourceModule;
  final String sourceSynapse;
  final String targetCase;
  final String contractVersion;
  final bool enabled;
  final String configurationHint;

  factory BehaviorBinding.fromJson(Map<String, Object?> json) {
    return BehaviorBinding(
      bindingId: json['bindingId'] as String,
      sourceModule: json['sourceModule'] as String,
      sourceSynapse: json['sourceSynapse'] as String,
      targetCase: json['targetCase'] as String,
      contractVersion: json['contractVersion'] as String,
      enabled: json['enabled'] as bool? ?? true,
      configurationHint: json['configurationHint'] as String? ?? 'opaque',
    );
  }
}

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

final class BehaviorEvent {
  const BehaviorEvent({
    required this.sequence,
    required this.kind,
    required this.behaviorId,
    required this.commandId,
    this.artifactHash,
    this.detail,
    required this.timestamp,
  });

  final int sequence;
  final String kind;
  final String behaviorId;
  final String commandId;
  final String? artifactHash;
  final String? detail;
  final DateTime timestamp;

  factory BehaviorEvent.fromJson(Map<String, Object?> json) {
    return BehaviorEvent(
      sequence: (json['sequence'] as num).toInt(),
      kind: json['kind'] as String,
      behaviorId: json['behaviorId'] as String,
      commandId: json['commandId'] as String,
      artifactHash: json['artifactHash'] as String?,
      detail: json['detail'] as String?,
      timestamp: DateTime.parse(json['timestamp'] as String).toUtc(),
    );
  }
}

/// Module-owned user action projected for Flutter cards — never secrets.
final class UserActionRequiredEvent {
  const UserActionRequiredEvent({
    required this.sequence,
    required this.taskId,
    required this.attemptId,
    required this.moduleId,
    required this.displayText,
    required this.actionUrl,
    required this.expiresAt,
    required this.timestamp,
  });

  final int sequence;
  final String taskId;
  final String attemptId;
  final String moduleId;
  final String displayText;
  final Uri actionUrl;
  final DateTime expiresAt;
  final DateTime timestamp;

  factory UserActionRequiredEvent.fromJson(Map<String, Object?> json) {
    return UserActionRequiredEvent(
      sequence: (json['sequence'] as num).toInt(),
      taskId: json['taskId'] as String,
      attemptId: json['attemptId'] as String,
      moduleId: json['moduleId'] as String,
      displayText: json['displayText'] as String,
      actionUrl: Uri.parse(json['actionUrl'] as String),
      expiresAt: DateTime.parse(json['expiresAt'] as String).toUtc(),
      timestamp: DateTime.parse(json['timestamp'] as String).toUtc(),
    );
  }
}
