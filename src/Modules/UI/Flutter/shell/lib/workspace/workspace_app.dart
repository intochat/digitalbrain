import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:file_picker/file_picker.dart';

import '../chat/agent_chat_app.dart';
import '../chat/brain_graph_store.dart';
import 'workspace_table_import.dart';
import 'workspace_store.dart';
import 'workspace_settings.dart';
import 'workspace_routes.dart';
import 'workspace_brain_observation.dart';
import 'workspace_chat.dart';
import 'artifact_editors.dart';

class WorkspaceApp extends StatefulWidget {
  const WorkspaceApp({
    super.key,
    this.store,
    this.persistenceKey = 'intocaht.workspace.v1',
    this.onRun,
    this.onOpenUrl,
    this.kernelBaseUri,
    this.statusMessage,
    this.onReadTable,
    this.onUpdateTableView,
    this.onListTables,
    this.onTranscribe,
    this.onCreateArtifact,
    this.onReadArtifact,
    this.onUpdateArtifact,
    this.onListArtifacts,
    this.onCreateTable,
    this.onReadBrain,
    this.onWatchBrain,
  });
  final WorkspaceStore? store;
  final String persistenceKey;
  final AgentRunner? onRun;
  final Future<void> Function(Uri)? onOpenUrl;
  final Uri? kernelBaseUri;
  final String? statusMessage;
  final ReadTable? onReadTable;
  final UpdateTableView? onUpdateTableView;
  final ListTables? onListTables;
  final ReadBrain? onReadBrain;
  final WatchBrain? onWatchBrain;
  final Future<String> Function(Uint8List, String)? onTranscribe;
  final Future<Map<String, dynamic>> Function({
    required String kind,
    required String title,
    required Map<String, dynamic> content,
  })?
  onCreateArtifact;
  final Future<Map<String, dynamic>> Function(String id)? onReadArtifact;
  final Future<Map<String, dynamic>> Function(
    String id, {
    required int expectedRevision,
    required String title,
    required Map<String, dynamic> content,
  })?
  onUpdateArtifact;
  final Future<List<Map<String, dynamic>>> Function()? onListArtifacts;
  final Future<TableSnapshot> Function({
    required String title,
    required List<TableColumn> columns,
    required List<TableRowData> rows,
  })?
  onCreateTable;
  @override
  State<WorkspaceApp> createState() => _WorkspaceAppState();
}

class _WorkspaceAppState extends State<WorkspaceApp> {
  late final WorkspaceStore store =
      widget.store ??
      WorkspaceStore(
        persistence: PreferencesWorkspacePersistence(
          key: widget.persistenceKey,
        ),
      );
  final _tables = <String, UiTableController>{};
  final _hydratingTables = <String>{};
  final _tableErrors = <String, String>{};
  BrainGraphStore? _graph;
  BrainGraphStore _liveGraph({bool refresh = false}) {
    if (_graph == null ||
        (_graph!.read == null && widget.onReadBrain != null)) {
      _graph?.dispose();
      _graph = BrainGraphStore(
        read: widget.onReadBrain,
        watch: widget.onWatchBrain,
      );
      _graph!.addListener(_publishLiveObservation);
    } else if (refresh) {
      unawaited(_graph!.refresh());
    }
    return _graph!;
  }

  void _publishLiveObservation() {
    if (!mounted) return;
    final graph = _graph;
    if (graph == null) return;
    final snapshot = graph.snapshot;
    for (final project in store.projects) {
      for (final artifact in project.artifacts) {
        if (artifact.data['_live'] != true) continue;
        artifact.data = {
          ...artifact.data,
          if (snapshot != null) ...workspaceBrainObservation(snapshot),
          '_observationStale':
              graph.stale || graph.failure != null || snapshot == null,
          '_observationFailure': graph.failure,
        }..remove('_dirty');
      }
    }
    store.save();
  }

  final _messenger = GlobalKey<ScaffoldMessengerState>();
  final _navigator = GlobalKey<NavigatorState>();
  late final WorkspaceRouteBridge _routes;
  final _saveTimers = <String, Timer>{};
  final _saving = <String>{};
  final _saveErrors = <String, String>{};
  bool _ready = false, _directory = true, _home = false, _mobileWork = false;
  String _query = '';
  @override
  void initState() {
    super.initState();
    _routes = WorkspaceRouteBridge(onNavigate: _navigateRoute);
    store.addListener(_changed);
    _load();
  }

  Future<void> _load() async {
    await store.load();
    if (!mounted) return;
    var cleaned = false;
    var hasLive = false;
    for (final project in store.projects) {
      for (final artifact in project.artifacts) {
        if (artifact.data['_live'] == true) {
          hasLive = true;
          artifact.data['_observationStale'] = true;
        }
        if (artifact.data['_live'] == true &&
            artifact.data.remove('_dirty') != null) {
          cleaned = true;
        }
      }
    }
    if (cleaned) {
      await store.save();
    }
    if (!mounted) return;
    setState(() => _ready = true);
    if (hasLive) {
      _liveGraph();
    }
    _navigateRoute(
      Uri.parse(WidgetsBinding.instance.platformDispatcher.defaultRouteName),
    );
  }

