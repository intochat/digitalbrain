import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'ui_form_scope.dart';

class UiKitCheckbox extends StatefulWidget {
  const UiKitCheckbox({super.key, required this.name, required this.label});
  final String name;
  final String label;

  @override
  State<UiKitCheckbox> createState() => _UiKitCheckboxState();
}

class _UiKitCheckboxState extends State<UiKitCheckbox> {
  bool _value = false;

  void _onChange(bool v) {
    setState(() => _value = v);
    UiKitFormScope.of(context)?.set(widget.name, v.toString());
  }

  @override
  Widget build(BuildContext context) =>
      FCheckbox(value: _value, onChange: _onChange, label: Text(widget.label));
}
