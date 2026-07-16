import 'package:flutter/material.dart';

import 'feature_catalog_gateway.dart';

class FeatureCatalogPage extends StatefulWidget {
  const FeatureCatalogPage({
    required this.gateway,
    required this.onOpenFeature,
    required this.onCreateFeature,
    super.key,
  });

  final FeatureCatalogGateway gateway;
  final ValueChanged<String> onOpenFeature;
  final VoidCallback onCreateFeature;

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
            return _EmptyState(onCreateFeature: widget.onCreateFeature);
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
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                    const SizedBox(height: 8),
                    Text('Version ${feature.revision} · $status'),
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

class _EmptyState extends StatelessWidget {
  const _EmptyState({required this.onCreateFeature});

  final VoidCallback onCreateFeature;

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
            'No Features yet',
            style: Theme.of(context).textTheme.titleLarge,
          ),
          const SizedBox(height: 8),
          const Text(
            'Describe what you want in Chat and DigitalBrain will open it in Feature Studio.',
          ),
          const SizedBox(height: 20),
          FilledButton.icon(
            onPressed: onCreateFeature,
            icon: const Icon(Icons.add),
            label: const Text('Create Feature'),
          ),
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
