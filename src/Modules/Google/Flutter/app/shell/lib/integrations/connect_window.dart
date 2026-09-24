import 'dart:async';

import 'package:flutter/material.dart';

typedef ConnectionsRequest = Future<Object?> Function(
  String path, {
  Map<String, Object?>? body,
});

const connectionSources = <(String, String)>[
  ('supabase', 'Supabase database'),
  ('salesforce', 'Salesforce'),
  ('gmail', 'Gmail'),
  ('webresearch', 'Web research'),
];

/// Connect window: pick a source, paste a connection string (masked) or start OAuth, probe the
/// credential through the backend, and show connected/expired/failing. The secret is never shown.
class ConnectWindow extends StatefulWidget {
  const ConnectWindow({super.key, required this.request, this.oauthUrl, this.onOpen});

  final ConnectionsRequest request;
  final Uri? oauthUrl;
  final Future<void> Function(Uri url)? onOpen;

  @override
  State<ConnectWindow> createState() => _ConnectWindowState();
}

class _ConnectWindowState extends State<ConnectWindow> {
  final _connectionId = TextEditingController();
  final _value = TextEditingController();
  List<Map<String, dynamic>> connections = [];
  String source = connectionSources.first.$1;
  String? error;
  String? notice;
  bool busy = false;
  bool loaded = false;
  int generation = 0;

  @override
  void initState() {
    super.initState();
    unawaited(load());
  }

  @override
  void dispose() {
    _connectionId.dispose();
    _value.dispose();
    super.dispose();
  }

  Future<void> load() async {
    final ticket = ++generation;
    setState(() => busy = true);
    try {
      final result = await widget.request('');
      if (!mounted || ticket != generation) return;
      setState(() {
        connections = [
          for (final item in (result as List? ?? const []))
            Map<String, dynamic>.from(item as Map),
        ];
        loaded = true;
        error = null;
      });
    } catch (failure) {
      if (mounted && ticket == generation) {
        setState(() => error = 'Connections are unavailable. $failure');
      }
    } finally {
      if (mounted && ticket == generation) setState(() => busy = false);
    }
  }

  Future<void> connect() async {
    if (_connectionId.text.trim().isEmpty || _value.text.isEmpty) {
      setState(() => error = 'Provide a connection name and a value.');
      return;
    }
    final ticket = ++generation;
    setState(() {
      busy = true;
      notice = null;
      error = null;
    });
    final secret = _value.text;
    _value.clear();
    try {
      final result = await widget.request(
        'connect',
        body: {
          'source': source,
          'connectionId': _connectionId.text.trim(),
          'label': source,
          'value': secret,
        },
      );
      if (!mounted || ticket != generation) return;
      final record = Map<String, dynamic>.from(result as Map);
      setState(() {
        _connectionId.clear();
        notice = 'Connected ${record['source']} (${record['status']}).';
      });
      await load();
    } catch (failure) {
      if (mounted && ticket == generation) {
        setState(() => error = 'The connection was refused. $failure');
      }
    } finally {
      if (mounted && ticket == generation) setState(() => busy = false);
    }
  }

  Future<void> probe(String id) => act('probe', id, 'Probed $id.');
  Future<void> disconnect(String id) => act('disconnect', id, 'Disconnected $id.');

  Future<void> act(String action, String id, String message) async {
    final ticket = ++generation;
    setState(() {
      busy = true;
      notice = null;
      error = null;
    });
    try {
      await widget.request(action, body: {'connectionId': id});
      if (!mounted || ticket != generation) return;
      setState(() => notice = message);
      await load();
    } catch (failure) {
      if (mounted && ticket == generation) {
        setState(() => error = '$action failed. $failure');
      }
    } finally {
      if (mounted && ticket == generation) setState(() => busy = false);
    }
  }

  Future<void> startOAuth() async {
    final url = widget.oauthUrl;
    final open = widget.onOpen;
    if (url == null || open == null) {
      setState(() => error = 'OAuth sign-in is unavailable in this session.');
      return;
    }
    await open(url);
  }

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      Padding(
        padding: const EdgeInsets.fromLTRB(16, 10, 8, 10),
        child: Row(
          children: [
            const Expanded(
              child: Text(
                'Connections',
                style: TextStyle(fontSize: 20, fontWeight: FontWeight.w600),
              ),
            ),
            IconButton(
              tooltip: 'Refresh connections',
              onPressed: load,
              icon: const Icon(Icons.refresh),
            ),
          ],
        ),
      ),
      if (error != null)
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16),
          child: Text(
            error!,
            style: TextStyle(color: Theme.of(context).colorScheme.error),
          ),
        ),
      if (notice != null)
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16),
          child: Text(notice!),
        ),
      if (busy) const LinearProgressIndicator(minHeight: 2),
      const Divider(height: 1),
      Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            DropdownButtonFormField<String>(
              key: const Key('connect_source'),
              initialValue: source,
              decoration: const InputDecoration(
                labelText: 'Source',
                border: OutlineInputBorder(),
              ),
              items: [
                for (final (id, label) in connectionSources)
                  DropdownMenuItem(value: id, child: Text(label)),
              ],
              onChanged: busy
                  ? null
                  : (value) {
                      if (value != null) setState(() => source = value);
                    },
            ),
            const SizedBox(height: 12),
            TextField(
              key: const Key('connect_name'),
              controller: _connectionId,
              decoration: const InputDecoration(
                labelText: 'Connection name',
                border: OutlineInputBorder(),
              ),
            ),
            const SizedBox(height: 12),
            TextField(
              key: const Key('connect_value'),
              controller: _value,
              obscureText: true,
              autocorrect: false,
              enableSuggestions: false,
              decoration: const InputDecoration(
                labelText: 'Connection string or token',
                border: OutlineInputBorder(),
              ),
            ),
            const SizedBox(height: 12),
            FilledButton.icon(
              key: const Key('connect_submit'),
              onPressed: busy ? null : connect,
              icon: const Icon(Icons.link),
              label: const Text('Connect'),
            ),
            if (widget.oauthUrl != null)
              TextButton.icon(
                onPressed: busy ? null : startOAuth,
                icon: const Icon(Icons.open_in_new),
                label: const Text('Sign in with the provider'),
              ),
          ],
        ),
      ),
      const Divider(height: 1),
      Expanded(
        child: !loaded
            ? (error != null
                  ? const SizedBox.shrink()
                  : const Center(child: CircularProgressIndicator()))
            : connections.isEmpty
            ? const Center(child: Text('No connections yet.'))
            : ListView(
                children: [
                  for (final connection in connections)
                    ListTile(
                      leading: Icon(
                        connection['status'] == 'Connected'
                            ? Icons.check_circle_outline
                            : Icons.error_outline,
                      ),
                      title: Text('${connection['source']} · ${connection['id']}'),
                      subtitle: Text('${connection['status']}'),
                      trailing: Row(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          IconButton(
                            tooltip: 'Probe connection',
                            onPressed: busy
                                ? null
                                : () => probe('${connection['id']}'),
                            icon: const Icon(Icons.network_check),
                          ),
                          IconButton(
                            tooltip: 'Disconnect',
                            onPressed: busy
                                ? null
                                : () => disconnect('${connection['id']}'),
                            icon: const Icon(Icons.link_off),
                          ),
                        ],
                      ),
                    ),
                ],
              ),
      ),
    ],
  );
}
