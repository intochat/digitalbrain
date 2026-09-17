import 'dart:async';
import 'dart:convert';
import 'dart:math' as math;

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';
import 'package:uuid/uuid.dart';

class WorkspacePrograms extends StatefulWidget {
  const WorkspacePrograms({
    super.key,
    required this.client,
    this.initialProgram,
  });
  final DigitalBrainUiClient client;
  final Map<String, dynamic>? initialProgram;

  @override
  State<WorkspacePrograms> createState() => _WorkspaceProgramsState();
}

class _WorkspaceProgramsState extends State<WorkspacePrograms> {
  static const _encoder = JsonEncoder.withIndent('  ');
  static const _uuid = Uuid();
  static const _nodeKinds = [
    'input',
    'template',
    'filter',
    'map',
    'aggregate',
    'output',
    'call',
    'agent',
    'code',
  ];
  final _intent = TextEditingController();
  final _source = TextEditingController();
  final _input = TextEditingController(
    text: '{"name": "IntoChat", "score": 80}',
  );
  final _runId = TextEditingController();
  final _search = TextEditingController();
  List<Map<String, dynamic>> _programs = [], _examples = [];
  Map<String, dynamic>? _snapshot, _validation, _run;
  String? _failure, _notice;
  bool _busy = false, _dirty = false, _showSource = false;
  Timer? _poll;
  int _selection = 0;
  int _baseVersion = 0;
  String? _baseDefinition;

  Map<String, dynamic> _map(Object? value) =>
      Map<String, dynamic>.from(value as Map);
  String _path(String value) => Uri.encodeComponent(value);
  String _newId(String prefix) {
    var safe = prefix.replaceAll(RegExp('[^A-Za-z0-9_-]'), '-');
    safe = safe.replaceFirst(RegExp('^[-_]+'), '');
    if (safe.isEmpty) safe = 'program';
    if (safe.length > 15) safe = safe.substring(0, 15);
    return '$safe-${_uuid.v4().replaceAll('-', '')}';
  }

  Map<String, dynamic> get _definition => _map(jsonDecode(_source.text));
  List<Map<String, dynamic>> _maps(Object? values) =>
      values is List ? values.whereType<Map>().map(_map).toList() : [];
  List<Map<String, dynamic>> get _runs => [
    for (final run in _snapshot?['runs'] as List? ?? [])
      if (run is Map) _map(run) else {'runId': '$run'},
  ];

  @override
  void initState() {
    super.initState();
    _newDraft();
    final initial = widget.initialProgram;
    if (initial?['definition'] is Map) {
      _draft(_map(initial!['definition']));
      _validation = initial['validation'] is Map
          ? _map(initial['validation'])
          : null;
    }
    unawaited(
      _perform(() async {
        await _refresh();
        if (initial?['definition'] is Map) {
          final definition = _map(initial!['definition']);
          final snapshot = _map(
            await _request('GET', '/${_path('${definition['id']}')}'),
          );
          if (!mounted) return;
          setState(() {
            if (initial['version'] != null) {
              _accept(snapshot);
            } else {
              _associateDraft(snapshot);
            }
          });
        }
      }),
    );
  }

  @override
  void dispose() {
    _poll?.cancel();
    for (final controller in [_intent, _source, _input, _runId, _search]) {
      controller.dispose();
    }
    super.dispose();
  }

  Future<Object?> _request(
    String method,
    String path, [
    Map<String, Object?>? body,
  ]) => widget.client.programmingRequest(method, path, body: body);

