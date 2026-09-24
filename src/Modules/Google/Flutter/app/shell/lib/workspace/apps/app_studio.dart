import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:uuid/uuid.dart';

typedef AppStudioRequest = Future<dynamic> Function(
  String method,
  String path, [
  Object? body,
]);

class AppStudio extends StatefulWidget {
  const AppStudio({
    super.key,
    required this.workspaceId,
    required this.request,
    required this.onClose,
  });
  final String workspaceId;
  final AppStudioRequest request;
  final VoidCallback onClose;
  @override
  State<AppStudio> createState() => _AppStudioState();
}

class _AppStudioState extends State<AppStudio> {
  final _slug = TextEditingController();
  final _name = TextEditingController();
  final _description = TextEditingController();
  final _createPrefix = TextEditingController(text: 'Hello ');
  final _prefix = TextEditingController(text: 'Hello ');
  final _input = TextEditingController(text: 'World');
  final _composition = TextEditingController(
    text: const JsonEncoder.withIndent('  ').convert({
      'requiredModules': <String>[],
      'activation': 0,
      'defaults': {'prefix': 'Hello '},
      'parts': [
        {
          'name': 'greet',
          'behavior': 'text.prefix',
          'settings': {'configurationKey': 'prefix'},
        },
        {
          'name': 'uppercase',
          'behavior': 'text.uppercase',
          'settings': <String, String>{},
        },
      ],
      'bindings': [
        {'source': r'$app', 'signal': 'run', 'target': 'greet'},
        {'source': 'greet', 'signal': 'completed', 'target': 'uppercase'},
      ],
    }),
  );
  List<Map<String, dynamic>> _installed = [], _marketplace = [];
  Map<String, dynamic>? _selected;
  int _tab = 0;
  bool _busy = false;
  String? _error, _notice;
  String get _root =>
      '/workspaces/${Uri.encodeComponent(widget.workspaceId)}/app-runtime';
  static Map<String, dynamic> _map(dynamic value) =>
      value is Map ? Map<String, dynamic>.from(value) : {};
  static List<Map<String, dynamic>> _list(dynamic value) =>
      value is List ? value.map(_map).toList() : [];
  String _id(Map<String, dynamic> snapshot) =>
      '${_map(snapshot['manifest'])['id'] ?? ''}';
  String _path(String id) {
    final segments = id.split('/');
    if (segments.length != 2 || segments.any((part) => part.isEmpty)) {
      throw const FormatException('Only creator apps can be managed here.');
    }
    return '$_root/${segments.map(Uri.encodeComponent).join('/')}';
  }

  @override
  void initState() {
    super.initState();
    _refresh();
  }

  @override
  void dispose() {
    for (final controller in [
      _slug,
      _name,
      _description,
      _createPrefix,
      _prefix,
      _input,
      _composition,
    ]) {
      controller.dispose();
    }
    super.dispose();
  }

