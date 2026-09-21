import 'package:flutter/material.dart';

import '../../models/ui_part.dart';
import '../../composition/neuron_view.dart';
import '../../theme/ui_theme.dart';

final class UiCard extends StatelessWidget {
  const UiCard({super.key, required this.part, this.loadChild});

  final UiCardPart part;
  final NeuronLoader? loadChild;

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
            for (final child in part.children)
              SizedBox(
                height: 200,
                child: loadChild == null
                    ? const Text('This component is not connected.')
                    : NeuronView(
                        kind: child.kind,
                        name: child.name,
                        load: loadChild!,
                      ),
              ),
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
