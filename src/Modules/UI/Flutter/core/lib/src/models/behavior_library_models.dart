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
