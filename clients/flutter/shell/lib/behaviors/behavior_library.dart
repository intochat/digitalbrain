import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import '../brain_theme.dart';

final class BehaviorLibraryView extends StatelessWidget {
  const BehaviorLibraryView({
    super.key,
    required this.items,
    required this.loading,
    this.error,
    this.onRefresh,
    this.onOpen,
  });

  final List<BehaviorLibraryItem> items;
  final bool loading;
  final String? error;
  final VoidCallback? onRefresh;
  final ValueChanged<String>? onOpen;

  @override
  Widget build(BuildContext context) {
    return ColoredBox(
      key: const Key('behavior_library'),
      color: BrainPalette.surface,
      child: Align(
        alignment: Alignment.topCenter,
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 1080),
          child: ListView(
            padding: const EdgeInsets.symmetric(horizontal: 32, vertical: 28),
            children: [
              Row(
                children: [
                  const Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text('Behaviors', style: BrainType.heading),
                        SizedBox(height: 8),
                        Text(
                          'Running, draft, and stopped behaviors with purpose and health.',
                          style: BrainType.bodyMuted,
                        ),
                      ],
                    ),
                  ),
                  if (onRefresh != null)
                    TextButton.icon(
                      key: const Key('behavior_library_refresh'),
                      onPressed: loading ? null : onRefresh,
                      icon: const Icon(Icons.refresh),
                      label: const Text('Refresh'),
                    ),
                ],
              ),
              const SizedBox(height: 20),
              if (loading)
                const Padding(
                  padding: EdgeInsets.only(top: 48),
                  child: Center(child: CircularProgressIndicator()),
                )
              else if (error != null)
                _MessageCard(text: error!, tone: BrainPalette.signal)
              else if (items.isEmpty)
                const _MessageCard(
                  text: 'No behaviors yet. Seeded and published behaviors appear here.',
                  tone: BrainPalette.textMuted,
                )
              else
                for (final item in items)
                  _LibraryCard(item: item, onOpen: onOpen),
            ],
          ),
        ),
      ),
    );
  }
}

final class _LibraryCard extends StatelessWidget {
  const _LibraryCard({required this.item, this.onOpen});

  final BehaviorLibraryItem item;
  final ValueChanged<String>? onOpen;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: Material(
        color: BrainPalette.surfaceRaised,
        borderRadius: BorderRadius.circular(14),
        child: InkWell(
          key: Key('behavior_card_${item.behaviorId}'),
          borderRadius: BorderRadius.circular(14),
          onTap: onOpen == null ? null : () => onOpen!(item.behaviorId),
          child: Padding(
            padding: const EdgeInsets.all(18),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(
                      child: Text(item.displayName, style: BrainType.cardTitle),
                    ),
                    _Pill(label: item.health),
                    const SizedBox(width: 8),
                    _Pill(label: item.runState),
                  ],
                ),
                const SizedBox(height: 8),
                Text(item.description, style: BrainType.bodyMuted),
                if (item.overview.isNotEmpty) ...[
                  const SizedBox(height: 10),
                  Text(item.overview, style: BrainType.body),
                ],
                if (item.scenarioTitles.isNotEmpty) ...[
                  const SizedBox(height: 12),
                  Wrap(
                    spacing: 8,
                    runSpacing: 8,
                    children: [
                      for (final title in item.scenarioTitles)
                        Chip(
                          label: Text(title, style: BrainType.meta),
                          backgroundColor: BrainPalette.surfaceSunken,
                          side: const BorderSide(color: BrainPalette.line),
                        ),
                    ],
                  ),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }
}

final class _Pill extends StatelessWidget {
  const _Pill({required this.label});

  final String label;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
      decoration: BoxDecoration(
        color: BrainPalette.surfaceSunken,
        borderRadius: BorderRadius.circular(999),
        border: Border.all(color: BrainPalette.line),
      ),
      child: Text(label, style: BrainType.metaStrong),
    );
  }
}

final class _MessageCard extends StatelessWidget {
  const _MessageCard({required this.text, required this.tone});

  final String text;
  final Color tone;

  @override
  Widget build(BuildContext context) {
    return Container(
      margin: const EdgeInsets.only(top: 24),
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(
        color: BrainPalette.surfaceRaised,
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: BrainPalette.line),
      ),
      child: Text(text, style: BrainType.body.copyWith(color: tone)),
    );
  }
}
