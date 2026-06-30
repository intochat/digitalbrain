import 'package:flutter/material.dart';
import 'package:forui/forui.dart';

import 'ui_form_scope.dart';

class UiKitTextField extends StatefulWidget {
  const UiKitTextField({
    super.key,
    required this.name,
    this.placeholder = '',
    this.secret = false,
  });
  final String name;
  final String placeholder;
  final bool secret;

  @override
  State<UiKitTextField> createState() => _UiKitTextFieldState();
}

class _UiKitTextFieldState extends State<UiKitTextField> {
  TextEditingValue _value = TextEditingValue.empty;

  void _onChange(TextEditingValue v) {
    setState(() => _value = v);
    UiKitFormScope.of(context)?.set(widget.name, v.text);
  }

  @override
  Widget build(BuildContext context) {
    return FTextField(
      control: FTextFieldControl.lifted(value: _value, onChange: _onChange),
      hint: widget.placeholder,
      obscureText: widget.secret,
    );
  }
}
