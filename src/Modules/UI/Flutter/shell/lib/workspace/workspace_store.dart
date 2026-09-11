import 'dart:convert';

import 'package:flutter/foundation.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:uuid/uuid.dart';

abstract interface class WorkspacePersistence {
  Future<String?> read();
  Future<void> write(String value);
}

class PreferencesWorkspacePersistence implements WorkspacePersistence {
  PreferencesWorkspacePersistence({this.key = 'intocaht.workspace.v1'});
  final String key;
  final SharedPreferencesAsync _preferences = SharedPreferencesAsync();
  @override
  Future<String?> read() => _preferences.getString(key);
  @override
  Future<void> write(String value) => _preferences.setString(key, value);
}

class WorkspaceAgent {
  const WorkspaceAgent(this.id, this.name, this.description);
  final String id;
  final String name;
  final String description;
}

const workspaceAgents = <WorkspaceAgent>[
  WorkspaceAgent(
    'intocaht',
    'IntoCaht',
    'General assistance across your project and attached work.',
  ),
  WorkspaceAgent(
    'salesforce',
    'Salesforce Administrator',
    'Help with CRM structure, administration and diagrams. Live Salesforce access requires a connected service.',
  ),
  WorkspaceAgent(
    'leads',
    'Lead Generator',
    'Research and organize leads using the tools available to your session.',
  ),
  WorkspaceAgent(
    'automation',
    'Automation Agent',
    'Design and inspect connected scenarios. Drafting does not activate an automation.',
  ),
];

Map<String, dynamic> _map(dynamic value) =>
    value is Map ? Map<String, dynamic>.from(value) : {};
List<String> _strings(dynamic value) =>
    value is List ? value.whereType<String>().toList() : [];
String _text(dynamic value, String fallback) =>
    value is String ? value : fallback;

class WorkspaceArtifact {
  WorkspaceArtifact({
    required this.id,
    required this.title,
    required this.kind,
    this.content = '',
    Map<String, dynamic>? data,
    Map<String, dynamic>? editorState,
    Map<String, dynamic>? viewState,
  }) : data = data ?? {},
       editorState = editorState ?? viewState ?? {};
  final String id;
  String title;
  String kind;
  String content;
  Map<String, dynamic> data;
  Map<String, dynamic> editorState;
  Map<String, dynamic> get viewState => editorState;
  set viewState(Map<String, dynamic> value) => editorState = value;
  WorkspaceArtifact copyWith({
    String? title,
    String? kind,
    String? content,
    Map<String, dynamic>? data,
    Map<String, dynamic>? editorState,
    Map<String, dynamic>? viewState,
  }) => WorkspaceArtifact(
    id: id,
    title: title ?? this.title,
    kind: kind ?? this.kind,
    content: content ?? this.content,
    data: data ?? this.data,
    editorState: editorState ?? viewState ?? this.editorState,
  );
  Map<String, dynamic> toJson() => {
    'id': id,
    'title': title,
    'kind': kind,
    'content': content,
    'data': data,
    'editorState': editorState,
  };
  factory WorkspaceArtifact.fromJson(Map<String, dynamic> json) =>
      WorkspaceArtifact(
        id: json['id'] as String,
        title: _text(json['title'], 'Untitled work'),
        kind: _text(json['kind'], 'document'),
        content: _text(json['content'], ''),
        data: _map(json['data']),
        editorState: _map(json['editorState']),
      );
}