  void _navigateRoute(Uri uri) {
    if (!_ready || !mounted) return;
    final segments = uri.pathSegments;
    _navigator.currentState?.popUntil((route) => route.isFirst);
    if (segments.firstOrNull == 'settings') {
      void showSettings() {
        if (!mounted) return;
        final section = segments.length > 1 ? segments[1] : 'profile';
        _navigator.currentState?.push(
          MaterialPageRoute<void>(
            settings: RouteSettings(name: '/settings/$section'),
            builder: (_) => WorkspaceSettings(
              store: store,
              initialSection: section,
              kernelBaseUri: widget.kernelBaseUri,
              onOpen: widget.onOpenUrl,
              onClose: () => _navigator.currentState?.pop(),
            ),
          ),
        );
        if (segments.length > 2 && segments[2] == 'gallery') {
          _navigator.currentState?.push(
            MaterialPageRoute<void>(
              settings: const RouteSettings(
                name: '/settings/developer/gallery',
              ),
              builder: (_) => Scaffold(
                appBar: AppBar(title: const Text('UI components gallery')),
                body: const UiGalleryScreen(),
              ),
            ),
          );
        }
      }

      if (_navigator.currentState != null) {
        showSettings();
      } else {
        WidgetsBinding.instance.addPostFrameCallback((_) => showSettings());
      }
      return;
    }
    setState(() {
      final id = segments.length > 1 ? segments[1] : null;
      if (segments.firstOrNull == 'projects' &&
          id != null &&
          store.projects.any((project) => project.id == id)) {
        store.selectProject(id);
        _directory = false;
        _home = segments.length < 3 || segments[2] != 'workspace';
      } else {
        _directory = true;
      }
    });
  }

  void _changed() {
    if (mounted) setState(() {});
  }

  @override
  void dispose() {
    _routes.dispose();
    _graph?.dispose();
    for (final timer in _saveTimers.values) {
      timer.cancel();
    }
    store.removeListener(_changed);
    for (final t in _tables.values) {
      t.dispose();
    }
    if (widget.store == null) store.dispose();
    super.dispose();
  }

  void _accept(Map<String, dynamic> result, {WorkspaceProject? project}) {
    final destination = project ?? store.currentProject;
    final id = result['id'] as String?;
    if (id == null) return;
    if (result['kind'] == 'table') {
      try {
        final snapshot = TableSnapshot.fromJson(result);
        final existing = _tables[id];
        if (existing == null) {
          _tables[id] = UiTableController(
            snapshot: snapshot,
            read: widget.onReadTable,
            update: widget.onUpdateTableView,
          );
          _tables[id]!.addListener(() {
            final snapshot = _tables[id]!.snapshot;
            for (final project in store.projects) {
              for (final artifact in project.artifacts.where(
                (a) => a.id == id,
              )) {
                artifact.data = {
                  ...snapshot.toJson(),
                  '_revision': snapshot.revision,
                };
                artifact.editorState = {
                  ...artifact.editorState,
                  'filters': snapshot.filters.map((f) => f.toJson()).toList(),
                  'sort': snapshot.sort?.toJson(),
                  'visibleColumns': snapshot.visibleColumns,
                  'filteredRows': snapshot.filteredRows,
                };
              }
            }
            store.save();
          });
        } else {
          existing.accept(snapshot);
        }
        result = _tables[id]!.snapshot.toJson();
        _tableErrors.remove(id);
      } catch (_) {
        return;
      }
    }
    final content = result['content'] is Map
        ? Map<String, dynamic>.from(result['content'] as Map)
        : result;
    final artifact = WorkspaceArtifact(
      id: id,
      title: result['title'] as String? ?? 'Untitled work',
      kind: result['kind'] as String? ?? 'document',
      content: content['source'] as String? ?? '',
      data: {
        ...content,
        if (result['revision'] != null) '_revision': result['revision'],
        if (result['kind'] != 'table') '_remote': true,
      },
      editorState: content['editorState'] is Map
          ? Map<String, dynamic>.from(content['editorState'] as Map)
          : result['kind'] == 'table'
          ? {
              'filters': result['filters'],
              'sort': result['sort'],
              'filteredRows': result['filteredRows'],
              'visibleColumns': result['visibleColumns'],
            }
          : null,
    );
    final existing = destination.artifacts.where((a) => a.id == id).firstOrNull;
    if (existing == null) {
      destination.artifacts.add(artifact);
      store.save();
    } else if (existing.data['_dirty'] != true) {
      if ((existing.data['_revision'] as num? ?? 0) <=
          (artifact.data['_revision'] as num? ?? 0)) {
        existing.title = artifact.title;
        existing.content = artifact.content;
        existing.data = artifact.data;
        if (artifact.editorState.isNotEmpty) {
          existing.editorState = {
            ...existing.editorState,
            ...artifact.editorState,
          };
        }
        store.save();
      }
    }
    if (destination.id == store.currentProject.id) store.openArtifact(id);
  }

  void _editArtifact(WorkspaceArtifact artifact) {
    final project = store.currentProject;
    final edited = project.artifacts.firstWhere((a) => a.id == artifact.id);
    edited.content = artifact.content;
    edited.editorState = artifact.editorState;
    edited.data = {
      ...artifact.data,
      if (artifact.kind != 'table' && artifact.data['_remote'] == true)
        '_dirty': true,
    };
    if (artifact.data['_live'] == true) {
      edited.data.remove('_dirty');
    }
    store.save();
    if (artifact.kind == 'table' || artifact.data['_remote'] != true) return;
    _saveTimers[artifact.id]?.cancel();
    _saveTimers[artifact.id] = Timer(
      const Duration(milliseconds: 600),
      () => _saveArtifact(edited),
    );
  }

