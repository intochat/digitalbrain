import 'package:flutter/material.dart';

import 'feature_catalog_gateway.dart';

class FeatureCatalogPage extends StatefulWidget {
  const FeatureCatalogPage({
    required this.gateway,
    required this.onOpenFeature,
    required this.onCreateFeature,
    this.onOpenConnections,
    super.key,
  });

  final FeatureCatalogGateway gateway;
  final ValueChanged<String> onOpenFeature;
  final VoidCallback onCreateFeature;
  final VoidCallback? onOpenConnections;

  @override
  State<FeatureCatalogPage> createState() => _FeatureCatalogPageState();
}

class _FeatureCatalogPageState extends State<FeatureCatalogPage> {
  late Future<List<FeatureCatalogItem>> _features;

  @override
  void initState() {
    super.initState();
    _features = widget.gateway.loadFeatures();
  }

  void _reload() {
    setState(() => _features = widget.gateway.loadFeatures());
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Features'),
        actions: [
          IconButton(
            tooltip: 'Reload Features',
            onPressed: _reload,
            icon: const Icon(Icons.refresh),
          ),
          FilledButton.icon(
            onPressed: widget.onCreateFeature,
            icon: const Icon(Icons.add),
            label: const Text('Create Feature'),
          ),
          const SizedBox(width: 12),
        ],
      ),
      body: FutureBuilder<List<FeatureCatalogItem>>(
        future: _features,
        builder: (context, snapshot) {
          if (snapshot.connectionState == ConnectionState.waiting) {
            return const Center(child: CircularProgressIndicator());
          }
          if (snapshot.hasError) {
            return _Failure(onRetry: _reload);
          }
          final features = snapshot.data ?? const <FeatureCatalogItem>[];
          if (features.isEmpty) {
            return _EmptyState(
              onCreateFeature: widget.onCreateFeature,
              onOpenConnections: widget.onOpenConnections,
            );
          }
          return ListView.separated(
            padding: const EdgeInsets.all(24),
            itemCount: features.length,
            separatorBuilder: (_, _) => const SizedBox(height: 12),
            itemBuilder: (context, index) {
              final feature = features[index];
              return _FeatureCard(
                feature: feature,
                onTap: () => widget.onOpenFeature(feature.draftId),
              );
            },
          );
        },
      ),
    );
  }
}

class _FeatureCard extends StatelessWidget {
  const _FeatureCard({required this.feature, required this.onTap});

  final FeatureCatalogItem feature;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final status = feature.status == FeatureCatalogStatus.installed
        ? 'Installed'
        : 'Draft';
    final theme = Theme.of(context);
    return Card(
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(12),
        child: Padding(
          padding: const EdgeInsets.all(20),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Icon(Icons.auto_awesome_outlined),
              const SizedBox(width: 16),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      feature.goal,
                      style: theme.textTheme.titleMedium,
                    ),
                    const SizedBox(height: 4),
                    Text(
                      'Feature specialist',
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: theme.colorScheme.onSurfaceVariant,
                      ),
                    ),
                    const SizedBox(height: 8),
                    Wrap(
                      spacing: 8,
                      runSpacing: 8,
                      crossAxisAlignment: WrapCrossAlignment.center,
                      children: [
                        _StatusChip(label: status),
                        Text(
                          'Version ${feature.revision}',
                          style: theme.textTheme.bodyMedium,
                        ),
                      ],
                    ),
                  ],
                ),
              ),
              const Icon(Icons.chevron_right),
            ],
          ),
        ),
      ),
    );
  }
}

class _StatusChip extends StatelessWidget {
  const _StatusChip({required this.label});

  final String label;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final isInstalled = label == 'Installed';
    final background = isInstalled
        ? theme.colorScheme.primaryContainer
        : theme.colorScheme.surfaceContainerHighest;
    final foreground = isInstalled
        ? theme.colorScheme.onPrimaryContainer
        : theme.colorScheme.onSurfaceVariant;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
      decoration: BoxDecoration(
        color: background,
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        label,
        style: theme.textTheme.labelMedium?.copyWith(color: foreground),
      ),
    );
  }
}

class _EmptyState extends StatelessWidget {
  const _EmptyState({
    required this.onCreateFeature,
    this.onOpenConnections,
  });

  final VoidCallback onCreateFeature;
  final VoidCallback? onOpenConnections;

  @override
  Widget build(BuildContext context) => Center(
    child: Padding(
      padding: const EdgeInsets.all(32),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Icon(Icons.auto_awesome_outlined, size: 48),
          const SizedBox(height: 16),
          Text(
            'Install a Feature specialist',
            style: Theme.of(context).textTheme.titleLarge,
            textAlign: TextAlign.center,
          ),
          const SizedBox(height: 8),
          const Text(
            'Features are specialists that automate a goal. Start with something like Enrich Salesforce account from Gmail, then open Ask to describe what you want.',
            textAlign: TextAlign.center,
          ),
          const SizedBox(height: 20),
          FilledButton.icon(
            onPressed: onCreateFeature,
            icon: const Icon(Icons.add),
            label: const Text('Create Feature'),
          ),
          if (onOpenConnections != null) ...[
            const SizedBox(height: 12),
            OutlinedButton.icon(
              onPressed: onOpenConnections,
              icon: const Icon(Icons.link),
              label: const Text('Connect apps'),
            ),
          ],
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
        const Text('Features could not be loaded.'),
        const SizedBox(height: 12),
        OutlinedButton(onPressed: onRetry, child: const Text('Retry')),
      ],
    ),
  );
}