class WorkspaceConversation {
  WorkspaceConversation({
    required this.id,
    required this.title,
    this.selectedAgentId = 'intocaht',
    Set<String>? attachedArtifactIds,
    List<Map<String, dynamic>>? messages,
    this.threadId,
    this.parentRunId,
  }) : attachedArtifactIds = attachedArtifactIds ?? {},
       messages = messages ?? [];
  final String id;
  String title;
  String selectedAgentId;
  String? threadId;
  String? parentRunId;
  Set<String> attachedArtifactIds;
  List<Map<String, dynamic>> messages;
  Map<String, dynamic> toJson() => {
    'id': id,
    'title': title,
    'selectedAgentId': selectedAgentId,
    'attachedArtifactIds': attachedArtifactIds.toList(),
    'messages': messages,
    'threadId': threadId,
    'parentRunId': parentRunId,
  };
  factory WorkspaceConversation.fromJson(Map<String, dynamic> json) =>
      WorkspaceConversation(
        id: json['id'] as String,
        title: _text(json['title'], 'New conversation'),
        selectedAgentId: _text(json['selectedAgentId'], 'intocaht'),
        attachedArtifactIds: _strings(json['attachedArtifactIds']).toSet(),
        messages: (json['messages'] as List? ?? []).map(_map).toList(),
        threadId: json['threadId'] as String?,
        parentRunId: json['parentRunId'] as String?,
      );
}

class WorkspacePresentation {
  List<String> openArtifactIds = [];
  Set<String> minimizedArtifactIds = {};
  String? activeArtifactId;
  String layout = 'quiet';
  Map<String, List<double>> windowBounds = {};
  double chatWidth = 420;
  Map<String, dynamic> toJson() => {
    'openArtifactIds': openArtifactIds,
    'minimizedArtifactIds': minimizedArtifactIds.toList(),
    'activeArtifactId': activeArtifactId,
    'layout': layout,
    'windowBounds': windowBounds,
    'chatWidth': chatWidth,
  };
  WorkspacePresentation();
  factory WorkspacePresentation.fromJson(Map<String, dynamic> json) {
    final value = WorkspacePresentation();
    value.openArtifactIds = _strings(json['openArtifactIds']);
    value.minimizedArtifactIds = _strings(json['minimizedArtifactIds']).toSet();
    value.activeArtifactId = json['activeArtifactId'] as String?;
    value.layout = _text(json['layout'], 'quiet');
    value.chatWidth = (json['chatWidth'] as num? ?? 420).toDouble().clamp(
      280,
      700,
    );
    for (final entry in _map(json['windowBounds']).entries) {
      if (entry.value is List &&
          (entry.value as List).length == 4 &&
          (entry.value as List).every((v) => v is num)) {
        value.windowBounds[entry.key] = (entry.value as List)
            .map((v) => (v as num).toDouble())
            .toList();
      }
    }
    return value;
  }
}

class WorkspaceProject {
  WorkspaceProject({
    required this.id,
    required this.title,
    List<WorkspaceConversation>? conversations,
    List<WorkspaceArtifact>? artifacts,
    this.selectedConversationId,
    WorkspacePresentation? presentation,
  }) : conversations = conversations ?? [],
       artifacts = artifacts ?? [],
       presentation = presentation ?? WorkspacePresentation();
  final String id;
  String title;
  List<WorkspaceConversation> conversations;
  List<WorkspaceArtifact> artifacts;
  String? selectedConversationId;
  WorkspacePresentation presentation;
  Map<String, dynamic> toJson() => {
    'id': id,
    'title': title,
    'conversations': conversations.map((v) => v.toJson()).toList(),
    'artifacts': artifacts.map((v) => v.toJson()).toList(),
    'selectedConversationId': selectedConversationId,
    'presentation': presentation.toJson(),
  };
  factory WorkspaceProject.fromJson(Map<String, dynamic> json) =>
      WorkspaceProject(
        id: json['id'] as String,
        title: _text(json['title'], 'My project'),
        conversations: (json['conversations'] as List? ?? [])
            .map((v) => WorkspaceConversation.fromJson(_map(v)))
            .toList(),
        artifacts: (json['artifacts'] as List? ?? [])
            .map((v) => WorkspaceArtifact.fromJson(_map(v)))
            .toList(),
        selectedConversationId: json['selectedConversationId'] as String?,
        presentation: WorkspacePresentation.fromJson(
          _map(json['presentation']),
        ),
      );
}

