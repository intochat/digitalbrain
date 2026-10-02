import 'dart:async';
import 'dart:math';

import 'package:flutter/material.dart';

import 'app_spec_view.dart';
import 'apps_screen.dart';
import 'app_document.dart';
import 'behavior_authoring_view.dart';
import 'spec_vocabulary.dart';

// Numeric values of DigitalBrain.Apps.AppDraftStatus on the wire.
const _drafted = 1;
const _building = 2;
const _published = 3;
const _failed = 4;

// Create an app by describing it: the Author writes its spec, you read and adjust it, and the
// Builder writes the tests and implementation. The app reaches the marketplace only when its tests pass.
class CreateAppScreen extends StatefulWidget {
  const CreateAppScreen({
    super.key,
    required this.request,
    this.draftId,
    this.initialRevision,
  });

  final AppsRequest request;
  final String? draftId;
  final Map<String, dynamic>? initialRevision;

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
  void initState() {
    super.initState();
    // Reopening an existing draft picks up where the person left off.
    if (widget.initialRevision != null) {
      unawaited(
        _perform(
          'Opening an editable draft…',
          () => widget.request('POST', '$_path/import', {
            'revision': widget.initialRevision,
            'expectedRevision': 0,
          }),
        ),
      );
    } else if (widget.draftId != null) {
      unawaited(
        _perform('Loading the draft…', () => widget.request('GET', _path)),
      );
    }
  }

  Future<void> _convert() async {
    try {
      final proposal = AppDocument.fromJson(
        _map(
          await widget.request('POST', '$_path/conversion', {
            'expectedRevision': _map(_view?['draft'])['revision'],
          }),
        ),
      );
      if (!mounted) return;
      final accepted = await showDialog<bool>(
        context: context,
        builder: (context) => AlertDialog(
          title: const Text('Convert to behavior blocks?'),
          content: SizedBox(
            width: 560,
            child: SingleChildScrollView(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const Text(
                    'Your scenarios are preserved. Add behaviors and link scenarios explicitly; no source links are guessed.',
                  ),
                  const SizedBox(height: 12),
                  for (final s in proposal.scenarios)
                    ListTile(title: Text(s.name), subtitle: Text(s.body)),
                  if (proposal.scenarios.isEmpty) Text(proposal.preamble),
                ],
              ),
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('Cancel'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(context, true),
              child: const Text('Convert'),
            ),
          ],
        ),
      );
      if (accepted == true) {
        await _perform(
          'Saving blocks…',
          () => widget.request('PUT', '$_path/document', {
            'expectedRevision': _map(_view?['draft'])['revision'],
            'document': proposal.toJson(),
          }),
        );
      }
    } catch (e) {
      if (mounted) setState(() => _error = e.toString());
    }
  }

  @override
  void dispose() {
    _describe.dispose();
    _revise.dispose();
    _spec.dispose();
    super.dispose();
  }

  Future<void> _perform(
    String activity,
    Future<dynamic> Function() call,
  ) async {
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
        if (_describe.text.isEmpty) {
          _describe.text = '${_map(view['draft'])['request'] ?? ''}';
        }
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
    if (draft['document'] != null) {
      return BehaviorAuthoringView(
        draftId: _draftId,
        request: widget.request,
        initial: _view!,
      );
    }
    final verification = _map(_view?['verification']);
    final specText = '${draft['spec'] ?? ''}';
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
          if (specText.isNotEmpty) ...[
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
              AppSpecView(
                spec: specText,
                run: verification.isEmpty ? null : _map(verification['run']),
                vocabulary: SpecToken.read(_view?['vocabulary']),
                current:
                    _view?['verificationCurrent'] == true ||
                    status == _published,
              ),
            Wrap(
              spacing: 8,
              children: [
                TextButton(
                  onPressed: _busy ? null : _convert,
                  child: const Text('Convert to behavior blocks'),
                ),
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
                    onPressed: _busy
                        ? null
                        : () => _perform(
                            'The Builder is making every scenario pass. This can take a few minutes…',
                            () => widget.request('POST', '$_path/build'),
                          ),
                    child: const Text('Build and publish'),
                  ),
                ],
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
