import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'ui_form_scope.dart';

class UiKitSwitch extends StatefulWidget {
  const UiKitSwitch({super.key, required this.name, required this.label});
  final String name;
  final String label;

  @override
  State<UiKitSwitch> createState() => _UiKitSwitchState();
}

class _UiKitSwitchState extends State<UiKitSwitch> {
  bool _value = false;

  void _onChange(bool v) {
    setState(() => _value = v);
    UiKitFormScope.of(context)?.set(widget.name, v.toString());
  }

  @override
  Widget build(BuildContext context) =>
      FSwitch(value: _value, onChange: _onChange, label: Text(widget.label));
}