class WorkspacePreferences {
  String displayName = '';
  String role = '';
  String theme = 'light';
  bool reducedMotion = false;
  bool compactDensity = false;
  Map<String, dynamic> toJson() => {
    'displayName': displayName,
    'role': role,
    'theme': theme,
    'reducedMotion': reducedMotion,
    'compactDensity': compactDensity,
  };
  WorkspacePreferences();
  factory WorkspacePreferences.fromJson(Map<String, dynamic> json) =>
      WorkspacePreferences()
        ..displayName = _text(json['displayName'], '')
        ..role = _text(json['role'], '')
        ..theme = _text(json['theme'], 'light')
        ..reducedMotion = json['reducedMotion'] == true
        ..compactDensity = json['compactDensity'] == true;
}

/// Local, non-secret project state. Call save after changing a mutable record.
/// Writes are serialized so an older snapshot cannot overwrite a newer one.
class WorkspaceStore extends ChangeNotifier {
  WorkspaceStore({WorkspacePersistence? persistence})
    : _persistence = persistence ?? PreferencesWorkspacePersistence() {
    _seed();
  }
  final WorkspacePersistence _persistence;
  final List<WorkspaceProject> projects = [];
  WorkspacePreferences settings = WorkspacePreferences();
  String selectedProjectId = '';
  String? persistenceError;
  bool loaded = false;
  bool _disposed = false;
  Future<void> _writes = Future.value();
  WorkspaceProject get currentProject => projects.firstWhere(
    (p) => p.id == selectedProjectId,
    orElse: () => projects.first,
  );
  WorkspaceConversation get currentConversation =>
      currentProject.conversations.firstWhere(
        (c) => c.id == currentProject.selectedConversationId,
        orElse: () => currentProject.conversations.first,
      );
  String _id() => const Uuid().v4();
  void _seed() {
    final conversation = WorkspaceConversation(
      id: _id(),
      title: 'New conversation',
    );
    final project = WorkspaceProject(
      id: _id(),
      title: 'My project',
      conversations: [conversation],
      selectedConversationId: conversation.id,
    );
    projects.add(project);
    selectedProjectId = project.id;
  }

  Future<void> load() async {
    if (loaded) return;
    try {
      final raw = await _persistence.read();
      if (raw != null) {
        final json = _map(jsonDecode(raw));
        if (json['version'] != 1 || json['projects'] is! List) {
          throw const FormatException('Unsupported workspace data');
        }
        final restored = (json['projects'] as List)
            .map((v) => WorkspaceProject.fromJson(_map(v)))
            .toList();
        for (final project in restored) {
          if (project.conversations.isEmpty) {
            project.conversations.add(
              WorkspaceConversation(id: _id(), title: 'New conversation'),
            );
          }
          final ids = project.artifacts.map((a) => a.id).toSet();
          for (final c in project.conversations) {
            c.attachedArtifactIds.retainAll(ids);
          }
          project.presentation.openArtifactIds.removeWhere(
            (id) => !ids.contains(id),
          );
          project.presentation.minimizedArtifactIds.retainAll(
            project.presentation.openArtifactIds,
          );
        }
        if (restored.isNotEmpty) {
          projects
            ..clear()
            ..addAll(restored);
        }
        selectedProjectId = _text(json['selectedProjectId'], projects.first.id);
        settings = WorkspacePreferences.fromJson(_map(json['settings']));
      }
    } catch (_) {
      persistenceError = 'Saved workspace could not be loaded. Existing saved data has not been replaced.';
    }
    loaded = true;
    _notify();
  }

  Map<String, dynamic> toJson() => {
    'version': 1,
    'projects': projects.map((p) => p.toJson()).toList(),
    'selectedProjectId': selectedProjectId,
    'settings': settings.toJson(),
  };
  void _notify() {
    if (!_disposed) notifyListeners();
  }

  Future<void> save() {
    final snapshot = jsonEncode(toJson());
    _notify();
    _writes = _writes.then((_) async {
      try {
        await _persistence.write(snapshot);
        persistenceError = null;
      } catch (_) {
        persistenceError = 'Changes could not be saved on this device. Your work remains open; retry saving.';
      }
      _notify();
    });
    return _writes;
  }

