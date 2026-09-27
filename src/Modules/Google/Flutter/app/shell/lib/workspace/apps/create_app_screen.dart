import 'dart:math';

import 'package:flutter/material.dart';

import 'app_spec_view.dart';
import 'packages_screen.dart';

// Numeric values of IntoChat.Marketplace.AppDraftStatus on the wire.
const _drafted = 1;
const _building = 2;
const _published = 3;
const _failed = 4;

// Create an app by describing it: the Author writes its scenarios, you read and adjust them, and the
// Builder makes them pass. The app reaches the marketplace only when every scenario passes.
class CreateAppScreen extends StatefulWidget {
  const CreateAppScreen({super.key, required this.request, this.draftId});

  final PackagesRequest request;
  final String? draftId;

  @override
  State<CreateAppScreen> createState() => _CreateAppScreenState();
}

class _CreateAppScreenState extends State<CreateAppScreen> {
  late final String _draftId = widget.draftId ?? _newDraftId();
  final _describe = TextEditingController();
  final _revise = TextEditingController();
  final _spec = TextEditingController();
  Map<String, dynamic>? _view;
  bool _editing = false;
  bool _busy = false;
  String? _activity;
  String? _error;

  static String _newDraftId() {
    final random = Random.secure();
    return List.generate(
      32,
      (_) => random.nextInt(16).toRadixString(16),
    ).join();
  }

  static Map<String, dynamic> _map(dynamic value) =>
      value is Map ? Map<String, dynamic>.from(value) : <String, dynamic>{};

  String get _path => '/packages/drafts/$_draftId';

  @override
  void dispose() {
    _describe.dispose();
    _revise.dispose();
    _spec.dispose();
    super.dispose();
  }

  Future<void> _perform(String activity, Future<dynamic> Function() call) async {
    if (_busy) return;
    setState(() {
      _busy = true;
      _activity = activity;
      _error = null;
    });
    try {
      final view = _map(await call());
      if (!mounted) return;
      setState(() {
        _view = view;
        _spec.text = '${_map(view['draft'])['spec'] ?? ''}';
        _editing = false;
      });
    } catch (error) {
      if (mounted) setState(() => _error = error.toString());
    } finally {
      if (mounted) {
        setState(() {
          _busy = false;
          _activity = null;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final draft = _map(_view?['draft']);
    final feature = _map(_view?['feature']);
    final status = draft['status'] as int? ?? 0;
    final attempts = draft['attempts'] is List
        ? (draft['attempts'] as List).map(_map).toList()
        : <Map<String, dynamic>>[];
    final published = _map(draft['published']);
    return Scaffold(
      appBar: AppBar(title: const Text('Create an app')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          if (_busy) ...[
            const LinearProgressIndicator(),
            if (_activity != null)
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 8),
                child: Text(_activity!),
              ),
          ],
          if (_error != null)
            Text(
              _error!,
              key: const ValueKey('create-error'),
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
          TextField(
            key: const ValueKey('describe-app'),
            controller: _describe,
            minLines: 2,
            maxLines: 6,
            decoration: const InputDecoration(
              labelText: 'What should the app do?',
              border: OutlineInputBorder(),
            ),
          ),
          const SizedBox(height: 8),
          Align(
            alignment: Alignment.centerLeft,
            child: FilledButton(
              key: const ValueKey('draft-app'),
              onPressed: _busy
                  ? null
                  : () => _perform(
                      'The Author is writing the scenarios…',
                      () => widget.request('POST', _path, {
                        'text': _describe.text,
                      }),
                    ),
              child: Text(draft.isEmpty ? 'Write scenarios' : 'Start over'),
            ),
          ),
          if (feature.isNotEmpty) ...[
            const Divider(height: 32),
            Text(
              '${draft['title']} · ${draft['runtime']} app',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            if ('${draft['description'] ?? ''}'.isNotEmpty)
              Text('${draft['description']}'),
            const SizedBox(height: 8),
            if (_editing)
              TextField(
                key: const ValueKey('edit-spec'),
                controller: _spec,
                minLines: 8,
                maxLines: 40,
                style: const TextStyle(fontFamily: 'monospace'),
                decoration: const InputDecoration(border: OutlineInputBorder()),
              )
            else
              AppSpecView(feature: feature),
            Wrap(
              spacing: 8,
              children: [
                if (_editing)
                  FilledButton.tonal(
                    key: const ValueKey('save-spec'),
                    onPressed: _busy
                        ? null
                        : () => _perform(
                            'Checking the scenarios…',
                            () => widget.request('PUT', '$_path/spec', {
                              'text': _spec.text,
                            }),
                          ),
                    child: const Text('Save scenarios'),
                  ),
                TextButton(
                  key: const ValueKey('toggle-edit'),
                  onPressed: _busy
                      ? null
                      : () => setState(() => _editing = !_editing),
                  child: Text(_editing ? 'Cancel' : 'Edit scenarios'),
                ),
              ],
            ),
            if (status == _drafted || status == _failed) ...[
              TextField(
                key: const ValueKey('revise-app'),
                controller: _revise,
                decoration: const InputDecoration(
                  labelText: 'Ask the Author for a change',
                ),
              ),
              Wrap(
                spacing: 8,
                children: [
                  OutlinedButton(
                    key: const ValueKey('revise-button'),
                    onPressed: _busy
                        ? null
                        : () => _perform(
                            'The Author is revising the scenarios…',
                            () => widget.request('POST', '$_path/revise', {
                              'text': _revise.text,
                            }),
                          ),
                    child: const Text('Revise'),
                  ),
                  FilledButton(
                    key: const ValueKey('build-app'),
                    onPressed: _busy || feature['fullyBound'] != true
                        ? null
                        : () => _perform(
                            'The Builder is making every scenario pass. This can take a few minutes…',
                            () => widget.request('POST', '$_path/build'),
                          ),
                    child: const Text('Build app'),
                  ),
                ],
              ),
              if (feature['fullyBound'] != true)
                const Text(
                  'Grey steps are not ones the brain understands. Revise or edit them before building.',
                ),
            ],
            if (status == _building) const Text('Building…'),
            for (final (index, attempt) in attempts.indexed)
              ListTile(
                key: ValueKey('attempt-$index'),
                leading: Icon(
                  attempt['green'] == true ? Icons.check_circle : Icons.cancel,
                  color: attempt['green'] == true
                      ? Colors.green
                      : Theme.of(context).colorScheme.error,
                ),
                title: Text('Attempt ${index + 1}'),
                subtitle: '${attempt['failures'] ?? ''}'.isEmpty
                    ? null
                    : Text('${attempt['failures']}'),
              ),
            if (status == _published)
              Text(
                'Published as ${_map(published['package'])['owner']}/${_map(published['package'])['name']}. Install it from Apps.',
                key: const ValueKey('published-app'),
              ),
            if (status == _failed && '${draft['error'] ?? ''}'.isNotEmpty)
              Text(
                '${draft['error']}',
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
          ],
        ],
      ),
    );
  }
}
