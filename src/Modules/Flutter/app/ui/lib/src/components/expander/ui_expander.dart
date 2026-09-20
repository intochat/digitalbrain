import 'package:flutter/material.dart';

import '../../models/ui_part.dart';
import '../../theme/ui_theme.dart';

final class UiExpander extends StatelessWidget {
  const UiExpander({super.key, required this.part, this.onToggle});

  final UiExpanderPart part;
  final VoidCallback? onToggle;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final status = part.expanded ? 'Expanded' : 'Collapsed';
    return Semantics(
      button: true,
      label: '$status ${part.header}',
      child: Material(
        key: Key('ui_expander_${part.name}'),
        color: scheme.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(12),
        child: InkWell(
          onTap: onToggle,
          borderRadius: BorderRadius.circular(12),
          child: Padding(
            padding: const EdgeInsets.all(12),
            child: Row(
              children: [
                Icon(part.expanded ? Icons.expand_less : Icons.expand_more),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    part.header,
                    style: TextStyle(
                      fontFamily: UiType.bodyFamily,
                      fontWeight: FontWeight.w600,
                      color: scheme.onSurface,
                    ),
                  ),
                ),
                Text(
                  status,
                  style: TextStyle(
                    fontFamily: UiType.bodyFamily,
                    color: scheme.onSurfaceVariant,
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