  Future<void> flush() => _writes;
  WorkspaceProject createProject(String title) {
    final c = WorkspaceConversation(id: _id(), title: 'New conversation');
    final p = WorkspaceProject(
      id: _id(),
      title: title.trim().isEmpty ? 'Untitled project' : title.trim(),
      conversations: [c],
      selectedConversationId: c.id,
    );
    projects.add(p);
    selectedProjectId = p.id;
    save();
    return p;
  }

  void selectProject(String id) {
    if (projects.any((p) => p.id == id)) {
      selectedProjectId = id;
      save();
    }
  }

  WorkspaceConversation createConversation({String? title}) {
    final c = WorkspaceConversation(
      id: _id(),
      title: title?.trim().isNotEmpty == true
          ? title!.trim()
          : 'New conversation',
    );
    currentProject.conversations.add(c);
    currentProject.selectedConversationId = c.id;
    save();
    return c;
  }

  void selectConversation(String id) {
    if (currentProject.conversations.any((c) => c.id == id)) {
      currentProject.selectedConversationId = id;
      save();
    }
  }

  void setAgent(String id) {
    if (workspaceAgents.any((a) => a.id == id)) {
      currentConversation.selectedAgentId = id;
      save();
    }
  }

  void attachArtifact(String id) {
    if (currentProject.artifacts.any((a) => a.id == id)) {
      currentConversation.attachedArtifactIds.add(id);
      save();
    }
  }

  void detachArtifact(String id) {
    currentConversation.attachedArtifactIds.remove(id);
    save();
  }

  void clearAttachments() {
    currentConversation.attachedArtifactIds.clear();
    save();
  }

  void addArtifact(WorkspaceArtifact artifact) {
    if (!currentProject.artifacts.any((a) => a.id == artifact.id)) {
      currentProject.artifacts.add(artifact);
      save();
    }
  }

  void updateArtifact(
    String id, {
    String? title,
    String? content,
    Map<String, dynamic>? data,
    Map<String, dynamic>? editorState,
  }) {
    final a = currentProject.artifacts.where((a) => a.id == id).firstOrNull;
    if (a == null) return;
    if (title != null) a.title = title;
    if (content != null) a.content = content;
    if (data != null) a.data = data;
    if (editorState != null) a.editorState = editorState;
    save();
  }

  void openArtifact(String id) {
    if (!currentProject.artifacts.any((a) => a.id == id)) return;
    final p = currentProject.presentation;
    if (!p.openArtifactIds.contains(id)) p.openArtifactIds.add(id);
    p.minimizedArtifactIds.remove(id);
    p.activeArtifactId = id;
    save();
  }

  void closeArtifact(String id) {
    final p = currentProject.presentation;
    p.openArtifactIds.remove(id);
    p.minimizedArtifactIds.remove(id);
    if (p.activeArtifactId == id) {
      p.activeArtifactId = p.openArtifactIds
          .where((id) => !p.minimizedArtifactIds.contains(id))
          .firstOrNull;
    }
    save();
  }

  void minimizeArtifact(String id) {
    final p = currentProject.presentation;
    if (p.openArtifactIds.contains(id)) p.minimizedArtifactIds.add(id);
    if (p.activeArtifactId == id) {
      p.activeArtifactId = p.openArtifactIds
          .where((id) => !p.minimizedArtifactIds.contains(id))
          .firstOrNull;
    }
    save();
  }

  void setLayout(String value) {
    currentProject.presentation.layout = value;
    save();
  }

  void setWindowBounds(String id, List<double> bounds) {
    if (bounds.length == 4 && bounds.every((v) => v.isFinite)) {
      currentProject.presentation.windowBounds[id] = List.of(bounds);
      save();
    }
  }

  void setChatWidth(double value) {
    currentProject.presentation.chatWidth = value.clamp(280, 700);
    save();
  }

  void setMessages(String conversationId, List<Map<String, dynamic>> messages) {
    for (final p in projects) {
      for (final c in p.conversations) {
        if (c.id == conversationId) {
          c.messages = messages;
          save();
          return;
        }
      }
    }
  }

  @override
  void dispose() {
    _disposed = true;
    super.dispose();
  }
}