  Future<void> _perform(Future<void> Function() action) async {
    if (_busy) return;
    setState(() {
      _busy = true;
      _failure = null;
      _notice = null;
    });
    try {
      await action();
    } catch (error) {
      if (mounted) setState(() => _failure = '$error');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _refresh({bool refreshSelection = false}) async {
    final results = await Future.wait([
      _request('GET', ''),
      _request('GET', '/examples'),
    ]);
    if (!mounted) return;
    setState(() {
      _programs = _maps(results[0]);
      _examples = _maps(results[1]);
    });
    if (refreshSelection) await _refreshSelected();
  }

  void _draft(Map<String, dynamic> definition) {
    _poll?.cancel();
    _selection++;
    _source.text = _encoder.convert(definition);
    _snapshot = null;
    _baseVersion = 0;
    _baseDefinition = null;
    _validation = null;
    _run = null;
    _runId.clear();
    _failure = null;
    _notice = null;
    _dirty = true;
  }

  void _newDraft() => _draft({
    'id': _newId('program'),
    'name': 'My living program',
    'description': 'A greeting that you can change while IntoChat is running.',
    'trigger': 'Manual',
    'nodes': [
      {
        'id': 'input',
        'kind': 'input',
        'inputType': 'Json',
        'outputType': 'Json',
        'config': <String, dynamic>{},
      },
      {
        'id': 'greeting',
        'kind': 'template',
        'inputType': 'Json',
        'outputType': 'Json',
        'config': {'text': 'Hello {{input.name}}'},
      },
      {
        'id': 'result',
        'kind': 'output',
        'inputType': 'Json',
        'outputType': 'Json',
        'config': <String, dynamic>{},
      },
    ],
    'synapses': [
      {'from': 'input', 'to': 'greeting'},
      {'from': 'greeting', 'to': 'result'},
    ],
  });

  void _accept(Map<String, dynamic> snapshot) {
    if (snapshot['definition'] is! Map) {
      throw StateError(
        'This program has not been deployed. Choose a program from the library or create a new draft.',
      );
    }
    _poll?.cancel();
    _selection++;
    _snapshot = snapshot;
    _baseVersion = (snapshot['version'] as num).toInt();
    _baseDefinition = jsonEncode(snapshot['definition']);
    _source.text = _encoder.convert(snapshot['definition']);
    _dirty = false;
    _validation = null;
    _run = null;
    _runId.clear();
  }

  void _associateDraft(Map<String, dynamic> snapshot) {
    if (snapshot['definition'] is! Map) return;
    _snapshot = snapshot;
    _baseVersion = (snapshot['version'] as num).toInt();
    _baseDefinition = jsonEncode(snapshot['definition']);
    _notice =
        'This draft updates ${snapshot['definition']['name']} at version $_baseVersion. Review it, then deploy changes.';
  }

  void _updateServerSnapshot(Map<String, dynamic> latest) {
    final definition = latest['definition'];
    if (definition is! Map) {
      throw StateError(
        'The selected program is no longer deployed. Your draft is still available.',
      );
    }
    final fingerprint = jsonEncode(definition);
    if (fingerprint == _baseDefinition) {
      _baseVersion = (latest['version'] as num).toInt();
    } else if (!_dirty) {
      _source.text = _encoder.convert(definition);
      _baseDefinition = fingerprint;
      _baseVersion = (latest['version'] as num).toInt();
      _validation = null;
    } else {
      _notice =
          'The deployed program changed while you were editing. Your draft is based on version $_baseVersion; reload the server version before deploying.';
    }
    _snapshot = latest;
  }

  Future<void> _open(String id) => _perform(() async {
    final snapshot = _map(await _request('GET', '/${_path(id)}'));
    if (!mounted) return;
    setState(() => _accept(snapshot));
  });

  void _changedSource() => setState(() {
    _dirty = true;
    _validation = null;
    _notice = null;
  });

  void _write(Map<String, dynamic> definition) {
    _source.text = _encoder.convert(definition);
    _changedSource();
  }

  Future<void> _compile() => _perform(() async {
    if (_intent.text.trim().isEmpty) {
      throw const FormatException('Describe the behavior you want to create.');
    }
    final result = _map(
      await _request('POST', '/compile', {'intent': _intent.text.trim()}),
    );
    final definition = _map(result['definition']);
    final existing =
        RegExp(r'^[A-Za-z0-9][A-Za-z0-9_-]{0,47}$')
            .hasMatch('${definition['id']}')
        ? _map(await _request('GET', '/${_path('${definition['id']}')}'))
        : <String, dynamic>{};
    if (!mounted) return;
    setState(() {
      _draft(definition);
      _validation = result['validation'] is Map
          ? _map(result['validation'])
          : null;
      _notice =
          'Draft ready. Review its neurons and connections, then deploy it.';
      _associateDraft(existing);
    });
  });

  Future<void> _validate() => _perform(() async {
    final result = _map(
      await _request('POST', '/validate', {'definition': _definition}),
    );
    if (mounted) setState(() => _validation = result);
  });

  Future<void> _deploy() => _perform(() async {
    final definition = _definition;
    if (_snapshot != null && definition['id'] != _snapshot!['id']) {
      throw const FormatException(
        'A deployed program keeps its ID. Use Duplicate to create a separate program.',
      );
    }
    final snapshot = _map(
      await _request('PUT', '/${_path('${definition['id']}')}', {
        'definition': definition,
        'expectedVersion': _baseVersion,
      }),
    );
    if (!mounted) return;
    setState(() {
      _accept(snapshot);
      _notice =
          'Version ${snapshot['version']} is live. Send a signal to try it.';
    });
    await _refresh();
  });

  Future<void> _toggle() => _perform(() async {
    final snapshot = _snapshot!;
    final result = _map(
      await _request('POST', '/${_path('${snapshot['id']}')}/enabled', {
        'enabled': snapshot['enabled'] != true,
        'expectedVersion': snapshot['version'],
      }),
    );
    if (!mounted) return;
    setState(() {
      _updateServerSnapshot(result);
      _notice = result['enabled'] == true
          ? 'Program resumed.'
          : 'Program paused.';
    });
    await _refresh();
  });

  Future<void> _rollback(int version) => _perform(() async {
    final snapshot = _snapshot!;
    final result = _map(
      await _request('POST', '/${_path('${snapshot['id']}')}/rollback', {
        'version': version,
        'expectedVersion': snapshot['version'],
      }),
    );
    if (!mounted) return;
    setState(() {
      _accept(result);
      _notice = 'Restored version $version as version ${result['version']}.';
    });
    await _refresh();
  });

  bool get _running => [
    'running',
    'pending',
    'queued',
  ].contains('${_run?['status']}'.toLowerCase());

  Future<void> _sendSignal() => _perform(() async {
    final snapshot = _snapshot!;
    final result = _map(
      await _request('POST', '/${_path('${snapshot['id']}')}/run', {
        'input': jsonDecode(_input.text),
        if (_runId.text.trim().isNotEmpty) 'runId': _runId.text.trim(),
      }),
    );
    if (!mounted) return;
    setState(() => _run = result);
    if (_running) {
      _schedulePoll(_selection, '${snapshot['id']}', '${result['runId']}');
    }
    await _refreshSelected();
  });

  Future<void> _refreshSelected() async {
    final snapshot = _snapshot;
    if (snapshot == null) return;
    final selected = _selection;
    final latest = _map(
      await _request('GET', '/${_path('${snapshot['id']}')}'),
    );
    if (!mounted || selected != _selection) return;
    setState(() => _updateServerSnapshot(latest));
  }

  void _schedulePoll(int selection, String id, String runId) {
    _poll?.cancel();
    _poll = Timer(const Duration(seconds: 1), () async {
      try {
        final result = _map(
          await _request('GET', '/${_path(id)}/runs/${_path(runId)}'),
        );
        if (!mounted || selection != _selection) return;
        setState(() => _run = result);
        if (_running) {
          _schedulePoll(selection, id, runId);
        } else {
          await _refreshSelected();
        }
      } catch (error) {
        if (mounted && selection == _selection) {
          setState(() => _failure = '$error');
        }
      }
    });
  }

  Future<void> _readRun(String runId) => _perform(() async {
    final snapshot = _snapshot!;
    final result = _map(
      await _request(
        'GET',
        '/${_path('${snapshot['id']}')}/runs/${_path(runId)}',
      ),
    );
    if (!mounted) return;
    _poll?.cancel();
    _selection++;
    setState(() => _run = result);
    if (_running) {
      _schedulePoll(_selection, '${snapshot['id']}', runId);
    }
  });

  Future<void> _cancelRun() => _perform(() async {
    final id = '${_snapshot!['id']}';
    final runId = '${_run!['runId']}';
    await _request('POST', '/${_path(id)}/runs/${_path(runId)}/cancel');
    final result = _map(
      await _request('GET', '/${_path(id)}/runs/${_path(runId)}'),
    );
    if (!mounted) return;
    setState(() => _run = result);
    if (_running) {
      _schedulePoll(_selection, id, runId);
    } else {
      _poll?.cancel();
    }
    await _refreshSelected();
  });

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Living programs'),
      actions: [
        IconButton(
          tooltip: 'Refresh programs',
          onPressed: _busy
              ? null
              : () => _perform(() => _refresh(refreshSelection: true)),
          icon: const Icon(Icons.refresh),
        ),
        TextButton.icon(
          onPressed: _busy ? null : () => setState(_newDraft),
          icon: const Icon(Icons.add),
          label: const Text('New program'),
        ),
        const SizedBox(width: 12),
      ],
    ),
    body: Column(
      children: [
        if (_busy) const LinearProgressIndicator(minHeight: 2),
        if (_failure != null) _message(_failure!, error: true),
        if (_notice != null) _message(_notice!),
        if (_snapshot != null &&
            _dirty &&
            (_failure != null || _baseVersion != _snapshot!['version']))
          Align(
            alignment: Alignment.centerRight,
            child: TextButton(
              onPressed: _busy ? null : () => _open('${_snapshot!['id']}'),
              child: const Text('Reload deployed version'),
            ),
          ),
        Expanded(
          child: LayoutBuilder(
            builder: (context, constraints) {
              if (constraints.maxWidth < 850) {
                return ListView(
                  children: [
                    SizedBox(height: 250, child: _library()),
                    const Divider(height: 1),
                    _editor(),
                  ],
                );
              }
              return Row(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  SizedBox(width: 260, child: _library()),
                  const VerticalDivider(width: 1),
                  Expanded(child: SingleChildScrollView(child: _editor())),
                ],
              );
            },
          ),
        ),
      ],
    ),
  );

  Widget _message(String message, {bool error = false}) => Semantics(
    liveRegion: true,
    container: true,
    label: message,
    child: Container(
      width: double.infinity,
      color: error
          ? Theme.of(context).colorScheme.errorContainer
          : Theme.of(context).colorScheme.primaryContainer,
      padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 10),
      child: ExcludeSemantics(
        child: SelectableText(
          message,
          style: TextStyle(
            color: error
                ? Theme.of(context).colorScheme.onErrorContainer
                : Theme.of(context).colorScheme.onPrimaryContainer,
          ),
        ),
      ),
    ),
  );

  Widget _library() => Column(
    children: [
      Padding(
        padding: const EdgeInsets.all(16),
        child: TextField(
          controller: _search,
          decoration: const InputDecoration(
            labelText: 'Find a program',
            prefixIcon: Icon(Icons.search),
            isDense: true,
          ),
          onChanged: (_) => setState(() {}),
        ),
      ),
      Expanded(
        child: ListView(
          children: [
            if (_programs.isEmpty)
              const Padding(
                padding: EdgeInsets.all(20),
                child: Text(
                  'Deploy a program to keep its behavior running here.',
                ),
              ),
            for (final program in _programs.where(
              (p) => '${p['id']} ${p['definition']?['name'] ?? p['name']}'
                  .toLowerCase()
                  .contains(_search.text.toLowerCase()),
            ))
              ListTile(
                selected: _snapshot?['id'] == program['id'],
                enabled: !_busy,
                leading: Icon(
                  program['enabled'] == true
                      ? Icons.play_circle_outline
                      : Icons.pause_circle_outline,
                  size: 21,
                ),
                title: Text(
                  '${program['definition']?['name'] ?? program['name'] ?? program['id']}',
                ),
                subtitle: Text(
                  'v${program['version']} · ${program['enabled'] == true ? 'Live' : 'Paused'}',
                ),
                onTap: () => _open('${program['id']}'),
              ),
          ],
        ),
      ),
    ],
  );

  Widget _editor() {
    Map<String, dynamic>? definition;
    try {
      definition = _definition;
    } catch (_) {
      /* Source remains editable. */
    }
    final nodes = definition == null
        ? <Map<String, dynamic>>[]
        : _maps(definition['nodes']);
    final edges = definition == null
        ? <Map<String, dynamic>>[]
        : _maps(definition['synapses']);
    final versions = _maps(_snapshot?['versions']);
    return Padding(
      padding: const EdgeInsets.all(24),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Change how IntoChat works',
            style: Theme.of(context).textTheme.headlineSmall,
          ),
          const SizedBox(height: 8),
          const Text(
            'Describe a behavior, review its neurons, and deploy it. Changes take effect while the system is running.',
          ),
          const SizedBox(height: 20),
          TextField(
            controller: _intent,
            minLines: 2,
            maxLines: 4,
            decoration: const InputDecoration(
              labelText: 'Describe a behavior',
              hintText: 'Greet each person by name, or filter leads whose score is at least 50',
              border: OutlineInputBorder(),
            ),
          ),
          const SizedBox(height: 12),
          Wrap(
            spacing: 12,
            runSpacing: 8,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              FilledButton.icon(
                onPressed: _busy ? null : _compile,
                icon: const Icon(Icons.auto_awesome, size: 18),
                label: const Text('Create draft'),
              ),
              PopupMenuButton<int>(
                tooltip: 'Start from an example',
                enabled: !_busy && _examples.isNotEmpty,
                onSelected: (index) => setState(() {
                  final example = _map(
                    jsonDecode(jsonEncode(_examples[index])),
                  );
                  example['id'] = _newId('${example['id']}');
                  _draft(example);
                }),
                itemBuilder: (_) => [
                  for (var i = 0; i < _examples.length; i++)
                    PopupMenuItem(
                      value: i,
                      child: Text('${_examples[i]['name']}'),
                    ),
                ],
                child: const Padding(
                  padding: EdgeInsets.all(10),
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Icon(Icons.lightbulb_outline, size: 19),
                      SizedBox(width: 8),
                      Text('Examples'),
                      Icon(Icons.arrow_drop_down),
                    ],
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 28),
          Row(
            children: [
              Expanded(
                child: Text(
                  '${definition?['name'] ?? 'Program source'}',
                  style: Theme.of(context).textTheme.titleLarge,
                ),
              ),
              if (_snapshot != null)
                Chip(
                  label: Text(
                    'v${_snapshot!['version']} · ${_snapshot!['enabled'] == true ? 'Live' : 'Paused'}',
                  ),
                ),
              if (_dirty)
                const Padding(
                  padding: EdgeInsets.only(left: 8),
                  child: Chip(label: Text('Draft')),
                ),
            ],
          ),
          if (definition?['description'] != null)
            Padding(
              padding: const EdgeInsets.only(top: 4),
              child: Text('${definition!['description']}'),
            ),
          const SizedBox(height: 14),
          Wrap(
            spacing: 10,
            runSpacing: 8,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              SegmentedButton<bool>(
                segments: const [
                  ButtonSegment(
                    value: false,
                    icon: Icon(Icons.hub_outlined),
                    label: Text('Neurons'),
                  ),
                  ButtonSegment(
                    value: true,
                    icon: Icon(Icons.data_object),
                    label: Text('Source'),
                  ),
                ],
                selected: {_showSource},
                onSelectionChanged: (selection) =>
                    setState(() => _showSource = selection.first),
              ),
              if (definition != null)
                TextButton.icon(
                  onPressed: _busy ? null : () => _editMetadata(definition!),
                  icon: const Icon(Icons.edit_outlined, size: 17),
                  label: const Text('Program settings'),
                ),
              OutlinedButton(
                onPressed: _busy ? null : _validate,
                child: const Text('Validate'),
              ),
              FilledButton.icon(
                onPressed: _busy || _validation?['valid'] != true
                    ? null
                    : _deploy,
                icon: const Icon(Icons.bolt, size: 18),
                label: Text(
                  _snapshot == null ? 'Deploy program' : 'Deploy changes',
                ),
              ),
            ],
          ),
          if (_validation != null)
            Padding(
              padding: const EdgeInsets.only(top: 12),
              child: _validationResult(),
            ),
          const SizedBox(height: 16),
          if (_showSource || definition == null)
            TextField(
              controller: _source,
              enabled: !_busy,
              minLines: 16,
              maxLines: 30,
              style: const TextStyle(fontFamily: 'monospace', fontSize: 13),
              decoration: const InputDecoration(
                labelText: 'Program source (JSON)',
                alignLabelWithHint: true,
                border: OutlineInputBorder(),
              ),
              onChanged: (_) => _changedSource(),
            )
          else ...[
            if (nodes.isNotEmpty) _programDiagram(definition, nodes, edges),
            const SizedBox(height: 12),
            for (final node in nodes)
              Card(
                elevation: 0,
                child: ListTile(
                  leading: Icon(_nodeIcon('${node['kind']}')),
                  title: Text('${node['id']}'),
                  subtitle: Text(
                    '${node['kind']} · ${node['inputType'] ?? 'Json'} → ${node['outputType'] ?? 'Json'}\n${_encoder.convert(node['config'] ?? {})}',
                    maxLines: 4,
                    overflow: TextOverflow.ellipsis,
                  ),
                  isThreeLine: true,
                  trailing: Wrap(
                    children: [
                      IconButton(
                        tooltip: 'Edit neuron ${node['id']}',
                        onPressed: _busy
                            ? null
                            : () => _editNode(definition!, node),
                        icon: const Icon(Icons.edit_outlined),
                      ),
                      IconButton(
                        tooltip: 'Remove neuron ${node['id']}',
                        onPressed: _busy
                            ? null
                            : () {
                                definition!['nodes'] = nodes
                                    .where((n) => n['id'] != node['id'])
                                    .toList();
                                definition['synapses'] = edges
                                    .where(
                                      (edge) =>
                                          edge['from'] != node['id'] &&
                                          edge['to'] != node['id'],
                                    )
                                    .toList();
                                _write(definition);
                              },
                        icon: const Icon(Icons.close),
                      ),
                    ],
                  ),
                ),
              ),
            const SizedBox(height: 12),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (var i = 0; i < edges.length; i++)
                  OutlinedButton.icon(
                    label: Text(
                      'Disconnect ${edges[i]['from']} from ${edges[i]['to']}',
                    ),
                    icon: const Icon(Icons.link_off, size: 17),
                    onPressed: _busy
                        ? null
                        : () {
                            edges.removeAt(i);
                            definition!['synapses'] = edges;
                            _write(definition);
                          },
                  ),
              ],
            ),
            const SizedBox(height: 8),
            Wrap(
              spacing: 10,
              children: [
                TextButton.icon(
                  onPressed: _busy ? null : () => _editNode(definition!, null),
                  icon: const Icon(Icons.add),
                  label: const Text('Add neuron'),
                ),
                TextButton.icon(
                  onPressed: _busy || nodes.length < 2
                      ? null
                      : () => _connect(definition!),
                  icon: const Icon(Icons.add_link),
                  label: const Text('Connect neurons'),
                ),
              ],
            ),
          ],
          const SizedBox(height: 20),
          if (_snapshot != null)
            Wrap(
              spacing: 10,
              runSpacing: 8,
              children: [
                OutlinedButton.icon(
                  onPressed: _busy ? null : _toggle,
                  icon: Icon(
                    _snapshot!['enabled'] == true
                        ? Icons.pause
                        : Icons.play_arrow,
                  ),
                  label: Text(
                    _snapshot!['enabled'] == true
                        ? 'Pause program'
                        : 'Resume program',
                  ),
                ),
                PopupMenuButton<int>(
                  tooltip: 'Restore a previous version',
                  enabled:
                      !_busy &&
                      versions.any(
                        (v) => v['version'] != _snapshot!['version'],
                      ),
                  onSelected: _rollback,
                  itemBuilder: (_) => [
                    for (final version in versions.where(
                      (v) => v['version'] != _snapshot!['version'],
                    ))
                      PopupMenuItem(
                        value: (version['version'] as num).toInt(),
                        child: Text('Restore version ${version['version']}'),
                      ),
                  ],
                  child: const Padding(
                    padding: EdgeInsets.all(10),
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Icon(Icons.history, size: 18),
                        SizedBox(width: 8),
                        Text('Version history'),
                        Icon(Icons.arrow_drop_down),
                      ],
                    ),
                  ),
                ),
                TextButton.icon(
                  onPressed: _busy || definition == null
                      ? null
                      : () => setState(() {
                          final copy = _definition;
                          copy['id'] = _newId('${copy['id']}');
                          copy['name'] = '${copy['name']} copy';
                          _draft(copy);
                        }),
                  icon: const Icon(Icons.copy, size: 17),
                  label: const Text('Duplicate'),
                ),
              ],
            ),
          const Divider(height: 40),
          Text('Send a signal', style: Theme.of(context).textTheme.titleLarge),
          const SizedBox(height: 8),
          Text(
            _snapshot == null
                ? 'Deploy this program first, then send an input to see every neuron react.'
                : _dirty
                ? 'Signals run the deployed version. Deploy your draft to use these changes.'
                : 'Run the deployed graph and inspect its output and durable reaction trace.',
          ),
          const SizedBox(height: 14),
          TextField(
            controller: _input,
            minLines: 3,
            maxLines: 10,
            decoration: const InputDecoration(
              labelText: 'Signal input (JSON)',
              alignLabelWithHint: true,
              border: OutlineInputBorder(),
            ),
            style: const TextStyle(fontFamily: 'monospace', fontSize: 13),
          ),
          const SizedBox(height: 12),
          TextField(
            controller: _runId,
            decoration: const InputDecoration(
              labelText: 'Run ID (optional)',
              helperText: 'Reuse an ID to inspect the same durable run without repeating its reactions.',
              isDense: true,
            ),
          ),
          const SizedBox(height: 14),
          FilledButton.icon(
            onPressed:
                _busy ||
                    _snapshot == null ||
                    _snapshot!['enabled'] != true ||
                    _running
                ? null
                : _sendSignal,
            icon: const Icon(Icons.play_arrow),
            label: Text(_running ? 'Running…' : 'Send signal'),
          ),
          if (_run != null) _runView(),
          if (_snapshot != null) ...[
            const SizedBox(height: 24),
            ExpansionTile(
              title: const Text('Run history'),
              initiallyExpanded: false,
              children: [
                for (final run in _runs.reversed.take(30))
                  ListTile(
                    title: Text('${run['runId']}'),
                    subtitle: Text(
                      run['status'] == null
                          ? 'Inspect durable reaction trace'
                          : '${run['status']} · version ${run['version']}',
                    ),
                    onTap: _busy ? null : () => _readRun('${run['runId']}'),
                  ),
                if (_runs.isEmpty)
                  const ListTile(title: Text('No signals sent yet.')),
              ],
            ),
          ],
          const SizedBox(height: 28),
        ],
      ),
    );
  }

  Widget _validationResult() {
    final valid = _validation!['valid'] == true;
    return Semantics(
      liveRegion: true,
      child: Container(
        width: double.infinity,
        padding: const EdgeInsets.all(12),
        decoration: BoxDecoration(
          color: valid
              ? Theme.of(context).colorScheme.secondaryContainer
              : Theme.of(context).colorScheme.errorContainer,
          borderRadius: BorderRadius.circular(8),
        ),
        child: Text(
          valid
              ? 'Valid graph · ${(_validation!['order'] as List? ?? []).join(' → ')}'
              : 'Cannot deploy:\n${(_validation!['errors'] as List? ?? []).join('\n')}',
        ),
      ),
    );
  }

  Widget _runView() => Padding(
    padding: const EdgeInsets.only(top: 20),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Semantics(
          liveRegion: true,
          child: Text(
            'Run ${_run!['status']} · ${_run!['runId']} · version ${_run!['version']}',
            style: Theme.of(context).textTheme.titleMedium,
          ),
        ),
        if (_run!['status'] == 'Filtered')
          const Padding(
            padding: EdgeInsets.only(top: 8),
            child: Text(
              'The signal did not match the filters. No output branch ran.',
            ),
          ),
        if (_run!['error'] != null)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 8),
            child: _readableText(
              '${_run!['error']}',
              label: 'Run error',
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
          ),
        if (_running)
          if (_maps(_snapshot?['definition']?['nodes']).any(
            (node) =>
                node['kind'] == 'agent' &&
                node['config'] is Map &&
                node['config']['mode'] == 'web',
          ))
            const Padding(
              padding: EdgeInsets.only(top: 8),
              child: Text(
                'This run may browse several pages. Research results and source URLs will appear below.',
              ),
            ),
        if (_running)
          TextButton.icon(
            onPressed: _busy ? null : _cancelRun,
            icon: const Icon(Icons.stop_circle_outlined),
            label: const Text('Cancel run'),
          ),
        const SizedBox(height: 12),
        Container(
          width: double.infinity,
          padding: const EdgeInsets.all(16),
          decoration: BoxDecoration(
            color: Theme.of(context).colorScheme.surfaceContainer,
            borderRadius: BorderRadius.circular(8),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Text(
                'Output',
                style: TextStyle(fontWeight: FontWeight.w600),
              ),
              const SizedBox(height: 8),
              _readableText(
                _encoder.convert(_run!['output']),
                label: 'Program output',
                style: const TextStyle(fontFamily: 'monospace'),
              ),
            ],
          ),
        ),
        const SizedBox(height: 12),
        for (final step in _maps(_run!['steps']))
          ExpansionTile(
            key: ValueKey('${_run!['runId']}-${step['nodeId']}'),
            leading: Icon(switch ('${step['status']}'.toLowerCase()) {
              'failed' => Icons.error_outline,
              'filtered' => Icons.filter_alt_outlined,
              'skipped' => Icons.skip_next_outlined,
              _ => Icons.check_circle_outline,
            }),
            title: Text('${step['nodeId']} · ${step['status']}'),
            subtitle: Text('${step['kind'] ?? ''}'),
            children: [
              Padding(
                padding: const EdgeInsets.all(16),
                child: Align(
                  alignment: Alignment.centerLeft,
                  child: _readableText(
                    _encoder.convert(step),
                    label: 'Reaction details ${step['nodeId']}',
                    style: const TextStyle(
                      fontFamily: 'monospace',
                      fontSize: 12,
                    ),
                  ),
                ),
              ),
            ],
          ),
      ],
    ),
  );

  Widget _readableText(
    String text, {
    required String label,
    TextStyle? style,
  }) => Semantics(
    container: true,
    label: '$label: $text',
    child: ExcludeSemantics(child: SelectableText(text, style: style)),
  );

  Widget _programDiagram(
    Map<String, dynamic> definition,
    List<Map<String, dynamic>> nodes,
    List<Map<String, dynamic>> edges,
  ) {
    final unique = {for (final node in nodes) '${node['id']}': node};
    final levels = <String, int>{};
    for (var pass = 0; pass < unique.length; pass++) {
      for (final id in unique.keys) {
        if (levels.containsKey(id)) continue;
        final parents = edges
            .where((edge) => edge['to'] == id)
            .map((edge) => '${edge['from']}')
            .toList();
        if (parents.isEmpty) {
          levels[id] = 0;
        } else if (parents.every(levels.containsKey)) {
          levels[id] =
              parents.map((parent) => levels[parent]!).reduce(math.max) + 1;
        }
      }
    }
    final rows = <int, int>{};
    final positions = <String, Rect>{};
    for (final id in unique.keys) {
      final level = levels[id] ?? 0;
      final row = rows[level] ?? 0;
      rows[level] = row + 1;
      positions[id] = Rect.fromLTWH(20 + level * 228, 20 + row * 96, 180, 72);
    }
    final width =
        positions.values.map((rect) => rect.right).reduce(math.max) + 20;
    final height =
        positions.values.map((rect) => rect.bottom).reduce(math.max) + 20;
    final colors = Theme.of(context).colorScheme;
    return Container(
      height: height.clamp(132.0, 340.0),
      clipBehavior: Clip.antiAlias,
      decoration: BoxDecoration(
        color: colors.surfaceContainerLow,
        borderRadius: BorderRadius.circular(12),
      ),
      child: LayoutBuilder(
        builder: (context, constraints) => SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          child: SingleChildScrollView(
            child: SizedBox(
              width: math.max(width, constraints.maxWidth),
              height: math.max(height, 132),
              child: Stack(
                children: [
                  Positioned.fill(
                    child: CustomPaint(
                      painter: _ProgramConnections(
                        positions,
                        edges,
                        colors.primary,
                      ),
                    ),
                  ),
                  for (final entry in unique.entries)
                    Positioned.fromRect(
                      rect: positions[entry.key]!,
                      child: OutlinedButton(
                        style: OutlinedButton.styleFrom(
                          backgroundColor: colors.surface,
                          foregroundColor: colors.onSurface,
                          side: BorderSide(color: colors.outlineVariant),
                          padding: const EdgeInsets.symmetric(horizontal: 12),
                          shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(10),
                          ),
                        ),
                        onPressed: _busy
                            ? null
                            : () => _editNode(definition, entry.value),
                        child: Row(
                          children: [
                            Icon(
                              _nodeIcon('${entry.value['kind']}'),
                              size: 22,
                              color: colors.primary,
                            ),
                            const SizedBox(width: 10),
                            Expanded(
                              child: Column(
                                mainAxisAlignment: MainAxisAlignment.center,
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Text(
                                    entry.key,
                                    maxLines: 2,
                                    overflow: TextOverflow.ellipsis,
                                    style: const TextStyle(
                                      fontSize: 14,
                                      fontWeight: FontWeight.w600,
                                    ),
                                  ),
                                  Text(
                                    '${entry.value['kind']}',
                                    style: TextStyle(
                                      fontSize: 12,
                                      color: colors.onSurfaceVariant,
                                    ),
                                  ),
                                ],
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  IconData _nodeIcon(String kind) => switch (kind) {
    'input' => Icons.input,
    'output' => Icons.output,
    'filter' => Icons.filter_alt_outlined,
    'agent' => Icons.auto_awesome,
    'code' => Icons.code,
    'aggregate' => Icons.functions,
    'call' => Icons.call_split,
    _ => Icons.transform,
  };

  Map<String, dynamic> _nodeDefaults(String kind) => switch (kind) {
    'template' => {'text': 'Hello {{input.name}}'},
    'filter' => {'path': 'input.score', 'operator': 'gte', 'value': 70},
    'map' => {
      'path': 'input.items',
      'fields': {'name': '{{value.name}}'},
    },
    'aggregate' => {'path': 'input.items', 'operation': 'count'},
    'call' => {
      'neuron': 'timer:example',
      'interface': 'timer',
      'method': 'read',
      'arguments': <String, dynamic>{},
    },
    'agent' => {'instructions': 'Answer concisely.', 'prompt': '{{value}}'},
    'code' => {
      'source': 'Console.WriteLine(await Console.In.ReadToEndAsync());',
    },
    _ => <String, dynamic>{},
  };

  String _agentMode(Map<String, dynamic> configuration) =>
      switch (configuration['mode']) {
        'web' => configuration['operation'] == 'company' ? 'company' : 'web',
        'conversation' => 'conversation',
        'spawn' => 'spawn',
        'send' => 'send',
        'collect' => 'collect',
        'stop' => 'stop',
        _ => 'transform',
      };

  Map<String, dynamic> _agentDefaults(String mode) => switch (mode) {
    'company' => {
      'mode': 'web',
      'operation': 'company',
      'company': '{{input.companyName}}',
      'website': '{{input.website}}',
    },
    'web' => {
      'mode': 'web',
      'prompt': 'Research {{value}} and return verified findings with sources.',
    },
    'conversation' => {'mode': 'conversation', 'instructions': ''},
    'spawn' => {
      'mode': 'spawn',
      'instructions': 'You are a helpful assistant. Complete the requested task using your available tools.',
      'tools': <String>[],
      'lifetime': 'run',
    },
    'send' => {
      'mode': 'send',
      'agent': '{{nodes.creator}}',
      'prompt': '{{input.task}}',
      'wait': true,
    },
    'collect' => {'mode': 'collect', 'agent': '{{nodes.creator}}'},
    'stop' => {'mode': 'stop', 'agent': '{{nodes.creator}}'},
    _ => _nodeDefaults('agent'),
  };

  List<String> _agentFieldsFor(String mode) => switch (mode) {
    'company' => ['company', 'website'],
    'web' => ['prompt', 'startUrl'],
    'conversation' => ['instructions'],
    'spawn' => [
      'instructions',
      'prompt',
      'modelProfile',
      'provider',
      'model',
      'tools',
      'key',
    ],
    'send' => ['agent', 'prompt'],
    'collect' => ['agent', 'taskId'],
    'stop' => ['agent'],
    _ => ['instructions', 'prompt'],
  };

  void _setAgentField(
    Map<String, dynamic> configuration,
    String field,
    String value,
  ) {
    if (value.trim().isEmpty &&
        ([
              'website',
              'startUrl',
              'modelProfile',
              'provider',
              'model',
              'key',
              'agent',
              'taskId',
            ].contains(field) ||
            (field == 'prompt' &&
                configuration['mode'] != 'send' &&
                configuration['mode'] != 'web') ||
            field == 'instructions')) {
      configuration.remove(field);
    } else if (field == 'tools') {
      final tools = jsonDecode(value);
      if (tools is! List ||
          tools.any((tool) => tool is! String || tool.trim().isEmpty)) {
        throw const FormatException(
          'Tools must be a JSON list of nonempty tool names, such as ["browse_web"].',
        );
      }
      configuration[field] = tools;
    } else if (field == 'agent' &&
        (value.trimLeft().startsWith('{') ||
            value.trimLeft().startsWith('[')) &&
        !value.trimLeft().startsWith('{{')) {
      final reference = jsonDecode(value);
      if (reference is! Map && reference is! List) {
        throw const FormatException(
          'Use an agent reference or a list of agent references.',
        );
      }
      configuration[field] = reference;
    } else {
      configuration[field] = value;
    }
  }

  Future<void> _editMetadata(Map<String, dynamic> definition) async {
    final id = TextEditingController(text: '${definition['id']}');
    final name = TextEditingController(text: '${definition['name']}');
    final trigger = TextEditingController(
      text: '${definition['trigger'] ?? 'Manual'}',
    );
    final description = TextEditingController(
      text: '${definition['description'] ?? ''}',
    );
    final saved = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Program settings'),
        content: SizedBox(
          width: 480,
          child: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                TextField(
                  controller: name,
                  decoration: const InputDecoration(labelText: 'Program name'),
                ),
                TextField(
                  controller: id,
                  enabled: _snapshot == null,
                  maxLength: 48,
                  decoration: const InputDecoration(
                    labelText: 'Program ID',
                    helperText: 'Letters, digits, hyphens, and underscores; start with a letter or digit.',
                  ),
                ),
                TextField(
                  controller: trigger,
                  decoration: const InputDecoration(
                    labelText: 'Trigger signal',
                    helperText: 'Manual, ChatMessage, or a named signal. Use letters only.',
                  ),
                ),
                TextField(
                  controller: description,
                  minLines: 2,
                  maxLines: 4,
                  decoration: const InputDecoration(labelText: 'Description'),
                ),
              ],
            ),
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Apply settings'),
          ),
        ],
      ),
    );
    if (saved == true && mounted) {
      _write({
        ...definition,
        'id': id.text.trim(),
        'name': name.text.trim(),
        'trigger': trigger.text.trim(),
        'description': description.text.trim(),
      });
    }
    await Future<void>.delayed(const Duration(milliseconds: 300));
    for (final controller in [id, name, trigger, description]) {
      controller.dispose();
    }
  }

  Future<void> _editNode(
    Map<String, dynamic> definition,
    Map<String, dynamic>? node,
  ) async {
    final id = TextEditingController(
      text:
          '${node?['id'] ?? 'neuron${_maps(definition['nodes']).length + 1}'}',
    );
    var kind = '${node?['kind'] ?? 'template'}'.toLowerCase();
    if (!_nodeKinds.contains(kind)) kind = 'template';
    final config = TextEditingController(
      text: _encoder.convert(node?['config'] ?? {'text': '{{value}}'}),
    );
    final inputType = TextEditingController(
      text: '${node?['inputType'] ?? 'Json'}',
    );
    final outputType = TextEditingController(
      text: '${node?['outputType'] ?? 'Json'}',
    );
    final agentFields = {
      for (final field in [
        'instructions',
        'prompt',
        'startUrl',
        'company',
        'website',
        'modelProfile',
        'provider',
        'model',
        'tools',
        'key',
        'agent',
        'taskId',
      ])
        field: TextEditingController(),
    };
    var agentMode = 'transform';
    var waitForAgent = true;
    var agentLifetime = 'run';
    void syncAgentFields() {
      try {
        final configuration = _map(jsonDecode(config.text));
        agentMode = _agentMode(configuration);
        waitForAgent = configuration['wait'] != false;
        agentLifetime =
            configuration['lifetime'] == 'workspace' ||
                configuration['retain'] == true
            ? 'workspace'
            : 'run';
        for (final entry in agentFields.entries) {
          final raw = configuration[entry.key];
          final value = entry.key == 'tools'
              ? _encoder.convert(raw ?? <String>[])
              : raw is Map || raw is List
              ? _encoder.convert(raw)
              : '${raw ?? ''}';
          if (entry.value.text != value) entry.value.text = value;
        }
      } on FormatException {
        // Keep fields available while the user is editing incomplete JSON.
      } on TypeError {
        // The existing JSON validation will explain a non-object configuration.
      }
    }

    syncAgentFields();
    String? failure;
    final result = await showDialog<Map<String, dynamic>>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, update) => AlertDialog(
          title: Text(
            node == null ? 'Add neuron' : 'Edit neuron ${node['id']}',
          ),
          content: SizedBox(
            width: 600,
            child: SingleChildScrollView(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  TextField(
                    controller: id,
                    decoration: const InputDecoration(labelText: 'Neuron ID'),
                  ),
                  const SizedBox(height: 16),
                  DropdownButtonFormField<String>(
                    initialValue: kind,
                    decoration: const InputDecoration(labelText: 'Behavior'),
                    items: [
                      for (final value in _nodeKinds)
                        DropdownMenuItem(value: value, child: Text(value)),
                    ],
                    onChanged: (value) => update(() {
                      if (value == null || value == kind) return;
                      kind = value;
                      config.text = _encoder.convert(_nodeDefaults(kind));
                      syncAgentFields();
                    }),
                  ),
                  if (kind == 'agent') ...[
                    const SizedBox(height: 16),
                    DropdownButtonFormField<String>(
                      key: ValueKey('agent-mode-$agentMode'),
                      initialValue: agentMode,
                      decoration: const InputDecoration(
                        labelText: 'Agent mode',
                      ),
                      items: const [
                        DropdownMenuItem(
                          value: 'transform',
                          child: Text('AI transform'),
                        ),
                        DropdownMenuItem(
                          value: 'conversation',
                          child: Text('IntoChat conversation'),
                        ),
                        DropdownMenuItem(
                          value: 'web',
                          child: Text('Web research'),
                        ),
                        DropdownMenuItem(
                          value: 'company',
                          child: Text('Company lookup'),
                        ),
                        DropdownMenuItem(
                          value: 'spawn',
                          child: Text('Create agent'),
                        ),
                        DropdownMenuItem(
                          value: 'send',
                          child: Text('Message agent'),
                        ),
                        DropdownMenuItem(
                          value: 'collect',
                          child: Text('Wait for agents'),
                        ),
                        DropdownMenuItem(
                          value: 'stop',
                          child: Text('Stop agents'),
                        ),
                      ],
                      onChanged: (value) => update(() {
                        if (value == null || value == agentMode) return;
                        config.text = _encoder.convert(_agentDefaults(value));
                        syncAgentFields();
                        failure = null;
                      }),
                    ),
                    if (agentMode == 'web' || agentMode == 'company')
                      Padding(
                        padding: const EdgeInsets.only(top: 10),
                        child: Text(
                          agentMode == 'company'
                              ? 'Find a company’s website and public contact details. Returns company, website, address, email, status, source URLs, and notes.'
                              : 'Browse pages to answer your prompt and return findings with source URLs.',
                        ),
                      ),
                    if ([
                      'spawn',
                      'send',
                      'collect',
                      'stop',
                    ].contains(agentMode))
                      Padding(
                        padding: const EdgeInsets.only(top: 10),
                        child: Text(switch (agentMode) {
                          'spawn' => 'Create an agent with these instructions and tools. Pass its returned reference to another neuron. For one agent per item, set items in the JSON configuration and use {{value.name}} in prompts.',
                          'send' => 'Send a task to one or more agents. Wait for their response to use the completed results in the next neuron.',
                          'collect' => 'Wait for the selected agents to finish their tasks and return their results.',
                          _ => 'Stop the selected agents.',
                        }),
                      ),
                    if (agentMode == 'spawn')
                      Padding(
                        padding: const EdgeInsets.only(top: 12),
                        child: DropdownButtonFormField<String>(
                          key: ValueKey('agent-lifetime-$agentLifetime'),
                          initialValue: agentLifetime,
                          decoration: const InputDecoration(
                            labelText: 'Agent lifetime',
                          ),
                          items: const [
                            DropdownMenuItem(
                              value: 'run',
                              child: Text('This run'),
                            ),
                            DropdownMenuItem(
                              value: 'workspace',
                              child: Text('Workspace · keep for later'),
                            ),
                          ],
                          onChanged: (value) {
                            if (value == null) return;
                            try {
                              final configuration = _map(
                                jsonDecode(config.text),
                              );
                              configuration['lifetime'] = value;
                              configuration.remove('retain');
                              config.text = _encoder.convert(configuration);
                              update(() {
                                agentLifetime = value;
                                failure = null;
                              });
                            } catch (error) {
                              update(
                                () => failure =
                                    'Could not update agent settings: $error',
                              );
                            }
                          },
                        ),
                      ),
                    for (final field in _agentFieldsFor(agentMode))
                      Padding(
                        padding: const EdgeInsets.only(top: 12),
                        child: TextField(
                          controller: agentFields[field],
                          minLines:
                              [
                                'instructions',
                                'prompt',
                                'tools',
                              ].contains(field)
                              ? 2
                              : 1,
                          maxLines:
                              [
                                'instructions',
                                'prompt',
                                'tools',
                                'agent',
                              ].contains(field)
                              ? 5
                              : 1,
                          decoration: InputDecoration(
                            labelText: switch (field) {
                              'company' => 'Company name or input reference',
                              'website' => 'Company website (optional)',
                              'startUrl' => 'Starting URL (optional)',
                              'instructions' => 'Agent instructions',
                              'modelProfile' => 'Model profile (optional)',
                              'provider' => 'Provider (optional)',
                              'model' => 'Model (optional)',
                              'tools' => 'Tools (JSON list)',
                              'key' => 'Agent key (optional)',
                              'agent' => 'Agent reference',
                              'taskId' => 'Task ID (optional)',
                              _ =>
                                agentMode == 'web'
                                    ? 'Research prompt'
                                    : agentMode == 'spawn'
                                    ? 'Initial message (optional)'
                                    : agentMode == 'send'
                                    ? 'Message to agent'
                                    : 'Agent prompt',
                            },
                            helperText: field == 'company'
                                ? 'For example: OpenAI or {{input.companyName}}'
                                : field == 'website'
                                ? 'For example: https://openai.com or {{input.website}}'
                                : field == 'startUrl'
                                ? 'Start on this page, or leave blank to search.'
                                : field == 'tools'
                                ? 'JSON tool names or neuron addresses. Examples: browse_web, lookup_company, websearch. Use [] for no tools.'
                                : field == 'agent'
                                ? 'Use {{nodes.creator}}, an agent address, or JSON references. Leave blank to use the previous neuron’s result.'
                                : field == 'taskId'
                                ? 'Leave blank to wait for the initial task, or the latest task if none was supplied.'
                                : field == 'modelProfile'
                                ? 'Leave model settings blank to use the configured default.'
                                : field == 'key'
                                ? 'Stable within this run. Use a retained agent’s returned reference in later runs.'
                                : null,
                            border: const OutlineInputBorder(),
                          ),
                          onChanged: (value) {
                            try {
                              final configuration = _map(
                                jsonDecode(config.text),
                              );
                              _setAgentField(configuration, field, value);
                              config.text = _encoder.convert(configuration);
                              if (failure != null) update(() => failure = null);
                            } catch (error) {
                              update(
                                () => failure =
                                    'Could not update agent settings: $error',
                              );
                            }
                          },
                        ),
                      ),
                    if (agentMode == 'send')
                      CheckboxListTile(
                        contentPadding: EdgeInsets.zero,
                        title: const Text('Wait for agent response'),
                        subtitle: const Text(
                          'Turn off to return as soon as the task has been accepted.',
                        ),
                        value: waitForAgent,
                        onChanged: (value) {
                          try {
                            final configuration = _map(jsonDecode(config.text));
                            configuration['wait'] = value ?? true;
                            config.text = _encoder.convert(configuration);
                            update(() {
                              waitForAgent = value ?? true;
                              failure = null;
                            });
                          } catch (error) {
                            update(
                              () => failure =
                                  'Could not update agent settings: $error',
                            );
                          }
                        },
                      ),
                  ],
                  if (kind == 'code')
                    const Padding(
                      padding: EdgeInsets.only(top: 12),
                      child: Text(
                        'Trusted local C# · runs with the server account’s operating system access.',
                      ),
                    ),
                  Row(
                    children: [
                      Expanded(
                        child: TextField(
                          controller: inputType,
                          decoration: const InputDecoration(
                            labelText: 'Input type',
                          ),
                        ),
                      ),
                      const SizedBox(width: 12),
                      Expanded(
                        child: TextField(
                          controller: outputType,
                          decoration: const InputDecoration(
                            labelText: 'Output type',
                          ),
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 16),
                  TextField(
                    controller: config,
                    minLines: 8,
                    maxLines: 18,
                    style: const TextStyle(
                      fontFamily: 'monospace',
                      fontSize: 13,
                    ),
                    decoration: const InputDecoration(
                      labelText: 'Neuron configuration (JSON)',
                      alignLabelWithHint: true,
                      border: OutlineInputBorder(),
                    ),
                    onChanged: kind == 'agent'
                        ? (_) => update(syncAgentFields)
                        : null,
                  ),
                  const SizedBox(height: 8),
                  const Text(
                    'Use {{input.name}} for the original signal, {{value}} for the incoming value, and {{nodes.neuronId}} for a prior result.',
                  ),
                  if (failure != null)
                    Text(
                      failure!,
                      style: TextStyle(
                        color: Theme.of(context).colorScheme.error,
                      ),
                    ),
                ],
              ),
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
                  final newId = id.text.trim();
                  if (newId.isEmpty ||
                      _maps(definition['nodes']).any(
                        (value) =>
                            value['id'] == newId && value['id'] != node?['id'],
                      )) {
                    throw const FormatException('Choose a unique neuron ID.');
                  }
                  final configuration = _map(jsonDecode(config.text));
                  if (kind == 'agent') {
                    for (final field in _agentFieldsFor(agentMode)) {
                      _setAgentField(
                        configuration,
                        field,
                        agentFields[field]!.text,
                      );
                    }
                  }
                  Navigator.pop(context, {
                    'id': newId,
                    'kind': kind,
                    'inputType': inputType.text.trim(),
                    'outputType': outputType.text.trim(),
                    'config': configuration,
                  });
                } catch (error) {
                  update(() => failure = '$error');
                }
              },
              child: const Text('Apply neuron'),
            ),
          ],
        ),
      ),
    );
    if (result != null && mounted) {
      final nodes = _maps(definition['nodes']);
      if (node == null) {
        nodes.add(result);
      } else {
        nodes[nodes.indexWhere((value) => value['id'] == node['id'])] = result;
      }
      definition['nodes'] = nodes;
      if (node != null && node['id'] != result['id']) {
        definition['synapses'] = [
          for (final edge in _maps(definition['synapses']))
            {
              ...edge,
              if (edge['from'] == node['id']) 'from': result['id'],
              if (edge['to'] == node['id']) 'to': result['id'],
            },
        ];
      }
      _write(definition);
    }
    await Future<void>.delayed(const Duration(milliseconds: 300));
    for (final controller in [
      id,
      config,
      inputType,
      outputType,
      ...agentFields.values,
    ]) {
      controller.dispose();
    }
  }

  Future<void> _connect(Map<String, dynamic> definition) async {
    final ids = _maps(definition['nodes'])
        .map((node) => '${node['id']}')
        .toSet()
        .toList();
    if (ids.length < 2) return;
    var from = ids.first, to = ids.last;
    final saved = await showDialog<bool>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, update) => AlertDialog(
          title: const Text('Connect neurons'),
          content: SizedBox(
            width: 420,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                DropdownButtonFormField<String>(
                  initialValue: from,
                  decoration: const InputDecoration(labelText: 'Source neuron'),
                  items: [
                    for (final id in ids)
                      DropdownMenuItem(value: id, child: Text(id)),
                  ],
                  onChanged: (value) => update(() => from = value!),
                ),
                const SizedBox(height: 16),
                DropdownButtonFormField<String>(
                  initialValue: to,
                  decoration: const InputDecoration(labelText: 'Target neuron'),
                  items: [
                    for (final id in ids)
                      DropdownMenuItem(value: id, child: Text(id)),
                  ],
                  onChanged: (value) => update(() => to = value!),
                ),
              ],
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context),
              child: const Text('Cancel'),
            ),
            FilledButton(
              onPressed: from == to ? null : () => Navigator.pop(context, true),
              child: const Text('Connect'),
            ),
          ],
        ),
      ),
    );
    if (saved == true && mounted) {
      final edges = _maps(definition['synapses']);
      if (!edges.any((edge) => edge['from'] == from && edge['to'] == to)) {
        edges.add({'from': from, 'to': to});
      }
      definition['synapses'] = edges;
      _write(definition);
    }
  }
}