  Future<void> _perform(Future<void> Function() action) async {
    if (_busy) return;
    setState(() {
      _busy = true;
      _error = null;
      _notice = null;
    });
    try {
      await action();
    } catch (error) {
      if (mounted) setState(() => _error = error.toString());
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _refresh() => _perform(() async {
    final result = await widget.request(
      'GET',
      _tab == 2 ? '/marketplace/apps' : _root,
    );
    if (!mounted) return;
    setState(() {
      if (_tab == 2) {
        _marketplace = _list(result);
      } else {
        _installed = _list(result)
            .where((item) => _id(item).contains('/'))
            .toList();
        if (_selected != null) {
          final matching = _installed.where(
            (item) => _id(item) == _id(_selected!),
          );
          _selected = matching.firstOrNull;
        }
      }
    });
  });

  void _select(Map<String, dynamic> snapshot) {
    _selected = snapshot;
    final manifest = _map(snapshot['manifest']);
    final defaults = _map(_map(manifest['composition'])['defaults']);
    _prefix.text =
        '${_map(snapshot['configuration'])['prefix'] ?? defaults['prefix'] ?? ''}';
  }

  void _accept(dynamic result) {
    final snapshot = _map(result);
    if (snapshot['manifest'] == null) {
      throw const FormatException('The server did not return an app.');
    }
    if (!mounted) return;
    setState(() {
      _installed.removeWhere((item) => _id(item) == _id(snapshot));
      _installed.add(snapshot);
      _select(snapshot);
      _tab = 0;
    });
  }

  Future<void> _create() => _perform(() async {
    if (_slug.text.trim().isEmpty || _name.text.trim().isEmpty) {
      throw const FormatException('Enter an app ID and a name.');
    }
    final composition = jsonDecode(_composition.text);
    if (composition is! Map<String, dynamic>) {
      throw const FormatException('Composition must be a JSON object.');
    }
    final description = _description.text.trim();
    composition['defaults'] = {
      ..._map(composition['defaults']),
      'prefix': _createPrefix.text,
    };
    _accept(
      await widget.request('POST', _root, {
        'name': _slug.text.trim(),
        'manifest': {
          'id': 'placeholder.app',
          'publisher': 'placeholder',
          'version': '1.0.0',
          'kind': 0,
          'name': _name.text.trim(),
          'descriptionForPeople': description,
          'descriptionForModel': description,
          'composition': composition,
          'operations': [
            {
              'name': 'run',
              'descriptionForModel': 'Run the app',
              'inputTypeIds': {'value': 'plain-text'},
              'outputTypeId': 'plain-text',
            },
          ],
          'scenarios': [
            {'name': 'Run', 'given': 'input', 'when': 'run', 'then': 'output'},
          ],
        },
      }),
    );
  });

  Future<void> _configure() => _perform(() async {
    _accept(
      await widget.request('POST', '${_path(_id(_selected!))}/configure', {
        'configuration': {
          ..._map(_selected!['configuration']),
          'prefix': _prefix.text,
        },
      }),
    );
    if (mounted) setState(() => _notice = 'Configuration saved');
  });

  Future<void> _run() => _perform(() async {
    _accept(
      await widget.request('POST', '${_path(_id(_selected!))}/run', {
        'operationId': const Uuid().v4(),
        'signal': 'run',
        'value': _input.text,
      }),
    );
  });

  Future<void> _publish() => _perform(() async {
    final id = _id(_selected!);
    final result = _map(await widget.request('POST', '${_path(id)}/publish'));
    if (mounted) {
      setState(
        () => _notice =
            'Published ${result['appId'] ?? id} · ${result['version'] ?? _map(_selected!['manifest'])['version']}',
      );
    }
  });

  Future<void> _install(Map<String, dynamic> listing) => _perform(() async {
    final segments = '${listing['appId']}'.split('/');
    if (segments.length != 2) {
      throw const FormatException('Invalid creator app ID.');
    }
    _accept(
      await widget.request('POST', '$_root/install', {
        'publisher': segments[0],
        'name': segments[1],
        'version': listing['version'],
        'configuration': <String, dynamic>{},
      }),
    );
  });

  Widget _field(
    String key,
    String label,
    TextEditingController controller, {
    int lines = 1,
  }) => Padding(
    padding: const EdgeInsets.only(bottom: 16),
    child: TextField(
      key: Key(key),
      controller: controller,
      enabled: !_busy,
      minLines: lines,
      maxLines: lines,
      decoration: InputDecoration(
        labelText: label,
        border: const OutlineInputBorder(),
      ),
    ),
  );

  Widget _createForm() => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Text('Create an app', style: Theme.of(context).textTheme.headlineSmall),
      const SizedBox(height: 8),
      const Text(
        'Start with a greeting app. Its connected steps add a prefix and turn the result into uppercase.',
      ),
      const SizedBox(height: 24),
      _field('app-slug', 'App ID', _slug),
      _field('app-name', 'App name', _name),
      _field('app-description', 'Description', _description),
      _field('create-app-prefix', 'Default prefix', _createPrefix),
      ExpansionTile(
        title: const Text('Advanced composition'),
        children: [
          _field(
            'app-composition',
            'Composition JSON',
            _composition,
            lines: 12,
          ),
        ],
      ),
      const SizedBox(height: 16),
      FilledButton(
        key: const Key('create-app'),
        onPressed: _busy ? null : _create,
        child: const Text('Create app'),
      ),
    ],
  );

