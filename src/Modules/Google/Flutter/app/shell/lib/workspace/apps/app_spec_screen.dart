import 'dart:async';

import 'package:flutter/material.dart';

import 'app_spec_view.dart';
import 'apps_screen.dart';
import 'app_document.dart';
import 'create_app_screen.dart';
import 'spec_vocabulary.dart';
import 'app_verification_summary.dart';
import 'spec_document.dart';

// One app's page: what it does, as scenarios the brain verified against a scratch installation.
class AppSpecScreen extends StatefulWidget {
  const AppSpecScreen({
    super.key,
    required this.packageId,
    required this.title,
    required this.request,
    this.revision,
    this.onOpen,
  });

  final String packageId;
  final String title;
  final AppsRequest request;
  final String? revision;
  final VoidCallback? onOpen;

  @override
  State<AppSpecScreen> createState() => _AppSpecScreenState();
}

class _AppSpecScreenState extends State<AppSpecScreen> {
  Map<String, dynamic>? _spec;
  bool _busy = false;
  String? _error;

  String get _path =>
      '/packages/${widget.packageId.split('/').map(Uri.encodeComponent).join('/')}';

  static Map<String, dynamic> _map(dynamic value) =>
      value is Map ? Map<String, dynamic>.from(value) : <String, dynamic>{};

  @override
  void initState() {
    super.initState();
    unawaited(
      _perform(
        () => widget.request(
          'GET',
          '$_path/spec${widget.revision == null ? '' : '?revision=${Uri.encodeQueryComponent(widget.revision!)}'}',
        ),
      ),
    );
  }

  Future<void> _perform(Future<dynamic> Function() load) async {
    if (_busy) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final spec = _map(await load());
      if (mounted) setState(() => _spec = spec);
    } catch (error) {
      if (mounted) setState(() => _error = error.toString());
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final spec = _spec;
    final specText = '${spec?['spec'] ?? ''}';
    final verification = _map(spec?['verification']);
    final run = verification.isEmpty ? null : _map(verification['run']);
    final read = _map(spec?['documentReadResult']);
    AppDocument? document;
    String? documentError = read['error'] as String?;
    if (read['document'] != null) {
      try {
        document = AppDocument.fromJson(_map(read['document']));
      } on FormatException catch (e) {
        documentError = e.message;
      }
    }
    final revision = '${_map(spec?['revision'])['revision'] ?? ''}';
    final scenarios = document?.scenarios ?? parseSpec(specText).scenarios;
    final result = run == null ? null : VerificationRun.fromJson(run);
    final passed = scenarios
        .where((s) => scenarioStatus(s, scenarios, result, true) == 'Passed')
        .length;
    final failed = scenarios
        .where((s) => scenarioStatus(s, scenarios, result, true) == 'Failed')
        .length;
    final live = scenarios.where((s) => s.isLive).length;
    return Scaffold(
      appBar: AppBar(
        title: Text(widget.title),
        actions: [
          if (widget.onOpen != null)
            TextButton(onPressed: widget.onOpen, child: const Text('Open app')),
          if (spec?['canEdit'] == true && documentError == null)
            TextButton(
              onPressed: _busy
                  ? null
                  : () => Navigator.of(context).push(
                      MaterialPageRoute<void>(
                        builder: (_) => CreateAppScreen(
                          request: widget.request,
                          initialRevision: _map(spec?['revision']),
                        ),
                      ),
                    ),
              child: const Text('Edit app'),
            ),
          TextButton.icon(
            key: const ValueKey('verify-app'),
            onPressed: _busy || revision.isEmpty
                ? null
                : () => _perform(
                    () => widget.request(
                      'POST',
                      '$_path/verify?revision=${Uri.encodeQueryComponent(revision)}',
                    ),
                  ),
            icon: const Icon(Icons.play_arrow),
            label: const Text('Run checks'),
          ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          if (_busy) const LinearProgressIndicator(),
          if (_error != null)
            Text(
              _error!,
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
          if (spec != null) ...[
            Text(
              verification.isEmpty
                  ? 'Not verified yet.'
                  : '$passed passed · $failed failed · $live live · ${scenarios.length - passed - failed - live} not run',
              key: const ValueKey('verification-summary'),
            ),
            Text(
              'Revision $revision${verification['verifiedAt'] == null ? '' : ' · Checked ${verification['verifiedAt']}'}',
              style: Theme.of(context).textTheme.bodySmall,
            ),
            const Divider(),
            if (documentError != null) SelectableText(documentError),
            if (document == null && specText.isNotEmpty)
              const Text(
                'Document view · behavior links have not been authored.',
              ),
            if (specText.isEmpty && document == null)
              const Text('This app has no spec.')
            else
              ...AppSpecView(
                spec: specText,
                run: run,
                document: document,
                vocabulary: SpecToken.read(spec['vocabulary']),
                files: _map(spec['files']).map((k, v) => MapEntry(k, '$v')),
                sourceRevision: revision,
              ).childrenFor(context),
          ],
        ],
      ),
    );
  }
}
