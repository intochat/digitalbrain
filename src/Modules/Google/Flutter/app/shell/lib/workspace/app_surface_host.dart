import 'dart:typed_data';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:uuid/uuid.dart';

import 'workspace_store.dart';

class AppSurfaceHost extends StatefulWidget {
  const AppSurfaceHost({
    super.key,
    required this.client,
    required this.workspace,
    required this.artifact,
    required this.onImage,
    required this.onChanged,
    required this.onSaved,
  });
  final DigitalBrainUiClient client;
  final String workspace;
  final WorkspaceArtifact artifact;
  final ValueChanged<Map<String, dynamic>> onImage;
  final Future<void> Function() onChanged;
  final VoidCallback onSaved;
  @override
  State<AppSurfaceHost> createState() => _AppSurfaceHostState();
}

class _AppSurfaceHostState extends State<AppSurfaceHost> {
  Map<String, dynamic>? listing, document, editorUi;
  Uint8List? bytes;
  String? error, folder;
  bool editing = false;
  final sessions = <String, ImageCanvasSession>{};
  String sort = 'name', filter = '';
  int offset = 0, generation = 0, nodeRevision = 0;
  String? requestedImage;
  final search = TextEditingController();
  bool loading = false;
  String get app => widget.artifact.data['app'] as String;
  String? get selected => widget.artifact.data['selected'] as String?;
  @override
  void initState() {
    super.initState();
    load();
  }

  @override
  void didUpdateWidget(AppSurfaceHost old) {
    super.didUpdateWidget(old);
    if (app == 'images' &&
        selected != document?['id'] &&
        (!loading || selected != requestedImage)) {
      load();
    }
    if (app == 'files' && widget.artifact.data['refresh'] != _refresh) {
      _refresh = widget.artifact.data['refresh'];
      load();
    }
  }

  Object? _refresh;
  Future<void> load() async {
    final request = ++generation;
    requestedImage = selected;
    setState(() {
      loading = true;
      error = null;
    });
    try {
      if (app == 'files') {
        final query = Uri(
          queryParameters: {
            'offset': '$offset',
            'sort': sort,
            'filter': filter,
            'folderId': ?folder,
          },
        ).query;
        final result = await widget.client.appRequest(
          widget.workspace,
          'files?$query',
        );
        if (mounted && request == generation) {
          setState(() {
            listing = result;
            folder = result['page']['folderId'] as String;
          });
        }
      } else {
        final ui = await widget.client.appRequest(
          widget.workspace,
          'images-ui',
        );
        if (!mounted || request != generation) return;
        editorUi = ui;
        final tabs = ui['tabs'] as Map;
        if (selected == null &&
            (tabs['selectedId'] as String? ?? '').isNotEmpty) {
          widget.artifact.data['selected'] = tabs['selectedId'];
          requestedImage = selected;
          widget.onChanged();
        }
        if (selected == null) return;
        final result = await widget.client.appRequest(
          widget.workspace,
          'images/$selected',
        );
        final source = await widget.client.appAsset(
          widget.workspace,
          result['asset']['id'] as String,
        );
        if (mounted && request == generation) {
          setState(() {
            document = result;
            nodeRevision++;
            bytes = source;
            final pending =
                (widget.artifact.data['pendingEdits'] as Map?)?[result['id']]
                    as Map?;
            if (pending != null) {
              sessions
                  .putIfAbsent(result['id'] as String, ImageCanvasSession.new)
                  .pendingEdit = ImageRecipe.fromJson(
                Map<String, dynamic>.from(pending['command']['recipe']),
              );
            }
          });
        }
      }
    } catch (e) {
      if (mounted && request == generation) setState(() => error = '$e');
    } finally {
      if (mounted && request == generation) setState(() => loading = false);
    }
  }

  Future<Map<String, dynamic>> node(String kind, String name) =>
      widget.client.appRequest(
        widget.workspace,
        'node?${Uri(queryParameters: {'kind': kind, 'name': name}).query}',
      );
  Future<void> dispatch(Map<String, dynamic> event) async {
    try {
      if (event['kind'] == 'tabs' && editing) return;
      await widget.client.appRequest(widget.workspace, 'event', body: event);
      if (event['kind'] == 'tabs') {
        widget.artifact.data['selected'] = event['value'];
        widget.onChanged();
        await load();
        return;
      }
      if (event['kind'] == 'collection') return;
      if (event['kind'] == 'textfield') {
        filter = event['value'] as String;
        offset = 0;
      }
      final action = event['action'] as String? ?? '';
      if (action.startsWith('folder:')) {
        folder = action.substring(7);
        offset = 0;
      }
      if (action == 'sort') {
        sort = switch (sort) {
          'name' => 'date',
          'date' => 'size',
          _ => 'name',
        };
      }
      if (action == 'previous') offset = (offset - 100).clamp(0, 100000);
      if (action == 'next') {
        offset = listing?['page']['nextOffset'] as int? ?? offset;
      }
      await load();
    } catch (e) {
      if (mounted) setState(() => error = '$e');
    }
  }

