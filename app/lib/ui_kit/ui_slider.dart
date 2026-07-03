import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'ui_form_scope.dart';

class UiKitSlider extends StatelessWidget {
  const UiKitSlider({
    super.key,
    required this.name,
    this.min = 0,
    this.max = 1,
    this.label = '',
  });
  final String name;
  final double min;
  final double max;
  final String label;

  @override
  Widget build(BuildContext context) => FSlider(
        label: label.isEmpty ? null : Text(label),
        onEnd: (FSliderValue v) {
          final scaled = min + (max - min) * v.max;
          UiKitFormScope.of(context)?.set(name, scaled.toString());
        },
      );
}
