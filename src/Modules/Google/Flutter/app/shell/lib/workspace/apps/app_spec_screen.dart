import 'dart:async';

import 'package:flutter/material.dart';

import 'app_spec_view.dart';
import 'packages_screen.dart';

// One app's page: what it does, as scenarios the brain verified against a scratch installation.
class AppSpecScreen extends StatefulWidget {
  const AppSpecScreen({
    super.key,
    required this.packageId,
    required this.title,
    required this.request,
  });

  final String packageId;
  final String title;
  final PackagesRequest request;

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
    unawaited(_perform(() => widget.request('GET', '$_path/spec')));
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
    final green = verification['green'] == true;
    return Scaffold(
      appBar: AppBar(
        title: Text(widget.title),
        actions: [
          TextButton.icon(
            key: const ValueKey('verify-app'),
            onPressed: _busy
                ? null
                : () => _perform(() => widget.request('POST', '$_path/verify')),
            icon: const Icon(Icons.play_arrow),
            label: const Text('Run scenarios'),
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
                  : green
                  ? 'Every scenario passed.'
                  : 'Some scenarios do not pass yet, so this revision is not in the marketplace.',
              key: const ValueKey('verification-summary'),
            ),
            Text(
              '${spec['runtime']} app · revision ${'${_map(spec['revision'])['revision']}'.substring(0, 12)}',
              style: Theme.of(context).textTheme.bodySmall,
            ),
            const Divider(),
            if (specText.isEmpty)
              const Text('This app has no spec.')
            else
              AppSpecView(spec: specText, run: run),
          ],
        ],
      ),
    );
  }
}
