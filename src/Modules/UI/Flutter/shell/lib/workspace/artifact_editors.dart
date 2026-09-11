import 'dart:convert';
import 'dart:typed_data';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:markdraw/markdraw.dart' as markdraw;

import '../chat/brain_graph_store.dart';
import 'workspace_store.dart';

/// Use the editor's own serializer for new documents as well as saved edits.
String createWorkspaceDrawingSource() {
  final controller = markdraw.MarkdrawController();
  try {
    return controller.serializeScene();
  } finally {
    controller.dispose();
  }
}

class WorkspaceArtifactEditor extends StatelessWidget {
  const WorkspaceArtifactEditor({
    super.key,
    required this.artifact,
    required this.onChanged,
    this.tableController,
    this.graph,
  });
  final WorkspaceArtifact artifact;
  final ValueChanged<WorkspaceArtifact> onChanged;
  final KitTableController? tableController;
  final BrainGraphStore? graph;

  @override
  Widget build(BuildContext context) => switch (artifact.kind) {
    'table' =>
      tableController == null
          ? const Center(child: Text('Loading table…'))
          : SingleChildScrollView(
              padding: const EdgeInsets.all(12),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Align(
                    alignment: Alignment.centerLeft,
                    child: TextButton.icon(
                      icon: const Icon(Icons.checklist, size: 18),
                      label: Text(
                        'Select rows for context (${(artifact.viewState['selectedRowIds'] as List? ?? []).length})',
                      ),
                      onPressed: () => _selectRows(context),
                    ),
                  ),
                  KitDataTable(controller: tableController!, active: true),
                ],
              ),
            ),
    'diagram' => _DiagramEditor(
      key: ValueKey(artifact.id),
      artifact: artifact,
      onChanged: onChanged,
    ),
    'image' => _ImageEditor(
      key: ValueKey(artifact.id),
      artifact: artifact,
      onChanged: onChanged,
    ),
    'brain' => _BrainEditor(
      artifact: artifact,
      graph: graph,
      onChanged: onChanged,
    ),
    _ => const Center(child: Text('This artifact type cannot be edited yet.')),
  };

  Future<void> _selectRows(BuildContext context) async {
    final snapshot = tableController!.snapshot;
    final selected = (artifact.viewState['selectedRowIds'] as List? ?? [])
        .whereType<String>()
        .toSet();
    final result = await showDialog<Set<String>>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, setState) => AlertDialog(
          title: const Text('Rows included as context'),
          content: SizedBox(
            width: 440,
            height: 320,
            child: ListView(
              children: [
                const Text('Choose from the currently loaded table page.'),
                for (final row in snapshot.rows)
                  CheckboxListTile(
                    title: Text(
                      row.cells
                          .take(3)
                          .map((value) => value?.toString() ?? '')
                          .join(' · '),
                    ),
                    value: selected.contains(row.id),
                    onChanged: (value) => setState(() {
                      if (value == true) {
                        selected.add(row.id);
                      } else {
                        selected.remove(row.id);
                      }
                    }),
                  ),
              ],
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => setState(selected.clear),
              child: const Text('Clear'),
            ),
            TextButton(
              onPressed: () => Navigator.pop(context),
              child: const Text('Cancel'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(context, selected),
              child: const Text('Apply'),
            ),
          ],
        ),
      ),
    );
    if (result != null) {
      onChanged(
        artifact.copyWith(
          viewState: {...artifact.viewState, 'selectedRowIds': result.toList()},
        ),
      );
    }
  }
}

class _DiagramEditor extends StatefulWidget {
  const _DiagramEditor({
    super.key,
    required this.artifact,
    required this.onChanged,
  });
  final WorkspaceArtifact artifact;
  final ValueChanged<WorkspaceArtifact> onChanged;
  @override
  State<_DiagramEditor> createState() => _DiagramEditorState();
}

class _DiagramEditorState extends State<_DiagramEditor> {
  final _controller = markdraw.MarkdrawController();
  late final _files = markdraw.MarkdrawFileHandler(controller: _controller);
  String _lastSource = '';
  String? _failure;
  @override
  void initState() {
    super.initState();
    _load();
  }

