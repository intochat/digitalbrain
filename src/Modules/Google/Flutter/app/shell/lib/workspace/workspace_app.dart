import 'dart:async';
import 'dart:ui' show SemanticsRole;

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';

import '../integrations/connect_window.dart';
import '../integrations/integrations_menu.dart';
import 'workspace_store.dart';
import 'workspace_remote_controller.dart';
import 'workspace_desktop.dart';
import 'workspace_settings.dart';
import 'workspace_routes.dart';
import 'workspace_chat.dart';
import 'app_surface_host.dart';
import 'inbox_panel.dart';
import 'workspace_islands.dart';
import 'behaviors/behavior_manager.dart';
import 'apps/consent_sheet_view.dart';
import 'mydata/mydata_window.dart';

class WorkspaceApp extends StatefulWidget {
  const WorkspaceApp({
    super.key,
    this.store,
    this.initialLocation,
    this.persistenceKey = 'intocaht.workspace.v1',
    this.onRun,
    this.onOpenUrl,
    this.onSalesforceConnected,
    this.kernelBaseUri,
    this.statusMessage,
    this.onReadTable,
    this.onUpdateTableView,
    this.onListTables,
    this.programmingClient,
    this.connectedSources = const [],
  });
  final WorkspaceStore? store;
  final Uri? initialLocation;
  final String persistenceKey;
  final AgentRunner? onRun;
  final OpenUrl? onOpenUrl;
  final Future<bool> Function()? onSalesforceConnected;
  final Uri? kernelBaseUri;
  final String? statusMessage;
  final ReadTable? onReadTable;
  final UpdateTableView? onUpdateTableView;
  final ListTables? onListTables;
  final DigitalBrainUiClient? programmingClient;

  /// Sources the shell knows are connected; pushed to the workspace so first-run prompts match.
  final List<String> connectedSources;
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
  final _remote = <String, WorkspaceRemoteController>{};
  final _remoteCancelled = Completer<void>();
  final _tableCancelled = <String, Completer<void>>{};
  final _apps = <String, List<AppManifestSummary>>{};
  final _appsLoading = <String>{};
  final _sourcesSynced = <String>{};
  final _firstRunDismissed = <String>{};

  void _connectWorkspaces() {
    final client = widget.programmingClient;
    if (client == null || !_ready) return;
    store.onRemoteWindowAction = (workspace, window, open) =>
        unawaited(_remoteAction(workspace, window, open));
    for (final project in store.projects) {
      if (_remote.containsKey(project.id)) continue;
      if (_sourcesSynced.add(project.id)) {
        unawaited(
          client
              .setConnectedSources(project.id, widget.connectedSources)
              .then((snapshot) {
                if (mounted) store.reconcileWorkspace(project, snapshot);
              })
              .catchError((Object _) {}),
        );
      }
      final controller = WorkspaceRemoteController(
        read: (id) =>
            client.readWorkspace(id, cancelled: _remoteCancelled.future),
        watch: client.watchWorkspace,
        apply: (state) {
          if (mounted) store.reconcileWorkspace(project, state);
        },
        onError: (error) {
          if (mounted) {
            _messenger.currentState?.showSnackBar(
              const SnackBar(
                content: Text(
                  'Workspace connection interrupted. Reconnecting…',
                ),
              ),
            );
          }
        },
      );
      _remote[project.id] = controller;
      controller.start(project.id);
    }
  }

  Future<void> _remoteAction(String workspace, String window, bool open) async {
    final client = widget.programmingClient;
    if (client == null) return;
    try {
      final revision = store.remoteRevisions[workspace] ?? 0;
      final state = open
          ? await client.reopenWorkspaceWindow(workspace, window, revision)
          : await client.closeWorkspaceWindow(workspace, window, revision);
      if (!mounted) return;
      final project = store.projects.firstWhere((p) => p.id == workspace);
      store.reconcileWorkspace(project, state);
      if (open && workspace == store.selectedProjectId) {
        store.focusWindow(window);
      }
    } catch (error) {
      await _remote[workspace]?.refresh();
      if (mounted) {
        _messenger.currentState?.showSnackBar(
          SnackBar(content: Text('Window changed. Please try again. $error')),
        );
      }
    }
  }

  Future<void> _cancelPreviousTable(String id) {
    final previous = _tableCancelled[id];
    if (previous != null && !previous.isCompleted) previous.complete();
    final next = Completer<void>();
    _tableCancelled[id] = next;
    return next.future;
  }

  final _hydratingTables = <String>{};
  final _tableErrors = <String, String>{};