  Future<void> _saveArtifact(WorkspaceArtifact artifact) async {
    final update = widget.onUpdateArtifact;
    if (update == null || artifact.data['_remote'] != true) return;
    if (_saving.contains(artifact.id)) {
      _saveTimers[artifact.id] = Timer(
        const Duration(milliseconds: 600),
        () => _saveArtifact(artifact),
      );
      return;
    }
    final payload = {...artifact.data}
      ..removeWhere((key, _) => key.startsWith('_'));
    payload['editorState'] = {...artifact.editorState};
    if (artifact.content.isNotEmpty) payload['source'] = artifact.content;
    final fingerprint = jsonEncode(payload);
    _saving.add(artifact.id);
    if (mounted) setState(() {});
    try {
      final saved = await update(
        artifact.id,
        expectedRevision: (artifact.data['_revision'] as num? ?? 0).toInt(),
        title: artifact.title,
        content: payload,
      );
      artifact.data['_revision'] = saved['revision'];
      final current = {...artifact.data}
        ..removeWhere((key, _) => key.startsWith('_'));
      current['editorState'] = {...artifact.editorState};
      if (artifact.content.isNotEmpty) current['source'] = artifact.content;
      if (jsonEncode(current) == fingerprint) artifact.data.remove('_dirty');
      _saveErrors.remove(artifact.id);
      store.save();
    } catch (e) {
      _saveErrors[artifact.id] =
          'Changes saved locally; remote save failed. $e';
    } finally {
      _saving.remove(artifact.id);
      if (mounted) setState(() {});
    }
  }

  Future<void> _resolveSave(
    WorkspaceArtifact artifact, {
    required bool keepLocal,
  }) async {
    final destination = store.projects
        .where((p) => p.artifacts.contains(artifact))
        .firstOrNull;
    final read = widget.onReadArtifact;
    if (read == null) return;
    _saveTimers[artifact.id]?.cancel();
    try {
      final latest = await read(artifact.id);
      if (!mounted) return;
      if (keepLocal) {
        artifact.data['_revision'] = latest['revision'];
        await _saveArtifact(artifact);
      } else {
        artifact.data.remove('_dirty');
        _saveErrors.remove(artifact.id);
        _accept(latest, project: destination);
      }
    } catch (e) {
      if (mounted) {
        setState(
          () => _saveErrors[artifact.id] =
              'Could not read the latest version: $e',
        );
      }
    }
  }

  Future<void> _open(WorkspaceArtifact a) async {
    final destination = store.currentProject;
    store.openArtifact(a.id);
    if (a.kind != 'table' &&
        a.data['_live'] != true &&
        a.data['_dirty'] != true &&
        widget.onReadArtifact != null) {
      final projectId = store.currentProject.id;
      try {
        final value = await widget.onReadArtifact!(a.id);
        if (mounted && store.currentProject.id == projectId) _accept(value);
      } catch (e) {
        _messenger.currentState?.showSnackBar(
          SnackBar(content: Text('Could not refresh saved work: $e')),
        );
      }
    }
    if (a.kind == 'table' && !_tables.containsKey(a.id)) {
      if (widget.onReadTable != null) {
        try {
          final snapshot = await widget.onReadTable!(a.id);
          if (mounted) _accept(snapshot.toJson(), project: destination);
        } catch (e) {
          _messenger.currentState?.showSnackBar(
            SnackBar(content: Text('Could not open table: $e')),
          );
        }
      } else {
        try {
          _accept(a.data);
        } catch (_) {}
      }
    }
  }

