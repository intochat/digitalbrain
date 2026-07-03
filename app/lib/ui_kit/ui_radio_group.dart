import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'ui_form_scope.dart';

class UiKitRadioGroup extends StatefulWidget {
  const UiKitRadioGroup({super.key, required this.name, required this.options, this.label = ''});
  final String name;
  final List<String> options;
  final String label;

  @override
  State<UiKitRadioGroup> createState() => _UiKitRadioGroupState();
}

class _UiKitRadioGroupState extends State<UiKitRadioGroup> {
  // Tracks the currently selected option; null = nothing selected yet.
  String? _selected;

  void _onSelect(String option, bool selected) {
    if (!selected) return;
    setState(() => _selected = option);
    UiKitFormScope.of(context)?.set(widget.name, option);
  }

  @override
  Widget build(BuildContext context) => Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (widget.label.isNotEmpty) ...[
            Text(widget.label),
            const SizedBox(height: 8),
          ],
          for (final o in widget.options)
            FRadio(
              label: Text(o),
              value: _selected == o,
              onChange: (v) => _onSelect(o, v),
            ),
        ],
      );
}
