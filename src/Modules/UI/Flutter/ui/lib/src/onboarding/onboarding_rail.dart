import 'package:flutter/material.dart';

import '../theme/ui_theme.dart';
import 'onboarding_catalog.dart';
import 'onboarding_models.dart';

final class OnboardingCapabilityRail extends StatelessWidget {
  const OnboardingCapabilityRail({
    super.key,
    required this.selectedId,
    required this.onSelected,
    this.capabilities,
  });

  final String selectedId;
  final ValueChanged<String> onSelected;
  final List<OnboardingCapability>? capabilities;

  @override
  Widget build(BuildContext context) {
    final items = capabilities ?? OnboardingCatalog.capabilities;
    return SizedBox(
      key: const Key('onboarding_capability_rail'),
      height: 108,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
        itemCount: items.length,
        separatorBuilder: (_, _) => const SizedBox(width: 8),
        itemBuilder: (context, index) {
          final item = items[index];
          final selected = item.id == selectedId;
          return _CapabilityCard(
            capability: item,
            selected: selected,
            onTap: () => onSelected(item.id),
          );
        },
      ),
    );
  }
}

final class _CapabilityCard extends StatelessWidget {
  const _CapabilityCard({
    required this.capability,
    required this.selected,
    required this.onTap,
  });

  final OnboardingCapability capability;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Material(
      color: selected
          ? UiPalette.signal.withValues(alpha: 0.14)
          : UiPalette.surfaceRaised,
      borderRadius: BorderRadius.circular(12),
      child: InkWell(
        key: Key('onboarding_capability_${capability.id}'),
        onTap: onTap,
        borderRadius: BorderRadius.circular(12),
        child: Container(
          width: 148,
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(12),
            border: Border.all(
              color: selected ? UiPalette.signal : UiPalette.line,
            ),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Icon(
                capability.icon,
                size: 18,
                color: selected ? UiPalette.signal : UiPalette.textMuted,
              ),
              const SizedBox(height: 8),
              Text(capability.title, style: UiType.metaStrong),
              Text(
                capability.blurb,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: UiType.meta,
              ),
            ],
          ),
        ),
      ),
    );
  }
}
