import 'dart:async';

import 'package:flutter/material.dart';

import 'connect_window.dart';
export 'connect_window.dart' show connectionStatus;

String _serviceName(Object? source) => switch (source) {
  'gmail' => 'Google · Gmail',
  'salesforce' => 'Salesforce',
  'supabase' => 'Supabase',
  'webresearch' => 'Web research',
  'github' => 'GitHub',
  _ => '$source',
};

class ConnectionsSettings extends StatefulWidget {
  const ConnectionsSettings({
    super.key,
    this.request,
    this.serviceRequest,
    this.onOpen,
  });
  final ConnectionsRequest? request;
  final ConnectionsRequest? serviceRequest;
  final Future<void> Function(Uri)? onOpen;
  @override
  State<ConnectionsSettings> createState() => _ConnectionsSettingsState();
}

class _ConnectionsSettingsState extends State<ConnectionsSettings> {
  List<Map<String, dynamic>> _connections = [];
  List<Map<String, dynamic>> _services = [];
  bool _busy = false;
  bool _add = false;
  String? _error;
  String? _notice;
  int _generation = 0;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void didUpdateWidget(ConnectionsSettings oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.request != widget.request ||
        oldWidget.serviceRequest != widget.serviceRequest) {
      _connections = [];
      _services = [];
      _notice = null;
      unawaited(_load());
    }
  }

  List<Map<String, dynamic>> _rows(Object? value) => [
    for (final row in value as List? ?? [])
      Map<String, dynamic>.from(row as Map),
  ];
  Future<void> _load() async {
    final generation = ++_generation;
    final request = widget.request;
    final serviceRequest = widget.serviceRequest;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final connections = request == null
          ? <Map<String, dynamic>>[]
          : _rows(await request(''));
      final services = serviceRequest == null
          ? <Map<String, dynamic>>[]
          : _rows(await serviceRequest(''));
      if (mounted && generation == _generation) {
        setState(() {
          _connections = connections;
          _services = services;
        });
      }
    } catch (_) {
      if (mounted && generation == _generation) {
        setState(
          () => _error = 'Connections are unavailable. Refresh to try again.',
        );
      }
    } finally {
      if (mounted && generation == _generation) setState(() => _busy = false);
    }
  }

  Future<void> _act(String action, String id) async {
    final generation = _generation;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await widget.request!(action, body: {'connectionId': id});
      if (mounted && generation == _generation) await _load();
    } catch (_) {
      if (mounted && generation == _generation) {
        setState(() {
          _busy = false;
          _error = 'The connection action failed. Please try again.';
        });
      }
    }
  }

  Future<void> _start(String provider) async {
    final generation = _generation;
    final open = widget.onOpen!;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final result = await widget.serviceRequest!('$provider/start', body: {});
      final url = Uri.parse((result as Map)['url'] as String);
      if (!url.hasAuthority ||
          (url.scheme != 'https' && url.scheme != 'http')) {
        throw const FormatException();
      }
      if (!mounted || generation != _generation) return;
      await open(url);
      if (mounted && generation == _generation) {
        setState(
          () => _notice = 'Authorization opened in your browser. Complete the provider steps, then refresh to see the authorized service. Configured credentials still require service verification.',
        );
      }
    } catch (_) {
      if (mounted && generation == _generation) {
        setState(
          () => _error = 'Provider authorization could not start. Check the service configuration and try again.',
        );
      }
    } finally {
      if (mounted && generation == _generation) setState(() => _busy = false);
    }
  }

  Future<void> _advanced() async {
    await showDialog<void>(
      context: context,
      builder: (context) => Dialog(
        child: SizedBox(
          width: 560,
          height: 680,
          child: ConnectWindow(request: widget.request!),
        ),
      ),
    );
    if (mounted) await _load();
  }

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      Row(
        children: [
          const Expanded(
            child: Text(
              'Connections',
              style: TextStyle(fontSize: 22, fontWeight: FontWeight.w600),
            ),
          ),
          IconButton(
            tooltip: 'Refresh connections',
            onPressed: _busy ? null : _load,
            icon: const Icon(Icons.refresh),
          ),
        ],
      ),
      const Text(
        'Connections are scoped to your account and workspace. Configured credentials have not yet been verified.',
      ),
      const SizedBox(height: 16),
      if (_busy) const LinearProgressIndicator(),
      if (_error != null)
        Text(
          _error!,
          style: TextStyle(color: Theme.of(context).colorScheme.error),
        ),
      if (_notice != null) Text(_notice!),
      if (widget.request == null)
        const Text('Connect to a workspace to manage services.')
      else if (_connections.isEmpty && !_busy && _error == null)
        const Padding(
          padding: EdgeInsets.symmetric(vertical: 16),
          child: Text('No connections configured yet.'),
        ),
      for (final row in _connections)
        Card(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  '${_serviceName(row['integrationId'])}${'${row['id']}'.startsWith('oauth:') ? '' : ' · ${row['id']}'}',
                  style: const TextStyle(fontWeight: FontWeight.w600),
                ),
                Text(connectionStatus(row['status'])),
                if (row['account'] is String) Text(row['account'] as String),
                if (row['integrationId'] == 'gmail')
                  const Text(
                    'Gmail credential only. Calendar and Drive access is not established.',
                  ),
                Wrap(
                  spacing: 8,
                  children: [
                    if (_services.any(
                      (service) =>
                          service['id'] == row['integrationId'] &&
                          service['available'] == true,
                    ))
                      TextButton.icon(
                        onPressed: _busy || widget.onOpen == null
                            ? null
                            : () => _start('${row['integrationId']}'),
                        icon: const Icon(Icons.login),
                        label: const Text('Reconnect'),
                      ),
                    TextButton.icon(
                      onPressed: _busy
                          ? null
                          : () => _act('probe', '${row['id']}'),
                      icon: const Icon(Icons.network_check),
                      label: const Text('Check credential'),
                    ),
                    TextButton.icon(
                      onPressed: _busy
                          ? null
                          : () => _act('disconnect', '${row['id']}'),
                      icon: const Icon(Icons.link_off),
                      label: const Text('Disconnect'),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ),
      const SizedBox(height: 16),
      OutlinedButton.icon(
        onPressed: widget.request == null
            ? null
            : () => setState(() => _add = !_add),
        icon: Icon(_add ? Icons.expand_less : Icons.add),
        label: const Text('Add connection'),
      ),
      if (_add) ...[
        for (final service in _services)
          ListTile(
            contentPadding: EdgeInsets.zero,
            title: Text('${service['name']}'),
            subtitle: Text(
              '${service['capabilities']}\n${service['available'] == true ? 'Ready to authorize.' : service['reason'] ?? 'Unavailable: operator setup required.'}',
            ),
            trailing: TextButton(
              onPressed:
                  _busy || service['available'] != true || widget.onOpen == null
                  ? null
                  : () => _start('${service['id']}'),
              child: const Text('Connect'),
            ),
          ),
        TextButton.icon(
          onPressed: _busy ? null : _advanced,
          icon: const Icon(Icons.key),
          label: const Text('Advanced: connection string or token'),
        ),
      ],
    ],
  );
}
