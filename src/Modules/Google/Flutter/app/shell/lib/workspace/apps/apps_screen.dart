import 'dart:async';

import 'package:flutter/material.dart';
import 'package:uuid/uuid.dart';

import '../csharp/csharp_manager.dart';
import 'app_spec_screen.dart';
import 'create_app_screen.dart';

typedef AppsRequest = Future<dynamic> Function(
  String method,
  String path, [
  Object? body,
]);

// Numeric values of DigitalBrain.Apps.AppStatus and InvocationStatus on the wire.
const _installed = 1;
const _pending = 0;
const _completed = 1;

// Shared C# apps: install one into this workspace in one step, run it, keep it current, or fork it.
class AppsScreen extends StatefulWidget {
  const AppsScreen({
    super.key,
    required this.workspaceId,
    required this.request,
    required this.onClose,
    this.onOpen,
    this.pollInterval = const Duration(milliseconds: 300),
  });

  final String workspaceId;
  final AppsRequest request;
  final VoidCallback onClose;
  final Future<void> Function(String id)? onOpen;
  final Duration pollInterval;

  @override
  State<AppsScreen> createState() => _AppsScreenState();
}

class _AppsScreenState extends State<AppsScreen> {
  List<Map<String, dynamic>> _listings = [];
  List<Map<String, dynamic>> _drafts = [];
  final Map<String, Map<String, dynamic>> _apps = {};
  final Map<String, List<Map<String, dynamic>>> _files = {};
  final Map<String, TextEditingController> _inputs = {};
  final Map<String, String> _outputs = {};
  final Map<String, List<Map<String, dynamic>>> _discussions = {};
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
      '/brains/${Uri.encodeComponent(widget.workspaceId)}/packages/${id.split('/').map(Uri.encodeComponent).join('/')}';

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
    // The person's own drafts; an unreadable list must not hide the marketplace.
    var drafts = <Map<String, dynamic>>[];
    try {
      final mine = await widget.request('GET', '/packages/drafts');
      if (mine is List) drafts = mine.map(_map).toList();
    } catch (_) {}
    final ids = loaded.map(_id).toList();
    // One unreadable install must not hide the rest of the marketplace.
    final reads = await Future.wait(
      ids.map(
        (id) => widget
            .request('GET', _appPath(id))
            .then<Object?>((view) => view, onError: (Object error) => error),
      ),
    );
    final failures =
        reads.whereType<Exception>().length + reads.whereType<Error>().length;
    if (!mounted) return;
    setState(() {
      _listings = loaded;
      _drafts = drafts;
      _apps
        ..clear()
        ..addEntries([
          for (var index = 0; index < ids.length; index++)
            MapEntry(ids[index], _map(_map(reads[index])['app'])),
        ]);
      _files
        ..clear()
        ..addEntries([
          for (var index = 0; index < ids.length; index++)
            MapEntry(ids[index], [
              for (final file
                  in (_map(reads[index])['files'] as List? ?? const []))
                if (file is Map) Map<String, dynamic>.from(file),
            ]),
        ]);
      if (failures > 0) {
        _error = 'Could not read $failures installed app(s).';
      }
    });
  }

  Future<void> _change(
    String method,
    String path,
    String notice, [
    Object? body,
  ]) => _perform(() async {
    try {
      await widget.request(method, path, body);
    } catch (_) {
      // A refused uninstall can still have persisted its pending state.
      try {
        await _load();
      } catch (_) {}
      rethrow;
    }
    await _load();
    if (mounted) setState(() => _notice = notice);
  });

  Future<void> _abandonStorage(String id) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Uninstall and leave storage behind?'),
        content: const Text(
          'Existing storage will remain and must be cleaned up separately.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Cancel'),
          ),
          FilledButton(
            key: const ValueKey('confirm-abandon-storage'),
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Leave storage behind'),
          ),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;
    await _change(
      'POST',
      '${_appPath(id)}/abandon-storage',
      'Uninstalled $id. Storage was left behind.',
      {'operationId': const Uuid().v4()},
    );
  }

  Future<void> _run(String id, String operation) => _perform(() async {
    final input = _inputs[id]?.text ?? '';
    var invocation = _map(
      await widget.request('POST', '${_appPath(id)}/invocations', {
        'operation': operation,
        'input': input,
      }),
    );
    // A group chat can take minutes; its turns show up while it runs.
    final deadline = DateTime.now().add(const Duration(minutes: 10));
    while (invocation['status'] == _pending &&
        DateTime.now().isBefore(deadline)) {
      await Future<void>.delayed(widget.pollInterval);
      await _readDiscussion(id, '${invocation['id']}');
      invocation = _map(
        await widget.request(
          'GET',
          '${_appPath(id)}/invocations/${invocation['id']}',
        ),
      );
    }
    await _readDiscussion(id, '${invocation['id']}');
    if (!mounted) return;
    setState(
      () => _outputs[id] = invocation['status'] == _completed
          ? '${invocation['output'] ?? ''}'
          : invocation['status'] == _pending
          ? 'Still running; the app has not answered yet.'
          : 'Failed: ${invocation['error'] ?? 'no reason given'}',
    );
  });

  Future<void> _readDiscussion(String id, String invocationId) async {
    try {
      final discussion = _map(
        await widget.request(
          'GET',
          '${_appPath(id)}/invocations/$invocationId/discussion',
        ),
      );
      final turns = discussion['turns'] is List
          ? (discussion['turns'] as List).map(_map).toList()
          : <Map<String, dynamic>>[];
      if (mounted && turns.isNotEmpty) {
        setState(() => _discussions[id] = turns);
      }
    } catch (_) {
      // Only group chat apps hold a discussion.
    }
  }

  void _openSpec(String id, String title) => Navigator.of(context).push(
    MaterialPageRoute<void>(
      builder: (_) =>
          AppSpecScreen(packageId: id, title: title, request: widget.request),
    ),
  );

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Apps'),
        leading: IconButton(
          tooltip: 'Close',
          icon: const Icon(Icons.close),
          onPressed: widget.onClose,
        ),
        actions: [
          TextButton.icon(
            key: const ValueKey('create-app'),
            onPressed: _busy
                ? null
                : () async {
                    await Navigator.of(context).push(
                      MaterialPageRoute<void>(
                        builder: (_) =>
                            CreateAppScreen(request: widget.request),
                      ),
                    );
                    if (mounted) await _refresh();
                  },
            icon: const Icon(Icons.add),
            label: const Text('Create app'),
          ),
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
            child: _listings.isEmpty && _drafts.isEmpty && !_busy
                ? const Center(child: Text('No one has published an app yet.'))
                : ListView(
                    padding: const EdgeInsets.all(12),
                    children: [
                      if (_drafts.isNotEmpty) ...[
                        Padding(
                          padding: const EdgeInsets.only(bottom: 4),
                          child: Text(
                            'My drafts',
                            style: Theme.of(context).textTheme.titleMedium,
                          ),
                        ),
                        for (final draft in _drafts) _draftTile(draft),
                        const SizedBox(height: 12),
                      ],
                      for (final listing in _listings) _card(listing),
                    ],
                  ),
          ),
        ],
      ),
    );
  }

  // Numeric values of DigitalBrain.Apps.AppDraftStatus on the wire.
  static const _draftStatusLabels = [
    'New',
    'Scenarios written',
    'Building',
    'Published',
    'Build failed',
  ];

  Widget _draftTile(Map<String, dynamic> draft) {
    final id = '${draft['id']}';
    final title = '${draft['title'] ?? ''}';
    final status = draft['status'] as int? ?? 0;
    return Card(
      key: ValueKey('draft-$id'),
      child: ListTile(
        title: Text(title.isEmpty ? 'Untitled draft' : title),
        subtitle: Text(
          status < _draftStatusLabels.length ? _draftStatusLabels[status] : '',
        ),
        trailing: const Icon(Icons.chevron_right),
        onTap: _busy
            ? null
            : () async {
                await Navigator.of(context).push(
                  MaterialPageRoute<void>(
                    builder: (_) =>
                        CreateAppScreen(request: widget.request, draftId: id),
                  ),
                );
                if (mounted) await _refresh();
              },
      ),
    );
  }

  Widget _card(Map<String, dynamic> listing) {
    final id = _id(listing);
    final app = _apps[id] ?? const <String, dynamic>{};
    final installed = app['status'] == _installed;
    final uninstallPending = app['uninstallPending'] == true;
    final running = '${_map(app['revision'])['revision'] ?? ''}';
    final published = '${listing['revision'] ?? ''}';
    final origin = _map(_map(listing['forkedFrom'])['package']);
    final operations = app['operations'] is List
        ? (app['operations'] as List).map(_map).toList()
        : <Map<String, dynamic>>[];
    final files = _files[id] ?? const <Map<String, dynamic>>[];
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
            if (uninstallPending) const Text('Uninstall pending'),
            if ((app['abandonedStorage'] as List? ?? const []).isNotEmpty)
              const Text('Storage from a previous uninstall was left behind.'),
            if ('${listing['description'] ?? ''}'.isNotEmpty)
              Text('${listing['description']}'),
            if (installed && files.isNotEmpty)
              Text(
                'C# app: ${files.map(csharpStatusLabel).join(', ')}',
                key: ValueKey('file-status-$id'),
              ),
            const SizedBox(height: 8),
            Wrap(
              spacing: 8,
              children: [
                if (installed &&
                    !uninstallPending &&
                    widget.onOpen != null &&
                    operations.any((operation) => operation['name'] == 'open'))
                  FilledButton(
                    key: ValueKey('open-$id'),
                    onPressed: _busy ? null : () => widget.onOpen!(id),
                    child: const Text('Open'),
                  ),
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
                if (installed && !uninstallPending && running != published)
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
                if (uninstallPending)
                  OutlinedButton(
                    key: ValueKey('abandon-storage-$id'),
                    onPressed: _busy ? null : () => _abandonStorage(id),
                    child: const Text('Leave storage behind'),
                  ),
                OutlinedButton(
                  key: ValueKey('scenarios-$id'),
                  onPressed: () => _openSpec(id, '${listing['title'] ?? id}'),
                  child: const Text('Scenarios'),
                ),
                OutlinedButton(
                  key: ValueKey('fork-$id'),
                  onPressed: _busy
                      ? null
                      : () => _change(
                          'POST',
                          '/packages/${id.split('/').map(Uri.encodeComponent).join('/')}/fork',
                          'Forked $id into your apps.',
                          <String, dynamic>{},
                        ),
                  child: const Text('Fork'),
                ),
              ],
            ),
            if (installed && !uninstallPending && operations.isNotEmpty) ...[
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
            for (final turn
                in _discussions[id] ?? const <Map<String, dynamic>>[])
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Text.rich(
                  TextSpan(
                    children: [
                      TextSpan(
                        text: '${turn['speaker']} · round ${turn['round']}: ',
                        style: const TextStyle(fontWeight: FontWeight.bold),
                      ),
                      TextSpan(text: '${turn['text']}'),
                    ],
                  ),
                  key: ValueKey('turn-$id-${turn['round']}-${turn['speaker']}'),
                ),
              ),
            if (_outputs[id] != null)
              SelectableText(_outputs[id]!, key: ValueKey('output-$id')),
          ],
        ),
      ),
    );
  }
}