  Future<void> activateItem(Map<String, dynamic> item) async {
    try {
      if (item['collection'] != null) {
        await widget.client.appRequest(
          widget.workspace,
          'event',
          body: {
            'kind': 'collection',
            'name': item['collection'],
            'action': 'activate',
            'value': item['id'],
            'revision': item['revision'],
          },
        );
      }
    } catch (e) {
      if (mounted) setState(() => error = '$e');
      return;
    }
    if (item['kind'] == 'folder') {
      folder = item['id'] as String;
      offset = 0;
      await load();
      return;
    }
    try {
      final result = await widget.client.appRequest(
        widget.workspace,
        'files/open',
        body: {'entryId': item['id']},
      );
      if (mounted) widget.onImage(result);
    } catch (e) {
      if (mounted) setState(() => error = '$e');
    }
  }

  Map<String, dynamic> get pendingSaves => Map<String, dynamic>.from(
    widget.artifact.data['pendingSaves'] as Map? ?? {},
  );
  Future<void> rememberSave(String id, Map<String, dynamic>? operation) async {
    final pending = pendingSaves;
    if (operation == null) {
      pending.remove(id);
    } else {
      pending[id] = operation;
    }
    widget.artifact.data['pendingSaves'] = pending;
    await widget.onChanged();
  }

  Future<void> edit(ImageRecipe recipe, Map<String, dynamic> current) async {
    final id = current['id'] as String;
    final pending = Map<String, dynamic>.from(
      widget.artifact.data['pendingEdits'] as Map? ?? {},
    );
    final previous = pending[id] as Map?;
    final command = {'kind': 'replace', 'recipe': recipe.toJson()};
    final operation =
        previous != null &&
            jsonEncode(previous['command']) == jsonEncode(command)
        ? Map<String, dynamic>.from(previous)
        : <String, dynamic>{
            'command': command,
            'expectedRevision': current['revision'],
            'operationId': const Uuid().v4(),
          };
    pending[id] = operation;
    widget.artifact.data['pendingEdits'] = pending;
    await widget.onChanged();
    final result = await widget.client.appRequest(
      widget.workspace,
      'images/$id/edit',
      body: operation,
    );
    pending.remove(id);
    widget.artifact.data['pendingEdits'] = pending;
    await widget.onChanged();
    if (mounted && document?['id'] == id) {
      setState(() {
        document = result;
        nodeRevision++;
      });
    }
  }

  Future<void> save(Uint8List png, Map<String, dynamic> current) async {
    final id = current['id'] as String;
    final pending = pendingSaves[id] as Map?;
    final operation =
        pending != null && pending['revision'] == current['revision']
        ? Map<String, dynamic>.from(pending)
        : <String, dynamic>{
            'id': const Uuid().v4(),
            'revision': current['revision'],
          };
    await rememberSave(id, operation);
    await widget.client.appRequest(
      widget.workspace,
      'images/$id/prepare-save',
      body: {
        'expectedRevision': current['revision'],
        'operationId': operation['id'],
      },
    );
    final result = await widget.client.saveImageCopy(
      widget.workspace,
      id,
      operation['id'] as String,
      png,
    );
    await rememberSave(id, null);
    if (!mounted) return;
    if (document?['id'] == id) {
      setState(
        () => document = Map<String, dynamic>.from(result['document'] as Map),
      );
    }
    widget.onSaved();
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(SnackBar(content: Text('Saved ${result['file']['name']}')));
  }

  @override
  Widget build(BuildContext context) {
    final renderedDocument = document;
    final renderedBytes = bytes;
    final surface = app == 'files'
        ? (listing == null ? null : listing!['state']['surface'])
        : document == null
        ? null
        : editorUi?['surface'];
    return Column(
      children: [
        if (error != null)
          Padding(
            padding: const EdgeInsets.all(12),
            child: Row(
              children: [
                Expanded(
                  child: Text(
                    error!,
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.error,
                    ),
                  ),
                ),
                TextButton(onPressed: load, child: const Text('Retry')),
              ],
            ),
          ),
        if (loading) const LinearProgressIndicator(minHeight: 2),
        Expanded(
          child: surface == null
              ? Center(
                  child: Text(
                    app == 'images'
                        ? 'Open an image from Files'
                        : loading
                        ? 'Opening Downloads…'
                        : 'Downloads unavailable',
                  ),
                )
              : NeuronView(
                  kind: surface['kind'] as String,
                  name: surface['name'] as String,
                  load: node,
                  revision: app == 'files'
                      ? listing!['state']['revision'] as int
                      : nodeRevision,
                  onActivate: activateItem,
                  onAction: dispatch,
                  enabled: !editing && !loading,
                  imageBuilder: (definition) =>
                      renderedBytes == null ||
                          renderedDocument == null ||
                          definition['assetId'] !=
                              renderedDocument['asset']['id']
                      ? const Center(child: CircularProgressIndicator())
                      : UiImageCanvas(
                          key: ValueKey(renderedDocument['id']),
                          bytes: renderedBytes,
                          session: sessions.putIfAbsent(
                            renderedDocument['id'] as String,
                            ImageCanvasSession.new,
                          ),
                          onBusyChanged: (value) {
                            if (mounted) setState(() => editing = value);
                          },
                          sourceWidth: definition['width'] as int,
                          sourceHeight: definition['height'] as int,
                          recipe: ImageRecipe.fromJson(
                            Map<String, dynamic>.from(
                              renderedDocument['recipe'],
                            ),
                          ),
                          onEdit: (recipe) => edit(recipe, renderedDocument),
                          onSave: (png) => save(png, renderedDocument),
                          saved:
                              document!['lastSavedRevision'] ==
                              document!['revision'],
                        ),
                ),
        ),
      ],
    );
  }

  @override
  void dispose() {
    generation++;
    search.dispose();
    super.dispose();
  }
}
