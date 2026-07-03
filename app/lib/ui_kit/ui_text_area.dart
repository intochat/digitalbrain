import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'ui_form_scope.dart';

class UiKitTextArea extends StatefulWidget {
  const UiKitTextArea({super.key, required this.name, this.placeholder = ''});
  final String name;
  final String placeholder;

  @override
  State<UiKitTextArea> createState() => _UiKitTextAreaState();
}

class _UiKitTextAreaState extends State<UiKitTextArea> {
  TextEditingValue _value = TextEditingValue.empty;

  void _onChange(TextEditingValue v) {
    setState(() => _value = v);
    UiKitFormScope.of(context)?.set(widget.name, v.text);
  }

  @override
  Widget build(BuildContext context) => FTextField.multiline(
        control: FTextFieldControl.lifted(value: _value, onChange: _onChange),
        hint: widget.placeholder,
        minLines: 3,
      );
}
