final class WorkspaceWindow {
  const WorkspaceWindow({
    required this.id,
    required this.title,
    required this.kind,
    required this.neuronId,
    required this.isOpen,
  });
  final String id, title, kind, neuronId;
  final bool isOpen;
  factory WorkspaceWindow.fromJson(Map<String, dynamic> json) {
    final reference = Map<String, dynamic>.from(json['reference'] as Map);
    return WorkspaceWindow(
      id: json['id'] as String,
      title: json['title'] as String,
      kind: reference['kind'] as String,
      neuronId: reference['neuronId'] as String,
      isOpen: json['isOpen'] as bool,
    );
  }
}

final class WorkspaceSnapshot {
  WorkspaceSnapshot({
    required this.revision,
    required List<WorkspaceWindow> windows,
  }) : windows = List.unmodifiable(windows);
  final int revision;
  final List<WorkspaceWindow> windows;
  factory WorkspaceSnapshot.fromJson(Map<String, dynamic> json) =>
      WorkspaceSnapshot(
        revision: json['revision'] as int,
        windows: (json['windows'] as List)
            .map(
              (w) =>
                  WorkspaceWindow.fromJson(Map<String, dynamic>.from(w as Map)),
            )
            .toList(),
      );
}

final class WorkspaceRevision {
  const WorkspaceRevision(this.workspaceId, this.revision);
  final String workspaceId;
  final int revision;
  factory WorkspaceRevision.fromJson(Map<String, dynamic> json) =>
      WorkspaceRevision(json['workspaceId'] as String, json['revision'] as int);
}
