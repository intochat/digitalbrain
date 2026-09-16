import 'package:flutter/material.dart';

import '../../models/ui_part.dart';
import '../../theme/ui_theme.dart';

final class UiCard extends StatelessWidget {
  const UiCard({super.key, required this.part});

  final UiCardPart part;

  @override
  Widget build(BuildContext context) {
    return DecoratedBox(
      key: Key('ui_card_${part.title}'),
      decoration: BoxDecoration(
        color: UiPalette.surfaceRaised,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: UiPalette.line),
      ),
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            if (part.title.isNotEmpty) ...[
              Text(part.title, style: UiType.title),
              const SizedBox(height: 8),
            ],
            if (part.body.isNotEmpty) Text(part.body, style: UiType.body),
            for (final field in part.fields) ...[
              const SizedBox(height: 8),
              Text(field.label, style: UiType.meta),
              Text(field.value, style: UiType.body),
            ],
          ],
        ),
      ),
    );
  }
}
