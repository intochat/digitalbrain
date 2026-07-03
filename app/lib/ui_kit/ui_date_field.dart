import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'ui_form_scope.dart';

class UiKitDateField extends StatefulWidget {
  const UiKitDateField({super.key, required this.name, this.label = ''});
  final String name;
  final String label;

  @override
  State<UiKitDateField> createState() => _UiKitDateFieldState();
}

class _UiKitDateFieldState extends State<UiKitDateField> {
  late final FDateFieldController _ctrl;

  static String _iso(DateTime d) =>
      '${d.year.toString().padLeft(4, '0')}-'
      '${d.month.toString().padLeft(2, '0')}-'
      '${d.day.toString().padLeft(2, '0')}';

  @override
  void initState() {
    super.initState();
    _ctrl = FDateFieldController();
    _ctrl.addListener(_onDateChanged);
  }

  void _onDateChanged() {
    final d = _ctrl.value;
    if (d != null) UiKitFormScope.of(context)?.set(widget.name, _iso(d));
  }

  @override
  void dispose() {
    _ctrl.removeListener(_onDateChanged);
    _ctrl.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => FDateField(
        control: FDateFieldControl.managed(controller: _ctrl),
        label: widget.label.isEmpty ? null : Text(widget.label),
      );
}
