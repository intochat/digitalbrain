import 'package:flutter/material.dart';
import 'package:forui/forui.dart';

import '../../models/ui_part.dart';
import '../../theme/ui_theme.dart';

/// Product button control. Used on surfaces and inside chat CustomMessage rows.
final class UiButton extends StatelessWidget {
  const UiButton({
    super.key,
    required this.part,
    this.onPressed,
    this.dense = false,
  });

  final UiButtonPart part;
  final ValueChanged<UiButtonPart>? onPressed;
  final bool dense;

  @override
  Widget build(BuildContext context) {
    return Align(
      alignment: Alignment.centerLeft,
      child: UiThemeScope(
        brightness: Theme.of(context).brightness,
        child: FButton(
          key: Key('ui_button_${part.buttonId}'),
          size: dense ? FButtonSizeVariant.sm : FButtonSizeVariant.md,
          mainAxisSize: MainAxisSize.min,
          onPress: onPressed == null ? null : () => onPressed!(part),
          child: Text(part.label),
        ),
      ),
    );
  }
}
