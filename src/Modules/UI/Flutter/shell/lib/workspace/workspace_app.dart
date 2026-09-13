import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:file_picker/file_picker.dart';

import 'brain_graph_store.dart';
import 'workspace_table_import.dart';
import 'workspace_store.dart';
import 'workspace_desktop.dart';
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
    this.onSalesforceConnected,
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
  final Future<bool> Function()? onSalesforceConnected;
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
        seedProject: false,
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
  bool _ready = false, _directory = true, _mobileWork = false;
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

  /// Tables and charts arrive complete in the tool result: there is no saved
  /// artifact to re-read on open or to write back on edit.
  static bool _servedByResult(String kind) =>
      kind == 'table' || kind == 'chart';

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
        if (!_servedByResult(result['kind'] as String? ?? 'document'))
          '_remote': true,
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
    if (!_servedByResult(a.kind) &&
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

  Future<void> _newProject(
    BuildContext context, [
    UiSpecialist specialist = uiCoordinator,
  ]) async {
    final result = await showDialog<UiProjectStart>(
      context: context,
      builder: (_) => UiProjectStartDialog(specialist: specialist),
    );
    if (!mounted || result == null) return;
    store.createProject(
      result.name,
      agentId: result.specialist.id,
      draft: result.intent,
    );
    setState(() {
      _directory = false;

      _mobileWork = false;
    });
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
          setState(() => _mobileWork = true);
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
      setState(() => _mobileWork = true);
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
          setState(() => _mobileWork = true);
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
    icon: const Icon(Icons.add, size: 20),
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

  Widget _projects(BuildContext context) => UiProjectLibrary(
    onStartTemplate: (specialist) => _newProject(context, specialist),
    projects: [
      for (final project in store.projects)
        UiProjectSummary(
          id: project.id,
          title: project.title,
          conversations: project.conversations.length,
          savedItems: project.artifacts.length,
          workKinds: {
            for (final kind in project.artifacts.map((a) => a.kind).toSet())
              kind: project.artifacts.where((a) => a.kind == kind).length,
          },
        ),
    ],
    onCreate: () => _newProject(context),
    onOpen: (id) {
      store.selectProject(id);
      setState(() {
        _directory = false;
      });
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
    'chart' => Icons.bar_chart_rounded,
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

  Widget _pane(WorkspaceArtifact a) => Column(
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
      Expanded(child: _editor(a)),
    ],
  );
  Future<void> _projectFiles(BuildContext context) async {
    final project = store.currentProject;
    final choice = await showDialog<(String, String)>(
      context: context,
      builder: (dialogContext) {
        var query = '';
        return StatefulBuilder(
          builder: (context, update) => AlertDialog(
            title: const Text('Project files'),
            content: SizedBox(
              width: 560,
              height: 400,
              child: Column(
                children: [
                  TextField(
                    autofocus: true,
                    decoration: const InputDecoration(
                      hintText: 'Find a table, document or image',
                      prefixIcon: Icon(Icons.search),
                    ),
                    onChanged: (value) =>
                        update(() => query = value.toLowerCase()),
                  ),
                  const SizedBox(height: 16),
                  Expanded(
                    child: ListView(
                      children: [
                        if (project.artifacts.isEmpty)
                          const Padding(
                            padding: EdgeInsets.all(24),
                            child: Text('Your saved work will appear here.'),
                          ),
                        for (final a in project.artifacts.where(
                          (a) => a.title.toLowerCase().contains(query),
                        ))
                          ListTile(
                            contentPadding: EdgeInsets.zero,
                            leading: Icon(_icon(a.kind), size: 22),
                            title: Text(
                              a.title,
                              maxLines: 2,
                              overflow: TextOverflow.ellipsis,
                            ),
                            subtitle: Text(a.kind),
                            onTap: () =>
                                Navigator.pop(dialogContext, (a.id, 'open')),
                            trailing: PopupMenuButton<String>(
                              tooltip: 'Open options',
                              onSelected: (mode) =>
                                  Navigator.pop(dialogContext, (a.id, mode)),
                              itemBuilder: (_) => const [
                                PopupMenuItem(
                                  value: 'beside',
                                  child: Text('Open beside'),
                                ),
                                PopupMenuItem(
                                  value: 'floating',
                                  child: Text('Open as window'),
                                ),
                              ],
                            ),
                          ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(dialogContext),
                child: const Text('Done'),
              ),
            ],
          ),
        );
      },
    );
    if (!mounted || choice == null || store.currentProject.id != project.id) {
      return;
    }
    if (choice.$2 == 'beside') {
      store.openBeside(choice.$1);
    } else if (choice.$2 == 'floating') {
      store.openArtifact(choice.$1, placement: 'floating');
    }
    await _open(project.artifacts.firstWhere((a) => a.id == choice.$1));
  }

  Widget _work(BuildContext context) {
    final p = store.currentProject;
    return Column(
      children: [
        SizedBox(
          height: 48,
          child: Row(
            children: [
              const SizedBox(width: 8),
              Tooltip(
                message: 'Open project work',
                child: TextButton.icon(
                  onPressed: () => _projectFiles(context),
                  icon: const Icon(Icons.folder_open_outlined, size: 19),
                  label: const Text('Project files'),
                ),
              ),
              _newWorkMenu(context),
              if (widget.onListTables != null || widget.onListArtifacts != null)
                IconButton(
                  tooltip: 'Import saved work',
                  onPressed: () => _import(context),
                  icon: const Icon(Icons.download_outlined, size: 19),
                ),
              const Spacer(),
              if (MediaQuery.sizeOf(context).width >= 760)
                IconButton(
                  tooltip: p.presentation.chatCollapsed
                      ? 'Show chat'
                      : 'Hide chat',
                  onPressed: store.toggleChat,
                  icon: Icon(
                    p.presentation.chatCollapsed
                        ? Icons.chat_bubble_outline
                        : Icons.view_sidebar_outlined,
                    size: 19,
                  ),
                ),
              const SizedBox(width: 8),
            ],
          ),
        ),
        Expanded(
          child: WorkspaceDesktop(
            key: ValueKey('desktop-${p.id}'),
            store: store,
            editorBuilder: _pane,
          ),
        ),
        UiArtifactDock(
          items: [
            for (final a in p.artifacts.where(
              (a) => p.presentation.minimizedArtifactIds.contains(a.id),
            ))
              UiDockItem(id: a.id, title: a.title, kind: a.kind),
          ],
          onRestore: (id) => _open(p.artifacts.firstWhere((a) => a.id == id)),
        ),
      ],
    );
  }

  Widget _workspace(BuildContext context) => LayoutBuilder(
    builder: (context, c) {
      final mobile = c.maxWidth < 760;
      final chat = Material(
        color: Theme.of(context).scaffoldBackgroundColor,
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 8, 8, 8),
              child: Row(
                children: [
                  Expanded(
                    child: PopupMenuButton<String>(
                      tooltip: 'Switch conversation',
                      onSelected: store.selectConversation,
                      itemBuilder: (_) => [
                        for (final conversation
                            in store.currentProject.conversations)
                          CheckedPopupMenuItem(
                            value: conversation.id,
                            checked:
                                conversation.id == store.currentConversation.id,
                            child: Text(
                              conversation.title,
                              maxLines: 2,
                              overflow: TextOverflow.ellipsis,
                            ),
                          ),
                      ],
                      child: Padding(
                        padding: const EdgeInsets.symmetric(vertical: 12),
                        child: Row(
                          children: [
                            Expanded(
                              child: Text(
                                store.currentConversation.title,
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                style: const TextStyle(
                                  fontSize: 14,
                                  fontWeight: FontWeight.w500,
                                ),
                              ),
                            ),
                            const SizedBox(width: 8),
                            Icon(
                              Icons.keyboard_arrow_down,
                              size: 17,
                              color: Theme.of(context)
                                  .colorScheme
                                  .onSurfaceVariant,
                            ),
                          ],
                        ),
                      ),
                    ),
                  ),
                  IconButton(
                    tooltip: 'New conversation',
                    onPressed: () => store.createConversation(),
                    icon: const Icon(Icons.edit_square, size: 19),
                  ),
                ],
              ),
            ),
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
                            !store.currentProject.presentation.chatCollapsed &&
                            !_directory &&
                            conversation.id == store.currentConversation.id,
                        store: store,
                        onRun: widget.onRun,
                        onOpenUrl: widget.onOpenUrl,
                        onSalesforceConnected: widget.onSalesforceConnected,
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
          Offstage(
            offstage: store.currentProject.presentation.chatCollapsed,
            child: SizedBox(width: width, child: chat),
          ),
          if (!store.currentProject.presentation.chatCollapsed)
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
    title: 'IntoChat',
    debugShowCheckedModeBanner: false,
    theme: UiTheme.workspace(Brightness.light).copyWith(
      visualDensity: store.settings.compactDensity
          ? VisualDensity.compact
          : VisualDensity.standard,
    ),
    darkTheme: UiTheme.workspace(Brightness.dark).copyWith(
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
              : '/projects/${store.currentProject.id}/workspace',
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
          toolbarHeight: !_directory && MediaQuery.sizeOf(context).width < 600
              ? 100
              : 64,
          titleSpacing: 16,
          title: Wrap(
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              Tooltip(
                message: 'Go to homepage',
                child: TextButton(
                  onPressed: () => setState(() {
                    _directory = true;
                  }),
                  style: TextButton.styleFrom(
                    foregroundColor: Theme.of(context).colorScheme.onSurface,
                    padding: const EdgeInsets.symmetric(
                      horizontal: 12,
                      vertical: 16,
                    ),
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(6),
                    ),
                  ),
                  child: const Text(
                    'IntoChat',
                    style: TextStyle(
                      fontWeight: FontWeight.w600,
                      fontSize: 21,
                      letterSpacing: -0.8,
                    ),
                  ),
                ),
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
                    onPressed: () => setState(() => _mobileWork = false),
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
                      index: _directory ? 0 : 1,
                      children: [
                        _projects(context),
                        if (store.projects.isNotEmpty)
                          _workspace(context)
                        else
                          const SizedBox.shrink(),
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
                      setState(() => _mobileWork = true);
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
