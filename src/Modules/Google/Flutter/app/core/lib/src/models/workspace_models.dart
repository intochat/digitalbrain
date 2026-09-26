final class WorkspaceWindow {
  const WorkspaceWindow({
    required this.id,
    required this.title,
    required this.tableId,
    required this.isOpen,
    this.surface,
  });
  final String id, title, tableId;
  final bool isOpen;
  final Map<String, dynamic>? surface;
  factory WorkspaceWindow.fromJson(Map<String, dynamic> json) =>
      WorkspaceWindow(
        id: json['id'] as String,
        title: json['title'] as String,
        tableId: (json['view'] as Map)['id'] as String,
        isOpen: json['isOpen'] as bool,
        surface: json['surface'] == null
            ? null
            : Map<String, dynamic>.from(json['surface'] as Map),
      );
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