  final _messenger = GlobalKey<ScaffoldMessengerState>();
  final _navigator = GlobalKey<NavigatorState>();
  late final WorkspaceRouteBridge _routes;
  late final Uri _initialLocation;
  bool _ready = false, _directory = false, _mobileWork = false;
  String _query = '';
  @override
  void initState() {
    super.initState();
    _initialLocation =
        widget.initialLocation ??
        Uri.parse(WidgetsBinding.instance.platformDispatcher.defaultRouteName);
    _routes = WorkspaceRouteBridge(onNavigate: _navigateRoute);
    store.addListener(_changed);
    _load();
  }

  Future<void> _load() async {
    await store.load();
    if (store.projects.isEmpty) store.createProject('Personal');
    if (!mounted) return;
    setState(() => _ready = true);
    _connectWorkspaces();
    _loadApps(store.currentProject.id);
    _navigateRoute(_initialLocation);
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
              connectionsRequest: _connectionsRequest,
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
        _directory = false;
      }
    });
  }

  void _changed() {
    _connectWorkspaces();
    if (store.projects.isNotEmpty) _loadApps(store.currentProject.id);
    if (mounted) setState(() {});
  }

  void _loadApps(String workspaceId) {
    final client = widget.programmingClient;
    if (client == null ||
        _apps.containsKey(workspaceId) ||
        !_appsLoading.add(workspaceId)) {
      return;
    }
    unawaited(
      client
          .listApps(workspaceId)
          .then((apps) {
            if (!mounted) return;
            _apps[workspaceId] = apps;
            setState(() {});
          })
          .catchError((Object _) {
            _appsLoading.remove(workspaceId);
          }),
    );
  }

  @override
  void dispose() {
    _remoteCancelled.complete();
    for (final controller in _remote.values) {
      controller.dispose();
    }
    for (final cancelled in _tableCancelled.values) {
      if (!cancelled.isCompleted) cancelled.complete();
    }
    store.onRemoteWindowAction = null;
    _routes.dispose();
    store.removeListener(_changed);
    for (final t in _tables.values) {
      t.dispose();
    }
    if (widget.store == null) store.dispose();
    super.dispose();
  }

  void _accept(Map<String, dynamic> result, {WorkspaceProject? project}) {
    final destination = project ?? store.currentProject;
    if (result['remoteManaged'] == true) {
      final window = result['windowId'] as String?;
      if (window != null) {
        unawaited(_remoteAction(destination.id, window, true));
      }
      return;
    }
    final id = result['id'] as String?;
    if (id == null) return;
    if (result['kind'] == 'table') {
      try {
        final snapshot = TableSnapshot.fromJson(result);
        final existing = _tables[id];
        if (existing == null) {
          _tables[id] = UiTableController(
            workspace: destination.id,
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
        } else if (snapshot.rows.isEmpty &&
            snapshot.revision > existing.snapshot.revision &&
            existing.read != null) {
          unawaited(existing.acceptSavedView(snapshot));
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
    } else if ((existing.data['_revision'] as num? ?? 0) <=
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
    if (destination.id == store.currentProject.id) store.openArtifact(id);
  }

  Future<void> _open(WorkspaceArtifact a) async {
    final destination = store.currentProject;
    if (a.kind == 'app') {
      store.launchLocalApp(a.data['app'] as String);
      return;
    }
    store.openArtifact(a.id);
    if (a.kind == 'table' && !_tables.containsKey(a.id)) {
      if (widget.onReadTable != null) {
        try {
          final snapshot = await widget.onReadTable!(destination.id, a.id);
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
      if (artifact.remoteManaged && widget.programmingClient != null) {
        final client = widget.programmingClient!;
        final tableId = artifact.data['tableId'] as String;
        final snapshot = await client.readWorkspaceTable(
          project.id,
          tableId,
          cancelled: _cancelPreviousTable(tableId),
        );
        if (!mounted) return;
        _tables[artifact.id] = UiTableController(
          workspace: project.id,
          snapshot: snapshot,
          read: (_, id, {offset = 0, limit = 25}) => client.readWorkspaceTable(
            project.id,
            id,
            offset: offset,
            limit: limit,
            cancelled: _cancelPreviousTable(id),
          ),
          update: (_, id, update) => client.updateWorkspaceTable(
            project.id,
            id,
            update,
            cancelled: _cancelPreviousTable(id),
          ),
        );
        return;
      }
      final snapshot = widget.onReadTable == null
          ? TableSnapshot.fromJson(artifact.data)
          : await widget.onReadTable!(project.id, artifact.id);
      if (mounted) _accept(snapshot.toJson(), project: project);
    } catch (e) {
      _tableErrors[artifact.id] = 'Could not load table: $e';
    } finally {
      _hydratingTables.remove(artifact.id);
      if (mounted) setState(() {});
    }
  }

  Widget _editor(WorkspaceArtifact a) {
    if (a.kind == 'app') {
      final client = widget.programmingClient;
      if (client == null) {
        return const Center(
          child: Text('Connect to IntoChat to open local apps.'),
        );
      }
      if (a.data['app'] == 'behaviors') {
        final project = store.currentProject;
        return BehaviorManager(
          key: ValueKey('${project.id}-${a.id}'),
          initialId: a.data['selected'] as String?,
          active: !project.presentation.minimizedArtifactIds.contains(a.id),
          request: (path, {body}) =>
              client.behaviorRequest(project.id, path, body: body),
          onSelected: (id) {
            a.data['selected'] = id;
            store.save();
          },
          onAsk: (id, prompt) {
            final conversation = store.createConversation(
              title: 'Behavior: $id',
            );
            conversation.draft = prompt;
            project.presentation.chatCollapsed = false;
            setState(() => _mobileWork = false);
            store.save();
          },
        );
      }
      if (a.data['app'] == 'mydata') {
        return Semantics(
          container: true,
          explicitChildNodes: true,
          role: SemanticsRole.region,
          label: a.title,
          child: MyDataWindow(
            key: ValueKey('${store.currentProject.id}-${a.id}'),
            request: (path, {body}) =>
                client.myDataRequest(store.currentProject.id, path, body: body),
            loadGrants: () => client.listGrants(store.currentProject.id),
            revokeGrant: (grant) => client.revokeGrant(
              store.currentProject.id,
              appId: grant.appId,
              semanticTypeId: grant.semanticTypeId,
              mode: grant.mode,
            ),
          ),
        );
      }
      return Semantics(
        container: true,
        explicitChildNodes: true,
        role: SemanticsRole.region,
        label: a.title,
        child: AppSurfaceHost(
          key: ValueKey('${store.currentProject.id}-${a.id}'),
          client: client,
          workspace: store.currentProject.id,
          artifact: a,
          onImage: (document) {
            final images = store.launchLocalApp('images');
            final documents = List<Map<String, dynamic>>.from(
              (images.data['documents'] as List? ?? []).map(
                (d) => Map<String, dynamic>.from(d),
              ),
            );
            if (!documents.any((d) => d['id'] == document['id'])) {
              documents.add({
                'id': document['id'],
                'name': document['asset']['name'],
              });
            }
            images.data['documents'] = documents;
            images.data['selected'] = document['id'];
            store.save();
          },
          onChanged: () => store.save(),
          onSaved: () {
            final files = store.currentProject.artifacts
                .where((a) => a.id == 'app-files')
                .firstOrNull;
            if (files != null) {
              files.data['refresh'] = (files.data['refresh'] as int? ?? 0) + 1;
              store.save();
            }
          },
        ),
      );
    }

    if (a.kind == 'table' && _tables[a.id] == null) {
      if (!_hydratingTables.contains(a.id) && !_tableErrors.containsKey(a.id)) {
        _hydratingTables.add(a.id);
        final project = store.currentProject;
        WidgetsBinding.instance.addPostFrameCallback((_) {
          if (mounted) _hydrateTable(a, project);
        });
      }
      return _region(
        a.title,
        Center(
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
        ),
      );
    }
    if (a.kind == 'table') {
      return _region(a.title, UiDataTable(controller: _tables[a.id]!));
    }
    return _region(
      a.title,
      Center(child: Text('This window type is not available yet: ${a.kind}')),
    );
  }

  Widget _region(String title, Widget child) => Semantics(
    container: true,
    explicitChildNodes: true,
    role: SemanticsRole.region,
    label: title,
    child: child,
  );

  Widget _pane(WorkspaceArtifact a) => _editor(a);
  Future<void> _projectFiles(BuildContext context) async {
    final project = store.currentProject;
    final choice = await showDialog<(String, String)>(
      context: context,
      builder: (dialogContext) {
        var query = '';
        return StatefulBuilder(
          builder: (context, update) => AlertDialog(
            title: const Text('Saved work'),
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

  Widget _work(BuildContext context) => WorkspaceDesktop(
    key: ValueKey('desktop-${store.currentProject.id}'),
    store: store,
    editorBuilder: _pane,
    editorStateToken: (artifact) => Object.hash(
      _tables[artifact.id],
      _tableErrors[artifact.id],
      artifact.data['selected'],
      artifact.data['refresh'],
    ),
  );

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
                                'Assistant',
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
            // Starter prompts sit above the conversation on a fresh workspace; the message box
            // stays reachable so the owner can always just type.
            if (store.firstRun != null &&
                !_firstRunDismissed.contains(store.currentProject.id) &&
                store.currentConversation.messages.isEmpty)
              Flexible(
                fit: FlexFit.loose,
                child: FirstRunView(
                  state: store.firstRun!,
                  onPrompt: _startFromPrompt,
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
                        selectedBehaviorId:
                            project.presentation.activeArtifactId ==
                                'app-behaviors'
                            ? project.artifacts
                                      .where((a) => a.id == 'app-behaviors')
                                      .firstOrNull
                                      ?.data['selected']
                                  as String?
                            : null,
                        onOpenBehavior: (id) {
                          store.selectProject(project.id);
                          final app = store.launchLocalApp('behaviors');
                          app.data['selected'] = id;
                          store.save();
                        },
                        onReadConversation:
                            widget.programmingClient?.readWorkspaceConversation,
                        onOpenUrl: widget.onOpenUrl,
                        onSalesforceConnected: widget.onSalesforceConnected,
                        onReportProblem: widget.programmingClient == null
                            ? null
                            : (workspaceId, intentId, message) =>
                                  widget.programmingClient!.reportProblem(
                                    workspaceId: workspaceId,
                                    intentId: intentId,
                                    message: message,
                                  ),
                        onArtifact: (result) =>
                            _accept(result, project: project),
                        onAttach: () => _attach(context),
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
                ButtonSegment(value: false, label: Text('Assistant')),
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
        textDirection: store.settings.assistantRight
            ? TextDirection.rtl
            : TextDirection.ltr,
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
                  if (store.settings.dockTop) _islands(context),
                  Expanded(
                    child: Padding(
                      padding: const EdgeInsets.fromLTRB(12, 10, 12, 0),
                      child: _workspace(context),
                    ),
                  ),
                  if (!store.settings.dockTop) _islands(context),
                ],
              ),
      ),
    ),
  );
  Widget _islands(BuildContext context) => WorkspaceIslands(
    store: store,
    apps: _apps[store.currentProject.id] ?? const [],
    onLaunch: _launchApp,
    onRestore: (id) =>
        _open(store.currentProject.artifacts.firstWhere((a) => a.id == id)),
    onNewWorkspace: () async {
      final input = TextEditingController();
      final name = await showDialog<String>(
        context: context,
        builder: (context) => AlertDialog(
          title: const Text('New workspace'),
          content: TextField(
            controller: input,
            autofocus: true,
            decoration: const InputDecoration(labelText: 'Workspace name'),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context),
              child: const Text('Cancel'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(context, input.text),
              child: const Text('Create workspace'),
            ),
          ],
        ),
      );
      if (name != null && mounted) {
        store.createProject(name.trim().isEmpty ? 'Untitled workspace' : name);
      }
    },
    onSavedWork: () => _projectFiles(context),
    onSearch: () => _search(context),
    onInbox: widget.programmingClient == null
        ? null
        : () => _openInbox(context),
    onSettings: () => Navigator.of(context).push(
      MaterialPageRoute<void>(
        settings: const RouteSettings(name: '/settings/profile'),
        builder: (_) => WorkspaceSettings(
          store: store,
          kernelBaseUri: widget.kernelBaseUri,
          onOpen: widget.onOpenUrl,
          connectionsRequest: _connectionsRequest,
          onClose: () => Navigator.of(context).pop(),
        ),
      ),
    ),
  );

  void _openInbox(BuildContext context) {
    final client = widget.programmingClient;
    if (client == null) return;
    showDialog<void>(
      context: context,
      builder: (_) => Dialog(
        child: InboxPanel(client: client, workspaceId: store.currentProject.id),
      ),
    );
  }

  ConnectionsRequest? get _connectionsRequest {
    final client = widget.programmingClient;
    if (client == null) return null;
    return (path, {body}) =>
        client.connectionsRequest(store.currentProject.id, path, body: body);
  }

  void _startFromPrompt(StarterPrompt prompt) {
    setState(() => _firstRunDismissed.add(store.currentProject.id));
    store.currentConversation.draft = prompt.prompt;
    store.save();
  }

  Future<void> _launchApp(String launchKey) async {
    if (const {'files', 'images', 'mydata', 'behaviors'}.contains(launchKey)) {
      store.launchLocalApp(launchKey);
      return;
    }
    final client = widget.programmingClient;
    if (client == null) {
      _messenger.currentState?.showSnackBar(
        const SnackBar(content: Text('Connect to IntoChat to open this app.')),
      );
      return;
    }
    try {
      final consent = await client.consentSheet(store.currentProject.id, launchKey);
      if (!consent.approved) {
        if (!mounted) return;
        final approved = await showConsentSheet(
          context,
          sheet: consent,
        );
        if (!approved) return;
        await client.approveConsent(store.currentProject.id, launchKey);
      }
      final snapshot = await client.openApp(store.currentProject.id, launchKey);
      if (mounted) store.reconcileWorkspace(store.currentProject, snapshot);
    } catch (error) {
      if (mounted) {
        _messenger.currentState?.showSnackBar(
          SnackBar(content: Text('Could not open the app. $error')),
        );
      }
    }
  }

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
