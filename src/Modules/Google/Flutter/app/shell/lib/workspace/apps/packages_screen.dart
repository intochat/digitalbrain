import 'dart:async';

import 'package:flutter/material.dart';

typedef PackagesRequest = Future<dynamic> Function(
  String method,
  String path, [
  Object? body,
]);

// Numeric values of DigitalBrain.Apps.AppStatus and InvocationStatus on the wire.
const _installed = 1;
const _pending = 0;
const _completed = 1;

// Shared behaviors: install one into this workspace in one step, run it, keep it current, or fork it.
class PackagesScreen extends StatefulWidget {
  const PackagesScreen({
    super.key,
    required this.workspaceId,
    required this.request,
    required this.onClose,
    this.pollInterval = const Duration(milliseconds: 300),
  });

  final String workspaceId;
  final PackagesRequest request;
  final VoidCallback onClose;
  final Duration pollInterval;

  @override
  State<PackagesScreen> createState() => _PackagesScreenState();
}

class _PackagesScreenState extends State<PackagesScreen> {
  List<Map<String, dynamic>> _listings = [];
  final Map<String, Map<String, dynamic>> _apps = {};
  final Map<String, TextEditingController> _inputs = {};
  final Map<String, String> _outputs = {};
  bool _busy = false;
  String? _error;
  String? _notice;

  static Map<String, dynamic> _map(dynamic value) =>
      value is Map ? Map<String, dynamic>.from(value) : <String, dynamic>{};

  static String _id(Map<String, dynamic> listing) {
    final package = _map(listing['package']);
    return '${package['owner']}/${package['name']}';
  }

  String _appPath(String id) =>
      '/workspaces/${Uri.encodeComponent(widget.workspaceId)}/packages/${id.split('/').map(Uri.encodeComponent).join('/')}';

  @override
  void initState() {
    super.initState();
    unawaited(_refresh());
  }

