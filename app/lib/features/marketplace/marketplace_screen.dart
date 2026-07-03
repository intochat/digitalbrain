import 'package:flutter/material.dart';
import 'package:forui/forui.dart';

import 'models/pack.dart';

// MIGRATED: All UI now via neuron UiSurface / UiWidgetTree + rich ForUI kit (no static screens).
// This file kept for reference / dev authoring fallback only. Use shell targets for runtime kit.
class MarketplaceScreen extends StatefulWidget {
  final void Function(String name, Map<String, Object?>)? onEvent;

  const MarketplaceScreen({super.key, this.onEvent});

  @override
  State<MarketplaceScreen> createState() => _MarketplaceScreenState();
}

class _MarketplaceScreenState extends State<MarketplaceScreen> {
  String _query = '';
  late final List<Pack> _packs;
  late final List<String> _allPackNames;

  // Dev authoring state for dogfooding: develop DigitalBrain using DigitalBrain + UI
  // Code must be valid C# implementing DigitalBrain.Core.IPackBehavior (Respond + optional manifest).
  // After Publish+Install it embodies via Roslyn+ALC, appears in Installed launcher, Open/Run exercise it + can emit surfaces.
  String _devPackName = 'My.DevFeature';
  String _devCode = '''
public sealed class MyDevFeature : DigitalBrain.Core.IPackBehavior
{
    public string Respond(string input) => "Dev feature handled: " + (input ?? "");
    // To support custom synapses override GetManifest() and Handle() returning PackEmission or UiSurface etc.
}
''';

  static const List<String> _recommendedNames = [
    'DigitalBrain.UI.ForUI',
    'ClosedLoop.Reviewer',
    'INO.Coder',
    'Context.Vector',
    'Economics.Stripe',
    'NeuroPack.Sandbox',
  ];

  @override
  void initState() {
    super.initState();
    _packs = _buildTestPacks();
    _allPackNames = _packs.map((p) => p.name).toList();
  }

  List<Pack> get _filteredPacks {
    final q = _query.trim().toLowerCase();
    if (q.isEmpty) return _packs;
    return _packs.where((p) {
      final hay = '${p.name} ${p.description} ${p.owner} ${p.tags.join(' ')}'
          .toLowerCase();
      return hay.contains(q);
    }).toList();
  }

  List<Pack> get _recommended {
    final q = _query.trim().toLowerCase();
    final base = _packs
        .where((p) => _recommendedNames.contains(p.name))
        .toList();
    if (q.isEmpty) return base;
    return base.where((p) {
      final hay = '${p.name} ${p.description} ${p.tags.join(' ')}'
          .toLowerCase();
      return hay.contains(q);
    }).toList();
  }

  List<Pack> _buildTestPacks() => const [
    Pack(
      name: 'DigitalBrain.UI.ForUI',
      version: '1.3.0',
      description:
          'ForUI shell, sidebar, cards, autocomplete and forms for server-driven neuron UIs.',
      owner: 'core',
      installed: true,
      tags: ['ui', 'shell', 'forui'],
    ),
    Pack(
      name: 'DigitalBrain.UI.Gallery',
      version: '0.8.1',
      description:
          'Live component explorer and previews for the ForUI + RFW kit.',
      owner: 'core',
      installed: true,
      tags: ['ui', 'demo'],
    ),
    Pack(
      name: 'ClosedLoop.Reviewer',
      version: '2.1.0',
      description:
          'LLM-powered review loop for synapses, code changes and pack behavior.',
      owner: 'core',
      installed: false,
      tags: ['ai', 'review', 'closed-loop'],
    ),
    Pack(
      name: 'INO.Coder',
      version: '1.5.4',
      description:
          'Natural language to typed C# pack authoring with safe sandbox.',
      owner: 'core',
      installed: true,
      tags: ['ino', 'dev', 'ai'],
    ),
    Pack(
      name: 'Marketplace.Broker',
      version: '0.4.2',
      description:
          'Discovery, trust scoring, licensing and one-click install flows.',
      owner: 'core',
      installed: true,
      tags: ['market', 'core'],
    ),
    Pack(
      name: 'Timeline.Visualizer',
      version: '1.0.0',
      description:
          'Causal synapse timeline, ring buffer and time-scrub with clusters.',
      owner: 'team',
      installed: false,
      tags: ['viz', 'time'],
    ),
    Pack(
      name: 'Context.Vector',
      version: '3.0.1',
      description: 'Hybrid scorer + Qdrant vector memory for neuron recall.',
      owner: 'core',
      installed: true,
      tags: ['memory', 'context'],
    ),
    Pack(
      name: 'Buddy.Einstein',
      version: '0.9.9',
      description:
          'Reasoning buddy tuned for physics, proofs and step-by-step.',
      owner: 'community',
      installed: false,
      tags: ['buddy', 'science'],
    ),
    Pack(
      name: 'Tool.WebSearch',
      version: '0.2.1',
      description:
          'Sandboxed web search with source citations and rate limits.',
      owner: 'core',
      installed: false,
      tags: ['tool', 'net'],
    ),
    Pack(
      name: 'Experience.Onboard',
      version: '1.1.1',
      description: 'Interactive first-neuron and first-pack creation guide.',
      owner: 'core',
      installed: true,
      tags: ['onboard', 'ux'],
    ),
    Pack(
      name: 'Economics.Stripe',
      version: '0.7.5',
      description: 'License key issuance, usage metering and payouts.',
      owner: 'core',
      installed: true,
      tags: ['eco', 'pay'],
    ),
    Pack(
      name: 'NeuroPack.Sandbox',
      version: '4.2.0',
      description: 'Collectible ALC loader, hot-swap embodiment and signing.',
      owner: 'core',
      installed: true,
      tags: ['kernel', 'security'],
    ),
    // Seed for self-dev scenario: a pack you can "re-develop" from the UI authoring card and re-publish
    Pack(
      name: 'Dummy.DevPack',
      version: '1.0.0-dev',
      description:
          'Example behavior pack for dogfooding development inside the running DigitalBrain + UI.',
      owner: 'self-dev',
      installed: false,
      tags: ['dev', 'self'],
    ),
  ];

