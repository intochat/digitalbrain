import 'package:flutter/material.dart';

import '../../models/kit_part.dart';
import '../../theme/kit_theme.dart';

final class KitCard extends StatelessWidget {
  const KitCard({super.key, required this.part});

  final KitCardPart part;

  @override
  Widget build(BuildContext context) {
    return DecoratedBox(
      key: Key('kit_card_${part.title}'),
      decoration: BoxDecoration(
        color: KitPalette.surfaceRaised,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: KitPalette.line),
      ),
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            if (part.title.isNotEmpty) ...[
              Text(part.title, style: KitType.title),
              const SizedBox(height: 8),
            ],
            if (part.body.isNotEmpty) Text(part.body, style: KitType.body),
            for (final field in part.fields) ...[
              const SizedBox(height: 8),
              Text(field.label, style: KitType.meta),
              Text(field.value, style: KitType.body),
            ],
          ],
        ),
      ),
    );
  }
}