  String get _source => widget.artifact.content.isNotEmpty
      ? widget.artifact.content
      : widget.artifact.data['source'] as String? ?? '';
  void _load() {
    _lastSource = _source;
    if (_lastSource.isEmpty) return;
    try {
      _controller.loadFromContent(_lastSource, 'drawing.markdraw');
      _failure = null;
    } catch (_) {
      _failure = 'The saved drawing could not be opened.';
    }
  }

  @override
  void didUpdateWidget(covariant _DiagramEditor oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (_source != _lastSource) _load();
  }

  void _save() {
    final source = _controller.serializeScene();
    if (source == _lastSource) return;
    _lastSource = source;
    widget.onChanged(
      widget.artifact.copyWith(
        content: source,
        data: {...widget.artifact.data, 'source': source},
      ),
    );
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Column(
    children: [
      if (_failure != null)
        MaterialBanner(
          content: Text(_failure!),
          actions: [
            TextButton(
              onPressed: () => setState(_load),
              child: const Text('Retry'),
            ),
          ],
        ),
      Expanded(
        child: markdraw.MarkdrawEditor(
          controller: _controller,
          currentThemeMode: ThemeMode.light,
          onSceneChanged: (_) => _save(),
          onSave: _save,
          onOpen: _files.open,
          onSaveAs: _files.saveAs,
          onExportPng: _files.exportPng,
          onExportSvg: _files.exportSvg,
          onImportImage: () => _files.importImage(context),
        ),
      ),
    ],
  );
}

/// Original bytes remain unchanged; presentation edits are stored separately.
Uint8List? workspaceImageBytes(WorkspaceArtifact artifact) {
  final raw = artifact.data['originalDataUrl'] ?? artifact.data['dataUrl'];
  if (raw is! String || !raw.startsWith('data:') || !raw.contains(';base64,')) {
    return null;
  }
  try {
    return base64Decode(raw.substring(raw.indexOf(',') + 1));
  } on FormatException {
    return null;
  }
}

List<double> workspaceBrightnessMatrix(double brightness) {
  final offset = brightness.clamp(-1.0, 1.0) * 255;
  return [
    1,
    0,
    0,
    0,
    offset,
    0,
    1,
    0,
    0,
    offset,
    0,
    0,
    1,
    0,
    offset,
    0,
    0,
    0,
    1,
    0,
  ];
}

class _ImageEditor extends StatefulWidget {
  const _ImageEditor({
    super.key,
    required this.artifact,
    required this.onChanged,
  });
  final WorkspaceArtifact artifact;
  final ValueChanged<WorkspaceArtifact> onChanged;
  @override
  State<_ImageEditor> createState() => _ImageEditorState();
}

class _ImageEditorState extends State<_ImageEditor> {
  bool _importing = false;
  double get _brightness =>
      ((widget.artifact.viewState['brightness'] ??
                  widget.artifact.data['brightness'] ??
                  0)
              as num)
          .toDouble()
          .clamp(-1, 1);
  int get _turns =>
      ((widget.artifact.viewState['quarterTurns'] ?? 0) as num).toInt();
  void _edit({double? brightness, int? turns}) => widget.onChanged(
    widget.artifact.copyWith(
      viewState: {
        ...widget.artifact.viewState,
        'brightness': brightness ?? _brightness,
        'quarterTurns': turns ?? _turns,
      },
    ),
  );
  Future<void> _import() async {
    setState(() => _importing = true);
    try {
      final result = await FilePicker.platform.pickFiles(
        type: FileType.image,
        withData: true,
      );
      if (!mounted || result == null) return;
      final file = result.files.single;
      final bytes = file.bytes;
      if (bytes == null || bytes.isEmpty) {
        throw StateError('The image could not be read.');
      }
      final dataUrl =
          'data:image/${file.extension ?? 'png'};base64,${base64Encode(bytes)}';
      widget.onChanged(
        widget.artifact.copyWith(
          data: {
            ...widget.artifact.data,
            'originalDataUrl': dataUrl,
            'dataUrl': dataUrl,
            'fileName': file.name,
          },
          viewState: {
            ...widget.artifact.viewState,
            'brightness': 0.0,
            'quarterTurns': 0,
          },
        ),
      );
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text('Import failed: $error')));
      }
    } finally {
      if (mounted) setState(() => _importing = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final bytes = workspaceImageBytes(widget.artifact);
    final remote = widget.artifact.data['url'] as String?;
    final hasImage = bytes != null || remote != null;
    return Column(
      children: [
        Padding(
          padding: const EdgeInsets.all(12),
          child: Wrap(
            spacing: 8,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              TextButton.icon(
                onPressed: _importing ? null : _import,
                icon: const Icon(Icons.upload_file, size: 18),
                label: const Text('Import image'),
              ),
              IconButton(
                tooltip: 'Rotate left',
                onPressed: hasImage ? () => _edit(turns: _turns - 1) : null,
                icon: const Icon(Icons.rotate_left),
              ),
              IconButton(
                tooltip: 'Rotate right',
                onPressed: hasImage ? () => _edit(turns: _turns + 1) : null,
                icon: const Icon(Icons.rotate_right),
              ),
              const Text('Brightness'),
              SizedBox(
                width: 150,
                child: Slider(
                  min: -1,
                  max: 1,
                  value: _brightness,
                  onChanged: hasImage
                      ? (value) => _edit(brightness: value)
                      : null,
                ),
              ),
              TextButton(
                onPressed: hasImage
                    ? () => _edit(brightness: 0, turns: 0)
                    : null,
                child: const Text('Reset'),
              ),
            ],
          ),
        ),
        Expanded(
          child: hasImage
              ? InteractiveViewer(
                  minScale: 0.1,
                  maxScale: 8,
                  child: Center(
                    child: RotatedBox(
                      quarterTurns: _turns,
                      child: ColorFiltered(
                        colorFilter: ColorFilter.matrix(
                          workspaceBrightnessMatrix(_brightness),
                        ),
                        child: bytes != null
                            ? Image.memory(
                                bytes,
                                fit: BoxFit.contain,
                                errorBuilder: (_, error, stack) => const Text(
                                  'This image format could not be displayed.',
                                ),
                              )
                            : Image.network(
                                remote!,
                                fit: BoxFit.contain,
                                errorBuilder: (_, error, stack) => const Text(
                                  'The image could not be loaded.',
                                ),
                              ),
                      ),
                    ),
                  ),
                )
              : Center(
                  child: OutlinedButton.icon(
                    onPressed: _importing ? null : _import,
                    icon: const Icon(Icons.add_photo_alternate_outlined),
                    label: const Text('Choose an image'),
                  ),
                ),
        ),
      ],
    );
  }
}