  void _dispatch(String name, Map<String, Object?> args) {
    widget.onEvent?.call(name, args);
  }

  void _toggleInstall(Pack pack) {
    _dispatch('action', {
      'action': pack.installed ? 'uninstall' : 'install',
      'packName': pack.name,
      'kind': 'marketplace',
    });

    setState(() {
      final idx = _packs.indexWhere((p) => p.name == pack.name);
      if (idx != -1) {
        final cur = _packs[idx];
        _packs[idx] = Pack(
          name: cur.name,
          version: cur.version,
          description: cur.description,
          owner: cur.owner,
          installed: !cur.installed,
          tags: cur.tags,
        );
      }
    });
  }

  void _showDetails(Pack pack) {
    _dispatch('select', {'packName': pack.name, 'kind': 'marketplace'});

    showDialog(
      context: context,
      builder: (ctx) => Dialog(
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: FCard(
            title: Text(pack.name),
            subtitle: Text('${pack.version}  •  by ${pack.owner}'),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(pack.description),
                const SizedBox(height: 12),
                Wrap(
                  spacing: 6,
                  children: pack.tags
                      .map((t) => FBadge(child: Text(t)))
                      .toList(),
                ),
                const SizedBox(height: 16),
                Row(
                  children: [
                    FButton(
                      onPress: () {
                        Navigator.of(ctx).pop();
                        _toggleInstall(pack);
                      },
                      child: Text(pack.installed ? 'Uninstall' : 'Install'),
                    ),
                    const SizedBox(width: 8),
                    FButton(
                      variant: FButtonVariant.outline,
                      onPress: () => Navigator.of(ctx).pop(),
                      child: const Text('Close'),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final filtered = _filteredPacks;
    final recs = _recommended;
    final q = _query.trim();

    return Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text(
            'Marketplace',
            style: TextStyle(fontSize: 22, fontWeight: FontWeight.w600),
          ),
          const SizedBox(height: 4),
          Text(
            'Discover, install and manage NeuroPacks and buddies',
            style: TextStyle(color: FTheme.of(context).colors.mutedForeground),
          ),
          const SizedBox(height: 12),
          FAutocomplete(
            items: _allPackNames,
            hint: 'Search packs, buddies, experiences…',
            filter: (q) {
              _dispatch('search', {'query': q, 'kind': 'marketplace'});
              if (q != _query && mounted) {
                Future.microtask(() {
                  if (mounted) setState(() => _query = q);
                });
              }
              return _allPackNames
                  .where(
                    (n) =>
                        q.isEmpty || n.toLowerCase().contains(q.toLowerCase()),
                  )
                  .toList();
            },
            onSubmit: (value) {
              _dispatch('search', {
                'query': value,
                'kind': 'marketplace',
                'committed': true,
              });
              setState(() => _query = value);
            },
          ),
          const SizedBox(height: 4),
          Text(
            'Type or pick to filter live. Clear by submitting empty.',
            style: TextStyle(
              fontSize: 11,
              color: FTheme.of(context).colors.mutedForeground,
            ),
          ),
          const SizedBox(height: 12),

          // Self-development scenario: use this UI (part of the brain) to author and publish new packs
          // back into the running DigitalBrain (kernel + surfaces). "Develop DigitalBrain using DigitalBrain".
          FCard(
            title: const Text('Develop DigitalBrain using DigitalBrain + UI'),
            subtitle: const Text(
              'Author a pack (or kernel version), publish & install via the system itself. UI updates live from surfaces.',
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                TextField(
                  decoration: const InputDecoration(
                    labelText: 'Pack name (or "kernel" for self-update)',
                    hintText: 'My.DevFeature or kernel',
                  ),
                  onChanged: (v) => setState(() => _devPackName = v),
                ),
                const SizedBox(height: 8),
                TextField(
                  decoration: const InputDecoration(
                    labelText: 'Code (C# for IPackBehavior; empty for kernel)',
                  ),
                  maxLines: 5,
                  onChanged: (v) => setState(() => _devCode = v),
                ),
                const SizedBox(height: 8),
                Row(
                  children: [
                    FButton(
                      onPress: () {
                        final name = _devPackName.isEmpty
                            ? 'My.DevFeature'
                            : _devPackName;
                        _dispatch('publish', {
                          'synapseType': 'PublishToMarketplace',
                          'packName': name,
                          'version': '1.0.0-dev',
                          'code': _devCode,
                          'ownerId': 'self-dev',
                          'isPrivate': false,
                          'commissionRate': 0.05,
                          'description': 'Authored live inside DigitalBrain UI',
                        });
                        // Chain install so server emits InstalledBundles surface including the new pack (appears in launcher).
                        Future.microtask(() {
                          _dispatch('action', {
                            'synapseType': 'InstallFromMarketplace',
                            'packName': name,
                            'version': '1.0.0-dev',
                            'buyerId': 'self-dev',
                          });
                        });
                        setState(() {
                          if (!_packs.any((p) => p.name == name)) {
                            _packs.insert(
                              0,
                              Pack(
                                name: name,
                                version: '1.0.0-dev',
                                description:
                                    'Self-developed pack (published from this UI)',
                                owner: 'self-dev',
                                installed: true,
                                tags: ['dev', 'self'],
                              ),
                            );
                            if (!_allPackNames.contains(name)) {
                              _allPackNames.insert(0, name);
                            }
                          }
                        });
                      },
                      child: const Text('Publish Dev Pack'),
                    ),
                    const SizedBox(width: 8),
                    FButton(
                      variant: FButtonVariant.outline,
                      onPress: () {
                        _dispatch('publish', {
                          'synapseType': 'PublishToMarketplace',
                          'packName': 'kernel',
                          'version': '0.4.0-dev',
                          'code': '',
                          'ownerId': 'self-dev',
                          'isPrivate': false,
                          'commissionRate': 0.0,
                          'description': 'New kernel version via self-dev',
                        });
                        _dispatch('action', {
                          'action': 'install',
                          'packName': 'kernel',
                          'version': '0.4.0-dev',
                        });
                      },
                      child: const Text('Publish+Install New Kernel'),
                    ),
                  ],
                ),
              ],
            ),
          ),

          const SizedBox(height: 12),
          Expanded(
            child: SingleChildScrollView(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  if (q.isEmpty) ...[
                    const Text(
                      'Recommended for you',
                      style: TextStyle(
                        fontSize: 16,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                    const SizedBox(height: 8),
                    SizedBox(height: 220, child: _buildPackGrid(recs)),
                    const SizedBox(height: 16),
                    const Text(
                      'Popular / All',
                      style: TextStyle(
                        fontSize: 16,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                    const SizedBox(height: 8),
                    SizedBox(height: 320, child: _buildPackGrid(filtered)),
                  ] else ...[
                    Text(
                      'Results (${filtered.length})',
                      style: const TextStyle(
                        fontSize: 16,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                    const SizedBox(height: 8),
                    SizedBox(height: 420, child: _buildPackGrid(filtered)),
                    if (recs.isNotEmpty) ...[
                      const SizedBox(height: 12),
                      const Text(
                        'Related recommendations',
                        style: TextStyle(
                          fontSize: 14,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                      const SizedBox(height: 8),
                      SizedBox(
                        height: 140,
                        child: _buildPackGrid(recs, compact: true),
                      ),
                    ],
                  ],
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildPackGrid(List<Pack> items, {bool compact = false}) {
    if (items.isEmpty) {
      return Center(
        child: Text(
          'No matches',
          style: TextStyle(color: FTheme.of(context).colors.mutedForeground),
        ),
      );
    }
    return LayoutBuilder(
      builder: (context, constraints) {
        final cols = compact
            ? 3
            : constraints.maxWidth > 900
            ? 3
            : constraints.maxWidth > 600
            ? 2
            : 1;
        return GridView.builder(
          gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(
            crossAxisCount: cols,
            crossAxisSpacing: 12,
            mainAxisSpacing: 12,
            childAspectRatio: compact ? 1.6 : 1.1,
          ),
          itemCount: items.length,
          itemBuilder: (context, index) {
            final p = items[index];
            return FTappable(
              onPress: () => _showDetails(p),
              child: FCard(
                title: Text(
                  p.name,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
                subtitle: Text(
                  '${p.version} • ${p.owner}',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(
                      child: Text(
                        p.description,
                        maxLines: compact ? 2 : 3,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(fontSize: 13),
                      ),
                    ),
                    const SizedBox(height: 8),
                    Wrap(
                      spacing: 4,
                      runSpacing: 2,
                      children: p.tags
                          .take(3)
                          .map((t) => FBadge(child: Text(t)))
                          .toList(),
                    ),
                    const SizedBox(height: 8),
                    Align(
                      alignment: Alignment.centerRight,
                      child: p.installed
                          ? FBadge(child: const Text('Installed'))
                          : FButton(
                              onPress: () => _toggleInstall(p),
                              child: const Text('Install'),
                            ),
                    ),
                  ],
                ),
              ),
            );
          },
        );
      },
    );
  }
}
