import 'package:flutter/material.dart';

import '../../models/kit_part.dart';
import '../../theme/kit_theme.dart';

/// Product button control. Used on surfaces and inside chat CustomMessage rows.
final class KitButton extends StatelessWidget {
  const KitButton({
    super.key,
    required this.part,
    this.onPressed,
    this.dense = false,
  });

  final KitButtonPart part;
  final ValueChanged<KitButtonPart>? onPressed;
  final bool dense;

  @override
  Widget build(BuildContext context) {
    return Align(
      alignment: Alignment.centerLeft,
      child: FilledButton(
        key: Key('kit_button_${part.buttonId}'),
        style: FilledButton.styleFrom(
          backgroundColor: KitPalette.signal,
          foregroundColor: KitPalette.surface,
          padding: EdgeInsets.symmetric(
            horizontal: dense ? 14 : 18,
            vertical: dense ? 10 : 12,
          ),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(10),
          ),
        ),
        onPressed: onPressed == null ? null : () => onPressed!(part),
        child: Text(
          part.label,
          style: KitType.metaStrong.copyWith(color: KitPalette.surface),
        ),
      ),
    );
  }
}