class _ProgramConnections extends CustomPainter {
  const _ProgramConnections(this.positions, this.edges, this.color);
  final Map<String, Rect> positions;
  final List<Map<String, dynamic>> edges;
  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final line = Paint()
      ..color = color.withValues(alpha: .65)
      ..strokeWidth = 2
      ..style = PaintingStyle.stroke;
    final arrow = Paint()..color = color.withValues(alpha: .8);
    for (final edge in edges) {
      final from = positions['${edge['from']}'];
      final to = positions['${edge['to']}'];
      if (from == null || to == null) continue;
      final start = from.centerRight;
      final end = to.centerLeft;
      final midpoint = (start.dx + end.dx) / 2;
      canvas.drawPath(
        Path()
          ..moveTo(start.dx, start.dy)
          ..cubicTo(midpoint, start.dy, midpoint, end.dy, end.dx - 3, end.dy),
        line,
      );
      canvas.drawPath(
        Path()
          ..moveTo(end.dx - 2, end.dy)
          ..lineTo(end.dx - 10, end.dy - 5)
          ..lineTo(end.dx - 10, end.dy + 5)
          ..close(),
        arrow,
      );
    }
  }

  @override
  bool shouldRepaint(covariant _ProgramConnections oldDelegate) =>
      oldDelegate.positions != positions ||
      oldDelegate.edges != edges ||
      oldDelegate.color != color;
}