  Future<void> _newProject(BuildContext context) async {
    final input = TextEditingController();
    final title = await showDialog<String>(
      context: context,
      builder: (c) => AlertDialog(
        title: const Text('New project'),
        content: TextField(
          controller: input,
          autofocus: true,
          decoration: const InputDecoration(labelText: 'Project name'),
          onSubmitted: (v) => Navigator.pop(c, v),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(c),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(c, input.text),
            child: const Text('Create project'),
          ),
        ],
      ),
    );
    // The dialog route may still be reversing its transition at this point.
    Future<void>.delayed(const Duration(milliseconds: 400), input.dispose);
    if (title != null && title.trim().isNotEmpty) {
      store.createProject(title.trim());
      setState(() {
        _directory = false;
        _home = true;
      });
    }
  }

  Future<void> _newWork(BuildContext context, String kind) async {
    final destination = store.currentProject;
    if (kind == 'table') {
      if (widget.onCreateTable != null) {
        final table = await importWorkspaceTable(
          context,
          widget.onCreateTable!,
        );
        if (table != null && mounted) {
          _accept(table.toJson(), project: destination);
          setState(() => _home = false);
        }
      }
      return;
    }
    if (kind == 'liveBrain') {
      _liveGraph(refresh: true);
      final id = 'live-brain-${store.currentProject.id}';
      store.addArtifact(
        WorkspaceArtifact(
          id: id,
          title: 'Live brain',
          kind: 'brain',
          data: {'_live': true, 'live': true},
        ),
      );
      store.openArtifact(id);
      _publishLiveObservation();
      setState(() => _home = false);
      return;
    }
    final create = widget.onCreateArtifact;
    if (create == null) return;
    try {
      Map<String, dynamic> content = {};
      String title = kind == 'diagram'
          ? 'Untitled drawing'
          : kind == 'brain'
          ? 'Untitled scenario'
          : 'Untitled image';
      if (kind == 'diagram') {
        content = {'source': createWorkspaceDrawingSource()};
      } else if (kind == 'image') {
        final picked = await FilePicker.platform.pickFiles(
          type: FileType.image,
          withData: true,
        );
        if (picked == null) return;
        final file = picked.files.single;
        final bytes = file.bytes;
        if (bytes == null) throw StateError('Image bytes could not be read.');
        title = file.name;
        content = {
          'originalDataUrl':
              'data:image/${file.extension ?? 'png'};base64,${base64Encode(bytes)}',
          'fileName': file.name,
        };
      } else if (kind == 'brain') {
        content = {
          'rootId': 'draft',
          'observedAt': DateTime.now().toUtc().toIso8601String(),
          'scope': 'Draft scenario · not activated',
          'nodes': [],
          'synapses': [],
        };
      }
      final result = await create(kind: kind, title: title, content: content);
      if (mounted) {
        _accept(result, project: destination);
        if (store.currentProject.id == destination.id) {
          setState(() => _home = false);
        }
      }
    } catch (e) {
      _messenger.currentState?.showSnackBar(
        SnackBar(content: Text('Could not create work: $e')),
      );
    }
  }

  Widget _newWorkMenu(BuildContext context) => PopupMenuButton<String>(
    tooltip: 'New work',
    onSelected: (kind) => _newWork(context, kind),
    itemBuilder: (_) => [
      for (final entry in const {
        'diagram': 'Drawing',
        'brain': 'Brain scenario',
        'image': 'Import image',
        'table': 'Import table (CSV, TSV, XLSX)',
        'liveBrain': 'Live brain',
      }.entries)
        PopupMenuItem(
          value: entry.key,
          enabled: entry.key == 'table'
              ? widget.onCreateTable != null
              : entry.key == 'liveBrain'
              ? widget.onReadBrain != null
              : widget.onCreateArtifact != null,
          child: Text(entry.value),
        ),
    ],
    child: const Padding(
      padding: EdgeInsets.all(12),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(Icons.add, size: 18),
          SizedBox(width: 5),
          Text('New work'),
        ],
      ),
    ),
  );

  void _attach(BuildContext context) {
    showDialog<void>(
      context: context,
      builder: (c) => ListenableBuilder(
        listenable: store,
        builder: (c, _) => AlertDialog(
          title: const Text('Attach project work'),
          content: SizedBox(
            width: 440,
            child: ListView(
              shrinkWrap: true,
              children: [
                if (store.currentProject.artifacts.isEmpty)
                  const Padding(
                    padding: EdgeInsets.all(16),
                    child: Text('Create or import work in this project first.'),
                  ),
                for (final a in store.currentProject.artifacts)
                  CheckboxListTile(
                    title: Text(a.title),
                    subtitle: Text(
                      a.kind == 'table'
                          ? '${(a.editorState['filters'] as List? ?? []).length} filters · ${(a.editorState['selectedRowIds'] as List? ?? []).length} selected rows'
                          : a.kind,
                    ),
                    value: store.currentConversation.attachedArtifactIds
                        .contains(a.id),
                    onChanged: (value) => value == true
                        ? store.attachArtifact(a.id)
                        : store.detachArtifact(a.id),
                  ),
              ],
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(c),
              child: const Text('Done'),
            ),
          ],
        ),
      ),
    );
  }

  Widget _projects(BuildContext context) => ListView(
    padding: const EdgeInsets.all(32),
    children: [
      Row(
        children: [
          const Expanded(
            child: Text(
              'Projects',
              style: TextStyle(fontSize: 28, fontWeight: FontWeight.w600),
            ),
          ),
          FilledButton.icon(
            onPressed: () => _newProject(context),
            icon: const Icon(Icons.add),
            label: const Text('New project'),
          ),
        ],
      ),
      const SizedBox(height: 24),
      for (final p in store.projects)
        Card(
          child: ListTile(
            contentPadding: const EdgeInsets.all(20),
            leading: const Icon(Icons.folder_outlined),
            title: Text(p.title),
            subtitle: Text(
              '${p.conversations.length} conversations · ${p.artifacts.length} saved items',
            ),
            trailing: const Icon(Icons.arrow_forward),
            onTap: () {
              store.selectProject(p.id);
              setState(() {
                _directory = false;
                _home = true;
              });
            },
          ),
        ),
    ],
  );
  Widget _projectHome(BuildContext context) => LayoutBuilder(
    builder: (context, constraints) {
      final conversations = Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Expanded(
                child: Text(
                  'Conversations',
                  style: TextStyle(fontSize: 19, fontWeight: FontWeight.w600),
                ),
              ),
              TextButton.icon(
                onPressed: () {
                  store.createConversation();
                  setState(() => _home = false);
                },
                icon: const Icon(Icons.add, size: 18),
                label: const Text('New conversation'),
              ),
            ],
          ),
          const Divider(height: 24),
          for (final conversation in store.currentProject.conversations)
            ListTile(
              contentPadding: EdgeInsets.zero,
              leading: const Icon(Icons.chat_bubble_outline, size: 20),
              title: Text(conversation.title),
              subtitle: Text(
                workspaceAgents
                        .where((a) => a.id == conversation.selectedAgentId)
                        .firstOrNull
                        ?.name ??
                    'IntoCaht',
              ),
              trailing: const Icon(Icons.chevron_right, size: 18),
              onTap: () {
                store.selectConversation(conversation.id);
                setState(() => _home = false);
              },
            ),
        ],
      );
      final work = Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Expanded(
                child: Text(
                  'Work',
                  style: TextStyle(fontSize: 19, fontWeight: FontWeight.w600),
                ),
              ),
              _newWorkMenu(context),
              if (widget.onListTables != null)
                IconButton(
                  tooltip: 'Import saved work',
                  onPressed: () => _import(context),
                  icon: const Icon(Icons.download_outlined, size: 19),
                ),
            ],
          ),
          const Divider(height: 24),
          for (final artifact in store.currentProject.artifacts)
            ListTile(
              contentPadding: EdgeInsets.zero,
              leading: Icon(_icon(artifact.kind), size: 20),
              title: Text(artifact.title),
              subtitle: Text(artifact.kind),
              trailing: IconButton(
                tooltip: 'Attach to conversation',
                onPressed: () => store.attachArtifact(artifact.id),
                icon: const Icon(Icons.attach_file, size: 18),
              ),
              onTap: () {
                _open(artifact);
                setState(() => _home = false);
              },
            ),
          if (store.currentProject.artifacts.isEmpty)
            const Padding(
              padding: EdgeInsets.symmetric(vertical: 20),
              child: Text('Work created in conversations appears here.'),
            ),
        ],
      );
      return SingleChildScrollView(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 1150),
            child: Padding(
              padding: EdgeInsets.all(constraints.maxWidth < 700 ? 20 : 40),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    store.currentProject.title,
                    style: const TextStyle(
                      fontSize: 28,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  const SizedBox(height: 36),
                  if (constraints.maxWidth < 800) ...[
                    conversations,
                    const SizedBox(height: 32),
                    work,
                  ] else
                    Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Expanded(child: conversations),
                        const SizedBox(width: 56),
                        Expanded(child: work),
                      ],
                    ),
                ],
              ),
            ),
          ),
        ),
      );
    },
  );
  Future<void> _import(BuildContext context) async {
    final project = store.currentProject;
    try {
      final results = await Future.wait<Object>([
        if (widget.onListTables != null) widget.onListTables!(),
        if (widget.onListArtifacts != null) widget.onListArtifacts!(),
      ]);
      final choices = <Map<String, dynamic>>[];
      for (final result in results) {
        if (result is List<TableSummary>) {
          choices.addAll(
            result.map((t) => {'id': t.id, 'title': t.title, 'kind': 'table'}),
          );
        } else if (result is List<Map<String, dynamic>>) {
          choices.addAll(result);
        }
      }
      if (!context.mounted) return;
      final selected = await showDialog<Map<String, dynamic>>(
        context: context,
        builder: (c) => AlertDialog(
          title: const Text('Import saved work'),
          content: SizedBox(
            width: 420,
            child: ListView(
              shrinkWrap: true,
              children: [
                for (final item in choices)
                  ListTile(
                    leading: Icon(_icon(item['kind'] as String? ?? '')),
                    title: Text(item['title'] as String? ?? 'Untitled'),
                    onTap: () => Navigator.pop(c, item),
                  ),
                if (choices.isEmpty) const Text('No saved work yet.'),
              ],
            ),
          ),
        ),
      );
      if (selected == null) return;
      final id = selected['id'] as String;
      final snapshot = selected['kind'] == 'table'
          ? await widget.onReadTable!(id).then((t) => t.toJson())
          : await widget.onReadArtifact!(id);
      if (mounted) _accept(snapshot, project: project);
    } catch (e) {
      _messenger.currentState?.showSnackBar(
        SnackBar(content: Text('Could not load saved work: $e')),
      );
    }
  }

  IconData _icon(String kind) => switch (kind) {
    'table' => Icons.table_chart_outlined,
    'image' => Icons.image_outlined,
    'brain' => Icons.hub_outlined,
    'diagram' => Icons.draw_outlined,
    _ => Icons.description_outlined,
  };
  Future<void> _hydrateTable(
    WorkspaceArtifact artifact,
    WorkspaceProject project,
  ) async {
    try {
      final snapshot = widget.onReadTable == null
          ? TableSnapshot.fromJson(artifact.data)
          : await widget.onReadTable!(artifact.id);
      if (mounted) _accept(snapshot.toJson(), project: project);
    } catch (e) {
      _tableErrors[artifact.id] = 'Could not load table: $e';
    } finally {
      _hydratingTables.remove(artifact.id);
      if (mounted) setState(() {});
    }
  }

  Widget _editor(WorkspaceArtifact a) {
    if (a.kind == 'table' && _tables[a.id] == null) {
      if (!_hydratingTables.contains(a.id) && !_tableErrors.containsKey(a.id)) {
        _hydratingTables.add(a.id);
        final project = store.currentProject;
        WidgetsBinding.instance.addPostFrameCallback((_) {
          if (mounted) _hydrateTable(a, project);
        });
      }
      return Center(
        child: _tableErrors[a.id] == null
            ? const CircularProgressIndicator()
            : Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(_tableErrors[a.id]!),
                  TextButton(
                    onPressed: () {
                      _tableErrors.remove(a.id);
                      setState(() {});
                    },
                    child: const Text('Retry'),
                  ),
                ],
              ),
      );
    }
    return WorkspaceArtifactEditor(
      key: ValueKey(a.id),
      artifact: a,
      tableController: _tables[a.id],
      graph: a.data['_live'] == true ? _liveGraph() : null,
      onChanged: _editArtifact,
    );
  }

  Widget _pane(WorkspaceArtifact a, {bool chrome = true}) => Card(
    margin: const EdgeInsets.all(6),
    clipBehavior: Clip.antiAlias,
    child: Column(
      children: [
        if (a.data['_dirty'] == true)
          MaterialBanner(
            content: Text(
              _saveErrors[a.id] ??
                  (_saving.contains(a.id)
                      ? 'Saving changes…'
                      : 'Changes saved locally · awaiting sync'),
            ),
            actions: [
              if (_saveErrors.containsKey(a.id) &&
                  widget.onReadArtifact != null) ...[
                TextButton(
                  onPressed: () => _resolveSave(a, keepLocal: false),
                  child: const Text('Reload server version'),
                ),
                TextButton(
                  onPressed: () => _resolveSave(a, keepLocal: true),
                  child: const Text('Save my version'),
                ),
              ] else
                TextButton(
                  onPressed: () => _saveArtifact(a),
                  child: const Text('Retry save'),
                ),
            ],
          ),
        if (chrome)
          SizedBox(
            height: 44,
            child: Row(
              children: [
                const SizedBox(width: 12),
                Icon(_icon(a.kind), size: 18),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    a.title,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
                IconButton(
                  tooltip: 'Minimize editor',
                  onPressed: () => store.minimizeArtifact(a.id),
                  icon: const Icon(Icons.minimize, size: 18),
                ),
                IconButton(
                  tooltip: 'Close editor',
                  onPressed: () => store.closeArtifact(a.id),
                  icon: const Icon(Icons.close, size: 18),
                ),
              ],
            ),
          ),
        Expanded(child: _editor(a)),
      ],
    ),
  );
  Widget _work(BuildContext context) {
    final p = store.currentProject;
    final layout = p.presentation;
    final visible = p.artifacts
        .where(
          (a) =>
              layout.openArtifactIds.contains(a.id) &&
              !layout.minimizedArtifactIds.contains(a.id),
        )
        .toList();
    final active =
        visible.where((a) => a.id == layout.activeArtifactId).firstOrNull ??
        visible.firstOrNull;
    return Column(
      children: [
        SizedBox(
          height: 48,
          child: Row(
            children: [
              const SizedBox(width: 12),
              Expanded(
                child: Text(
                  active?.title ?? 'Workspace',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(fontWeight: FontWeight.w600),
                ),
              ),
              _newWorkMenu(context),
              PopupMenuButton<String>(
                tooltip: 'Open project work',
                icon: const Icon(Icons.folder_open_outlined),
                onSelected: (id) =>
                    _open(p.artifacts.firstWhere((a) => a.id == id)),
                itemBuilder: (_) => [
                  for (final a in p.artifacts)
                    PopupMenuItem(value: a.id, child: Text(a.title)),
                ],
              ),
              PopupMenuButton<String>(
                tooltip: 'Workspace layout',
                icon: const Icon(Icons.view_quilt_outlined),
                onSelected: store.setLayout,
                itemBuilder: (_) => const [
                  PopupMenuItem(value: 'quiet', child: Text('Focused')),
                  PopupMenuItem(value: 'compare', child: Text('Compare')),
                  PopupMenuItem(
                    value: 'windows',
                    child: Text('Floating windows'),
                  ),
                ],
              ),
              if (active != null)
                PopupMenuButton<String>(
                  tooltip: 'Editor actions',
                  icon: const Icon(Icons.more_horiz),
                  onSelected: (action) {
                    if (action == 'minimize') {
                      store.minimizeArtifact(active.id);
                    } else if (action == 'close') {
                      store.closeArtifact(active.id);
                    } else {
                      store.attachArtifact(active.id);
                    }
                  },
                  itemBuilder: (_) => const [
                    PopupMenuItem(
                      value: 'attach',
                      child: Text('Attach to conversation'),
                    ),
                    PopupMenuItem(
                      value: 'minimize',
                      child: Text('Minimize editor'),
                    ),
                    PopupMenuItem(value: 'close', child: Text('Close editor')),
                  ],
                ),
            ],
          ),
        ),
        if (visible.length > 1 &&
            layout.layout != 'compare' &&
            layout.layout != 'windows')
          _ArtifactTabs(
            artifacts: visible,
            activeId: active?.id,
            onOpen: store.openArtifact,
          ),
        Expanded(
          child: active == null
              ? const Center(
                  child: Text('Open project work or create something in chat.'),
                )
              : layout.layout == 'compare'
              ? LayoutBuilder(
                  builder: (context, c) => c.maxWidth < 600
                      ? ListView(
                          children: [
                            for (final a in visible.take(2))
                              SizedBox(height: 450, child: _pane(a)),
                          ],
                        )
                      : Row(
                          children: [
                            for (final a in visible.take(2))
                              Expanded(child: _pane(a)),
                          ],
                        ),
                )
              : layout.layout == 'windows'
              ? _windows(visible)
              : _pane(active, chrome: false),
        ),
        if (layout.minimizedArtifactIds.isNotEmpty)
          SingleChildScrollView(
            scrollDirection: Axis.horizontal,
            child: Row(
              children: [
                for (final a in p.artifacts.where(
                  (a) => layout.minimizedArtifactIds.contains(a.id),
                ))
                  Padding(
                    padding: const EdgeInsets.all(6),
                    child: ActionChip(
                      avatar: const Icon(Icons.open_in_full, size: 15),
                      label: Text(a.title),
                      onPressed: () => _open(a),
                    ),
                  ),
              ],
            ),
          ),
      ],
    );
  }

  Widget _windows(List<WorkspaceArtifact> artifacts) => LayoutBuilder(
    builder: (context, c) {
      if (c.maxWidth < 600) {
        return ListView(
          children: [
            for (final a in artifacts) SizedBox(height: 460, child: _pane(a)),
          ],
        );
      }
      return Stack(
        children: [
          for (final (i, a) in artifacts.indexed)
            Builder(
              builder: (context) {
                final bounds =
                    store.currentProject.presentation.windowBounds[a.id] ??
                    [
                      20.0 + i * 28,
                      20.0 + i * 28,
                      (c.maxWidth * .75).clamp(320.0, c.maxWidth),
                      (c.maxHeight * .75).clamp(240.0, c.maxHeight),
                    ];
                return Positioned(
                  key: ValueKey('window-${a.id}'),
                  left: bounds[0].clamp(
                    0.0,
                    (c.maxWidth - 100).clamp(0.0, c.maxWidth),
                  ),
                  top: bounds[1].clamp(
                    0.0,
                    (c.maxHeight - 80).clamp(0.0, c.maxHeight),
                  ),
                  width: bounds[2].clamp(280.0, c.maxWidth),
                  height: bounds[3].clamp(220.0, c.maxHeight),
                  child: Stack(
                    children: [
                      Positioned.fill(child: _pane(a)),
                      Positioned(
                        top: 6,
                        left: 8,
                        right: 88,
                        height: 42,
                        child: MouseRegion(
                          cursor: SystemMouseCursors.move,
                          child: GestureDetector(
                            key: ValueKey('window-drag-${a.id}'),
                            behavior: HitTestBehavior.opaque,
                            onPanUpdate: (d) {
                              final current =
                                  store
                                      .currentProject
                                      .presentation
                                      .windowBounds[a.id] ??
                                  bounds;
                              store.setWindowBounds(a.id, [
                                (current[0] + d.delta.dx).clamp(
                                  0.0,
                                  c.maxWidth - 100,
                                ),
                                (current[1] + d.delta.dy).clamp(
                                  0.0,
                                  c.maxHeight - 80,
                                ),
                                current[2],
                                current[3],
                              ]);
                            },
                            child: const SizedBox.expand(),
                          ),
                        ),
                      ),
                      Positioned(
                        bottom: 8,
                        right: 8,
                        child: MouseRegion(
                          cursor: SystemMouseCursors.resizeUpLeftDownRight,
                          child: GestureDetector(
                            key: ValueKey('window-resize-${a.id}'),
                            behavior: HitTestBehavior.opaque,
                            onPanUpdate: (d) {
                              final current =
                                  store
                                      .currentProject
                                      .presentation
                                      .windowBounds[a.id] ??
                                  bounds;
                              store.setWindowBounds(a.id, [
                                current[0],
                                current[1],
                                (current[2] + d.delta.dx).clamp(
                                  280.0,
                                  c.maxWidth,
                                ),
                                (current[3] + d.delta.dy).clamp(
                                  220.0,
                                  c.maxHeight,
                                ),
                              ]);
                            },
                            child: const SizedBox(
                              width: 28,
                              height: 28,
                              child: Icon(Icons.drag_handle, size: 22),
                            ),
                          ),
                        ),
                      ),
                    ],
                  ),
                );
              },
            ),
        ],
      );
    },
  );
  Widget _workspace(BuildContext context) => LayoutBuilder(
    builder: (context, c) {
      final mobile = c.maxWidth < 760;
      final chat = Card(
        margin: const EdgeInsets.all(8),
        clipBehavior: Clip.antiAlias,
        child: Column(
          children: [
            ListTile(
              dense: true,
              title: Text(
                store.currentConversation.title,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
              trailing: IconButton(
                tooltip: 'New conversation',
                onPressed: () => store.createConversation(),
                icon: const Icon(Icons.edit_square),
              ),
            ),
            const Divider(height: 1),
            Expanded(
              child: IndexedStack(
                index: [
                  for (final project in store.projects)
                    for (final conversation in project.conversations)
                      conversation.id,
                ].indexOf(store.currentConversation.id),
                children: [
                  for (final project in store.projects)
                    for (final conversation in project.conversations)
                      WorkspaceChat(
                        key: ValueKey(conversation.id),
                        conversation: conversation,
                        project: project,
                        active:
                            (ModalRoute.of(context)?.isCurrent ?? true) &&
                            !(mobile && _mobileWork) &&
                            !_directory &&
                            !_home &&
                            conversation.id == store.currentConversation.id,
                        store: store,
                        onRun: widget.onRun,
                        onOpenUrl: widget.onOpenUrl,
                        onArtifact: (result) =>
                            _accept(result, project: project),
                        onAttach: () => _attach(context),
                        onTranscribe: widget.onTranscribe,
                      ),
                ],
              ),
            ),
          ],
        ),
      );
      if (mobile) {
        return Column(
          children: [
            SegmentedButton<bool>(
              segments: const [
                ButtonSegment(value: false, label: Text('Conversation')),
                ButtonSegment(value: true, label: Text('Workspace')),
              ],
              selected: {_mobileWork},
              onSelectionChanged: (v) => setState(() => _mobileWork = v.first),
            ),
            Expanded(
              child: IndexedStack(
                index: _mobileWork ? 1 : 0,
                children: [chat, _work(context)],
              ),
            ),
          ],
        );
      }
      final width = store.currentProject.presentation.chatWidth.clamp(
        300.0,
        c.maxWidth * .6,
      );
      return Row(
        children: [
          SizedBox(width: width, child: chat),
          MouseRegion(
            cursor: SystemMouseCursors.resizeColumn,
            child: GestureDetector(
              behavior: HitTestBehavior.opaque,
              onHorizontalDragUpdate: (d) => store.setChatWidth(
                (width + d.delta.dx).clamp(300.0, c.maxWidth * .6),
              ),
              child: const SizedBox(
                width: 8,
                child: Center(child: Icon(Icons.drag_indicator, size: 14)),
              ),
            ),
          ),
          Expanded(child: _work(context)),
        ],
      );
    },
  );
  @override
  Widget build(BuildContext context) => MaterialApp(
    navigatorKey: _navigator,
    navigatorObservers: [_routes],
    initialRoute: '/',
    scaffoldMessengerKey: _messenger,
    title: 'IntoCaht',
    debugShowCheckedModeBanner: false,
    theme: UiTheme.light().copyWith(
      visualDensity: store.settings.compactDensity
          ? VisualDensity.compact
          : VisualDensity.standard,
    ),
    darkTheme: UiTheme.dark().copyWith(
      colorScheme: ColorScheme.fromSeed(
        seedColor: LumenPalette.accent,
        brightness: Brightness.dark,
        surface: UiPalette.surface,
      ),
      visualDensity: store.settings.compactDensity
          ? VisualDensity.compact
          : VisualDensity.standard,
    ),
    themeAnimationDuration: store.settings.reducedMotion
        ? Duration.zero
        : kThemeAnimationDuration,
    builder: (context, child) {
      if (_ready) {
        _routes.sync(
          _directory
              ? '/projects'
              : '/projects/${store.currentProject.id}${_home ? '' : '/workspace'}',
        );
      }
      return MediaQuery(
        data: MediaQuery.of(context).copyWith(
          disableAnimations:
              store.settings.reducedMotion ||
              MediaQuery.of(context).disableAnimations,
        ),
        child: UiThemeScope(
          brightness: Theme.of(context).brightness,
          child: Theme(data: Theme.of(context), child: child!),
        ),
      );
    },
    themeMode: switch (store.settings.theme) {
      'dark' => ThemeMode.dark,
      'light' => ThemeMode.light,
      _ => ThemeMode.system,
    },
    home: Builder(
      builder: (context) => Scaffold(
        appBar: AppBar(
          toolbarHeight: MediaQuery.sizeOf(context).width < 600 ? 100 : 56,
          titleSpacing: 16,
          title: Wrap(
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              const Text(
                'IntoCaht',
                style: TextStyle(fontWeight: FontWeight.w600, fontSize: 20),
              ),
              const SizedBox(width: 8),
              TextButton(
                style: TextButton.styleFrom(
                  minimumSize: Size.zero,
                  padding: const EdgeInsets.symmetric(
                    horizontal: 8,
                    vertical: 12,
                  ),
                ),
                onPressed: () => setState(() => _directory = true),
                child: const Text('Projects'),
              ),
              if (_ready && !_directory) ...[
                const Text('/', style: TextStyle(fontSize: 16)),
                ConstrainedBox(
                  constraints: const BoxConstraints(maxWidth: 200),
                  child: TextButton(
                    style: TextButton.styleFrom(
                      minimumSize: Size.zero,
                      padding: const EdgeInsets.symmetric(
                        horizontal: 6,
                        vertical: 12,
                      ),
                    ),
                    onPressed: () => setState(() => _home = true),
                    child: Text(
                      store.currentProject.title,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                  ),
                ),
              ],
            ],
          ),
          actions: [
            if (_ready && !_directory)
              IconButton(
                tooltip: 'Search project work',
                onPressed: () => _search(context),
                icon: const Icon(Icons.search),
              ),
            IconButton(
              tooltip: 'Settings',
              onPressed: _ready
                  ? () => Navigator.of(context).push(
                      MaterialPageRoute<void>(
                        settings: const RouteSettings(
                          name: '/settings/profile',
                        ),
                        builder: (_) => WorkspaceSettings(
                          store: store,
                          kernelBaseUri: widget.kernelBaseUri,
                          onOpen: widget.onOpenUrl,
                          onClose: () => Navigator.of(context).pop(),
                        ),
                      ),
                    )
                  : null,
              icon: const Icon(Icons.settings_outlined),
            ),
          ],
        ),
        body: !_ready
            ? const Center(child: CircularProgressIndicator())
            : Column(
                children: [
                  if (widget.statusMessage != null)
                    MaterialBanner(
                      content: Text(widget.statusMessage!),
                      actions: const [SizedBox.shrink()],
                    ),
                  if (store.persistenceError != null)
                    Text(
                      store.persistenceError!,
                      style: TextStyle(
                        color: Theme.of(context).colorScheme.error,
                      ),
                    ),
                  Expanded(
                    child: IndexedStack(
                      index: _directory
                          ? 0
                          : _home
                          ? 1
                          : 2,
                      children: [
                        _projects(context),
                        _projectHome(context),
                        _workspace(context),
                      ],
                    ),
                  ),
                ],
              ),
      ),
    ),
  );
  void _search(BuildContext context) {
    _query = '';
    showDialog<void>(
      context: context,
      builder: (c) => StatefulBuilder(
        builder: (c, update) => AlertDialog(
          title: TextField(
            autofocus: true,
            decoration: const InputDecoration(hintText: 'Search project work'),
            onChanged: (v) => update(() => _query = v.toLowerCase()),
          ),
          content: SizedBox(
            width: 480,
            child: ListView(
              shrinkWrap: true,
              children: [
                for (final a in store.currentProject.artifacts.where(
                  (a) => a.title.toLowerCase().contains(_query),
                ))
                  ListTile(
                    leading: Icon(_icon(a.kind)),
                    title: Text(a.title),
                    onTap: () {
                      Navigator.pop(c);
                      _open(a);
                      setState(() => _home = false);
                    },
                  ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _ArtifactTabs extends StatefulWidget {
  const _ArtifactTabs({
    required this.artifacts,
    required this.activeId,
    required this.onOpen,
  });
  final List<WorkspaceArtifact> artifacts;
  final String? activeId;
  final ValueChanged<String> onOpen;
  @override
  State<_ArtifactTabs> createState() => _ArtifactTabsState();
}

class _ArtifactTabsState extends State<_ArtifactTabs> {
  final _anchors = <String, GlobalKey>{};
  @override
  void initState() {
    super.initState();
    _revealActive();
  }

  @override
  void didUpdateWidget(covariant _ArtifactTabs oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.activeId != widget.activeId ||
        oldWidget.artifacts.length != widget.artifacts.length) {
      _revealActive();
    }
  }

  void _revealActive() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      final target = _anchors[widget.activeId]?.currentContext;
      if (target != null) {
        Scrollable.ensureVisible(target, alignment: .5);
      }
    });
  }

  @override
  Widget build(BuildContext context) => SingleChildScrollView(
    scrollDirection: Axis.horizontal,
    child: Row(
      children: [
        for (final artifact in widget.artifacts)
          Padding(
            key: _anchors.putIfAbsent(artifact.id, () => GlobalKey()),
            padding: const EdgeInsets.all(3),
            child: Tooltip(
              message: artifact.title,
              child: ChoiceChip(
                key: ValueKey('artifact-tab-${artifact.id}'),
                label: ConstrainedBox(
                  constraints: const BoxConstraints(maxWidth: 180),
                  child: Text(
                    artifact.title,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
                selected: artifact.id == widget.activeId,
                onSelected: (_) => widget.onOpen(artifact.id),
              ),
            ),
          ),
      ],
    ),
  );
}