  @override
  void dispose() {
    for (final controller in _inputs.values) {
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

  Future<void> _refresh() => _perform(_load);

  Future<void> _load() async {
    final listings = await widget.request('GET', '/packages');
    final loaded = listings is List
        ? listings.map(_map).toList()
        : <Map<String, dynamic>>[];
    final apps = <String, Map<String, dynamic>>{};
    for (final listing in loaded) {
      final id = _id(listing);
      apps[id] = _map(_map(await widget.request('GET', _appPath(id)))['app']);
    }
    if (!mounted) return;
    setState(() {
      _listings = loaded;
      _apps
        ..clear()
        ..addAll(apps);
    });
  }

  Future<void> _change(
    String method,
    String path,
    String notice, [
    Object? body,
  ]) => _perform(() async {
    await widget.request(method, path, body);
    await _load();
    if (mounted) setState(() => _notice = notice);
  });

  Future<void> _run(String id, String operation) => _perform(() async {
    final input = _inputs[id]?.text ?? '';
    var invocation = _map(
      await widget.request('POST', '${_appPath(id)}/invocations', {
        'operation': operation,
        'input': input,
      }),
    );
    final deadline = DateTime.now().add(const Duration(seconds: 30));
    while (invocation['status'] == _pending &&
        DateTime.now().isBefore(deadline)) {
      await Future<void>.delayed(widget.pollInterval);
      invocation = _map(
        await widget.request(
          'GET',
          '${_appPath(id)}/invocations/${invocation['id']}',
        ),
      );
    }
    if (!mounted) return;
    setState(
      () => _outputs[id] = invocation['status'] == _completed
          ? '${invocation['output'] ?? ''}'
          : invocation['status'] == _pending
          ? 'Still running; the behavior has not answered yet.'
          : 'Failed: ${invocation['error'] ?? 'no reason given'}',
    );
  });

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Packages'),
        leading: IconButton(
          tooltip: 'Close',
          icon: const Icon(Icons.close),
          onPressed: widget.onClose,
        ),
        actions: [
          IconButton(
            tooltip: 'Refresh',
            icon: const Icon(Icons.refresh),
            onPressed: _busy ? null : _refresh,
          ),
        ],
      ),
      body: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (_busy) const LinearProgressIndicator(),
          if (_error != null)
            Padding(
              padding: const EdgeInsets.all(12),
              child: Text(
                _error!,
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
            ),
          if (_notice != null)
            Padding(padding: const EdgeInsets.all(12), child: Text(_notice!)),
          Expanded(
            child: _listings.isEmpty && !_busy
                ? const Center(
                    child: Text('No one has published a behavior yet.'),
                  )
                : ListView(
                    padding: const EdgeInsets.all(12),
                    children: [for (final listing in _listings) _card(listing)],
                  ),
          ),
        ],
      ),
    );
  }

  Widget _card(Map<String, dynamic> listing) {
    final id = _id(listing);
    final app = _apps[id] ?? const <String, dynamic>{};
    final installed = app['status'] == _installed;
    final running = '${_map(app['revision'])['revision'] ?? ''}';
    final published = '${listing['revision'] ?? ''}';
    final origin = _map(_map(listing['forkedFrom'])['package']);
    final operations = app['operations'] is List
        ? (app['operations'] as List).map(_map).toList()
        : <Map<String, dynamic>>[];
    final input = _inputs.putIfAbsent(id, TextEditingController.new);
    return Card(
      key: ValueKey('package-$id'),
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              '${listing['title'] ?? id}',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            Text(
              '$id · ${published.length > 12 ? published.substring(0, 12) : published}',
            ),
            if (origin.isNotEmpty)
              Text('Forked from ${origin['owner']}/${origin['name']}'),
            if ('${listing['description'] ?? ''}'.isNotEmpty)
              Text('${listing['description']}'),
            const SizedBox(height: 8),
            Wrap(
              spacing: 8,
              children: [
                if (!installed)
                  FilledButton(
                    key: ValueKey('install-$id'),
                    onPressed: _busy
                        ? null
                        : () => _change(
                            'POST',
                            _appPath(id),
                            'Installed $id.',
                            <String, dynamic>{},
                          ),
                    child: const Text('Install'),
                  ),
                if (installed && running != published)
                  FilledButton.tonal(
                    key: ValueKey('upgrade-$id'),
                    onPressed: _busy
                        ? null
                        : () => _change(
                            'POST',
                            '${_appPath(id)}/upgrade',
                            'Upgraded $id.',
                            <String, dynamic>{},
                          ),
                    child: const Text('Update'),
                  ),
                if (installed)
                  OutlinedButton(
                    key: ValueKey('uninstall-$id'),
                    onPressed: _busy
                        ? null
                        : () => _change(
                            'DELETE',
                            _appPath(id),
                            'Uninstalled $id.',
                          ),
                    child: const Text('Uninstall'),
                  ),
                OutlinedButton(
                  key: ValueKey('fork-$id'),
                  onPressed: _busy
                      ? null
                      : () => _change(
                          'POST',
                          '/packages/${id.split('/').map(Uri.encodeComponent).join('/')}/fork',
                          'Forked $id into your packages.',
                          <String, dynamic>{},
                        ),
                  child: const Text('Fork'),
                ),
              ],
            ),
            if (installed && operations.isNotEmpty) ...[
              const SizedBox(height: 8),
              TextField(
                key: ValueKey('input-$id'),
                controller: input,
                decoration: const InputDecoration(labelText: 'Input'),
              ),
              Wrap(
                spacing: 8,
                children: [
                  for (final operation in operations)
                    TextButton(
                      key: ValueKey('run-$id-${operation['name']}'),
                      onPressed: _busy
                          ? null
                          : () => _run(id, '${operation['name']}'),
                      child: Text('Run ${operation['name']}'),
                    ),
                ],
              ),
            ],
            if (_outputs[id] != null)
              SelectableText(_outputs[id]!, key: ValueKey('output-$id')),
          ],
        ),
      ),
    );
  }
}
