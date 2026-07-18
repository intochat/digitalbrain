import 'package:flutter/material.dart';

import 'connection_gateway.dart';
import 'connection_models.dart';

class ConnectionsPage extends StatefulWidget {
  const ConnectionsPage({
    required this.gateway,
    this.onConnect,
    this.resolveConnectUri = resolveConnectionConnectUri,
    super.key,
  });

  final ConnectionGateway gateway;
  final ValueChanged<Uri>? onConnect;
  final Uri Function(String connectPath) resolveConnectUri;

  @override
  State<ConnectionsPage> createState() => _ConnectionsPageState();
}

class _ConnectionsPageState extends State<ConnectionsPage> {
  late Future<List<ConnectionItem>> _connections;

  @override
  void initState() {
    super.initState();
    _connections = widget.gateway.loadConnections();
  }

  void _reload() {
    setState(() {
      _connections = widget.gateway.loadConnections();
    });
  }

  void _openConnect(ConnectionItem connection) {
    final path = connection.connectPath;
    if (path == null || path.isEmpty) return;
    final onConnect = widget.onConnect;
    if (onConnect == null) return;
    onConnect(widget.resolveConnectUri(path));
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Connections'),
        actions: [
          IconButton(
            tooltip: 'Reload Connections',
            onPressed: _reload,
            icon: const Icon(Icons.refresh),
          ),
          const SizedBox(width: 12),
        ],
      ),
      body: FutureBuilder<List<ConnectionItem>>(
        future: _connections,
        builder: (context, snapshot) {
          if (snapshot.connectionState == ConnectionState.waiting) {
            return const Center(child: CircularProgressIndicator());
          }
          if (snapshot.hasError) {
            return _Failure(onRetry: _reload);
          }
          final connections = snapshot.data ?? const <ConnectionItem>[];
          if (connections.isEmpty) {
            return const _EmptyState();
          }
          return ListView.separated(
            padding: const EdgeInsets.all(24),
            itemCount: connections.length,
            separatorBuilder: (_, _) => const SizedBox(height: 12),
            itemBuilder: (context, index) {
              final connection = connections[index];
              return _ConnectionCard(
                connection: connection,
                onConnect: connection.canConnect
                    ? () => _openConnect(connection)
                    : null,
              );
            },
          );
        },
      ),
    );
  }
}

class _ConnectionCard extends StatelessWidget {
  const _ConnectionCard({required this.connection, required this.onConnect});

  final ConnectionItem connection;
  final VoidCallback? onConnect;

  @override
  Widget build(BuildContext context) {
    final capabilities = connection.visibleCapabilityIds;
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Icon(Icons.link_outlined),
                const SizedBox(width: 16),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        connection.displayName,
                        style: Theme.of(context).textTheme.titleMedium,
                      ),
                      const SizedBox(height: 8),
                      Wrap(
                        spacing: 8,
                        runSpacing: 8,
                        children: [
                          Chip(
                            label: Text(connection.health.label),
                            visualDensity: VisualDensity.compact,
                          ),
                          Text(
                            connection.provider,
                            style: Theme.of(context).textTheme.bodyMedium,
                          ),
                        ],
                      ),
                      if (connection.healthDetail case final detail?) ...[
                        const SizedBox(height: 8),
                        Text(detail),
                      ],
                    ],
                  ),
                ),
                if (onConnect != null)
                  FilledButton(
                    onPressed: onConnect,
                    child: Text(
                      connection.health == ConnectionHealth.needsReauth
                          ? 'Reconnect'
                          : 'Connect',
                    ),
                  ),
              ],
            ),
            if (capabilities.isNotEmpty) ...[
              const SizedBox(height: 16),
              Text(
                'Unlocked capabilities',
                style: Theme.of(context).textTheme.titleSmall,
              ),
              const SizedBox(height: 8),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  for (final capabilityId in capabilities)
                    Chip(
                      label: Text(capabilityId),
                      visualDensity: VisualDensity.compact,
                    ),
                ],
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class _EmptyState extends StatelessWidget {
  const _EmptyState();

  @override
  Widget build(BuildContext context) => Center(
    child: Padding(
      padding: const EdgeInsets.all(32),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Icon(Icons.link_outlined, size: 48),
          const SizedBox(height: 16),
          Text(
            'No Connections yet',
            style: Theme.of(context).textTheme.titleLarge,
          ),
          const SizedBox(height: 8),
          const Text('Connect apps to unlock capabilities for Features.'),
        ],
      ),
    ),
  );
}

class _Failure extends StatelessWidget {
  const _Failure({required this.onRetry});

  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) => Center(
    child: Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        const Text('Connections could not be loaded.'),
        const SizedBox(height: 12),
        OutlinedButton(onPressed: onRetry, child: const Text('Retry')),
      ],
    ),
  );
}