class _BrainEditor extends StatelessWidget {
  const _BrainEditor({
    required this.artifact,
    required this.graph,
    required this.onChanged,
  });
  final WorkspaceArtifact artifact;
  final BrainGraphStore? graph;
  final ValueChanged<WorkspaceArtifact> onChanged;
  BrainSnapshot? get _saved {
    try {
      return BrainSnapshot.fromJson(artifact.data);
    } catch (_) {
      return null;
    }
  }

  @override
  Widget build(BuildContext context) =>
      graph == null || artifact.data['live'] != true
      ? _build(context, _saved)
      : ListenableBuilder(
          listenable: graph!,
          builder: (context, _) => _build(context, graph!.snapshot ?? _saved),
        );
  Widget _build(BuildContext context, BrainSnapshot? snapshot) {
    if (snapshot == null) {
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              graph?.failure ??
                  'Connect a live brain to see neurons and synapses.',
            ),
            if (graph?.read != null)
              TextButton(
                onPressed: graph!.refresh,
                child: const Text('Refresh'),
              ),
          ],
        ),
      );
    }
    final selected = artifact.viewState['selectedId'] as String?;
    final node = snapshot.nodes.where((n) => n.id == selected).firstOrNull;
    final edge = snapshot.synapses.where((e) => e.id == selected).firstOrNull;
    void select(String id) => onChanged(
      artifact.copyWith(viewState: {...artifact.viewState, 'selectedId': id}),
    );
    return Column(
      children: [
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
          child: Row(
            children: [
              Text(
                artifact.data['live'] == true
                    ? 'Live brain'
                    : 'Scenario draft · not running',
              ),
              const Spacer(),
              if (artifact.data['live'] != true)
                TextButton.icon(
                  icon: const Icon(Icons.data_object, size: 18),
                  label: const Text('Edit source'),
                  onPressed: () => _editSource(context),
                ),
            ],
          ),
        ),
        if (graph?.failure != null)
          Padding(
            padding: const EdgeInsets.all(8),
            child: Text(graph!.failure!),
          ),
        Expanded(
          child: LumenBrainGraph(
            snapshot: snapshot,
            selectedId: selected,
            stale: graph?.stale ?? false,
            activeNodes: graph?.activeNodes ?? const {},
            activeEdges: graph?.activeEdges ?? const {},
            onNeuron: (value) => select(value.id),
            onSynapse: (value) => select(value.id),
          ),
        ),
        if (node != null || edge != null)
          Container(
            width: double.infinity,
            padding: const EdgeInsets.all(14),
            decoration: const BoxDecoration(
              border: Border(top: BorderSide(color: Color(0xffe5e1d8))),
            ),
            child: SelectableText(
              node != null
                  ? '${node.label} · ${node.type}\n${node.status} · ${node.module}\nSignals: ${node.handledSignals.join(', ')}'
                  : '${edge!.signalType}\n${edge.sourceId} → ${edge.targetId}\n${edge.kind} · ${edge.fireCount} deliveries',
            ),
          ),
      ],
    );
  }

  Future<void> _editSource(BuildContext context) async {
    final data = Map<String, dynamic>.from(
      artifact.data,
    )..removeWhere((key, value) => key.startsWith('_') || key == 'editorState');
    final controller = TextEditingController(
      text: const JsonEncoder.withIndent('  ').convert(data),
    );
    String? failure;
    final result = await showDialog<Map<String, dynamic>>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, setState) => AlertDialog(
          title: const Text('Edit scenario neurons and synapses'),
          content: SizedBox(
            width: 660,
            height: 420,
            child: Column(
              children: [
                const Text(
                  'This changes the saved draft. It does not activate a live automation.',
                ),
                const SizedBox(height: 8),
                Expanded(
                  child: TextField(
                    controller: controller,
                    maxLines: null,
                    expands: true,
                    style: const TextStyle(
                      fontFamily: 'monospace',
                      fontSize: 12,
                    ),
                  ),
                ),
                if (failure != null)
                  Text(failure!, style: const TextStyle(color: Colors.red)),
              ],
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context),
              child: const Text('Cancel'),
            ),
            FilledButton(
              onPressed: () {
                try {
                  final parsed = Map<String, dynamic>.from(
                    jsonDecode(controller.text) as Map,
                  );
                  final snapshot = BrainSnapshot.fromJson(parsed);
                  final ids = snapshot.nodes.map((node) => node.id).toSet();
                  if (ids.length != snapshot.nodes.length ||
                      snapshot.synapses.any(
                        (edge) =>
                            !ids.contains(edge.sourceId) ||
                            !ids.contains(edge.targetId),
                      )) {
                    throw const FormatException(
                      'Use unique neuron IDs and existing endpoints for every synapse.',
                    );
                  }
                  Navigator.pop(context, parsed);
                } catch (error) {
                  setState(() => failure = 'Invalid brain source: $error');
                }
              },
              child: const Text('Save draft'),
            ),
          ],
        ),
      ),
    );
    if (result != null) {
      onChanged(artifact.copyWith(data: {...artifact.data, ...result}));
    }
    // A dialog route retains its field until its dismissal animation completes.
    await Future<void>.delayed(const Duration(milliseconds: 300));
    controller.dispose();
  }
}