  Widget _detail() {
    final snapshot = _selected!;
    final manifest = _map(snapshot['manifest']);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Divider(height: 32),
        Text(
          '${manifest['name'] ?? _id(snapshot)}',
          style: Theme.of(context).textTheme.headlineSmall,
        ),
        Text('${_id(snapshot)} · ${manifest['version'] ?? ''}'),
        const SizedBox(height: 8),
        Text('${manifest['descriptionForPeople'] ?? ''}'),
        const SizedBox(height: 24),
        _field('app-prefix', 'Prefix', _prefix),
        OutlinedButton(
          key: const Key('save-app-configuration'),
          onPressed: _busy ? null : _configure,
          child: const Text('Save configuration'),
        ),
        const SizedBox(height: 24),
        _field('app-input', 'Input', _input),
        Wrap(
          spacing: 12,
          runSpacing: 12,
          children: [
            FilledButton(
              key: const Key('run-app'),
              onPressed: _busy ? null : _run,
              child: const Text('Run app'),
            ),
            OutlinedButton(
              key: const Key('publish-app'),
              onPressed: _busy ? null : _publish,
              child: const Text('Publish to marketplace'),
            ),
          ],
        ),
        const SizedBox(height: 24),
        Text('Output', style: Theme.of(context).textTheme.titleMedium),
        const SizedBox(height: 8),
        SelectionArea(
          key: const Key('app-output'),
          child: Text('${snapshot['output'] ?? ''}'),
        ),
      ],
    );
  }

  Widget _installedView() => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      if (_installed.isEmpty) const Text('No apps installed yet.'),
      for (final snapshot in _installed)
        ListTile(
          key: Key('installed-${_id(snapshot)}'),
          contentPadding: EdgeInsets.zero,
          leading: const Icon(Icons.apps_outlined),
          title: Text('${_map(snapshot['manifest'])['name'] ?? _id(snapshot)}'),
          subtitle: Text(_id(snapshot)),
          selected: _selected != null && _id(_selected!) == _id(snapshot),
          onTap: _busy ? null : () => setState(() => _select(snapshot)),
        ),
      if (_selected != null) _detail(),
    ],
  );

  Widget _marketplaceView() => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      if (_marketplace.isEmpty) const Text('No published apps yet.'),
      for (final listing in _marketplace)
        Card(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  '${_map(listing['manifest'])['name'] ?? listing['appId']}',
                  style: Theme.of(context).textTheme.titleMedium,
                ),
                Text('${listing['appId']} · ${listing['version']}'),
                const SizedBox(height: 8),
                Text(
                  '${_map(listing['manifest'])['descriptionForPeople'] ?? ''}',
                ),
                const SizedBox(height: 12),
                FilledButton(
                  key: Key('install-${listing['appId']}-${listing['version']}'),
                  onPressed: _busy ? null : () => _install(listing),
                  child: const Text('Install'),
                ),
              ],
            ),
          ),
        ),
    ],
  );

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Apps'),
      leading: IconButton(
        tooltip: 'Close apps',
        onPressed: widget.onClose,
        icon: const Icon(Icons.arrow_back),
      ),
      actions: [
        IconButton(
          tooltip: 'Refresh apps',
          onPressed: _busy ? null : _refresh,
          icon: const Icon(Icons.refresh),
        ),
      ],
    ),
    body: Column(
      children: [
        Padding(
          padding: const EdgeInsets.all(16),
          child: SegmentedButton<int>(
            segments: const [
              ButtonSegment(value: 0, label: Text('Installed')),
              ButtonSegment(value: 1, label: Text('Create')),
              ButtonSegment(value: 2, label: Text('Marketplace')),
            ],
            selected: {_tab},
            onSelectionChanged: _busy
                ? null
                : (selection) {
                    setState(() {
                      _tab = selection.first;
                      _error = null;
                      _notice = null;
                    });
                    if (_tab != 1) _refresh();
                  },
          ),
        ),
        if (_busy) const LinearProgressIndicator(),
        if (_error != null)
          Padding(
            padding: const EdgeInsets.all(12),
            child: Text(
              _error!,
              key: const Key('app-error'),
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
          ),
        if (_notice != null)
          Padding(
            padding: const EdgeInsets.all(12),
            child: Text(_notice!, key: const Key('app-notice')),
          ),
        Expanded(
          child: SingleChildScrollView(
            child: Center(
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 760),
                child: Padding(
                  padding: const EdgeInsets.all(24),
                  child: switch (_tab) {
                    1 => _createForm(),
                    2 => _marketplaceView(),
                    _ => _installedView(),
                  },
                ),
              ),
            ),
          ),
        ),
      ],
    ),
  );
}
