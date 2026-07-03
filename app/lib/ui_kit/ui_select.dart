import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'ui_form_scope.dart';

class UiKitSelect extends StatefulWidget {
  const UiKitSelect({super.key, required this.name, required this.options, this.label = ''});
  final String name;
  final List<String> options;
  final String label;

  @override
  State<UiKitSelect> createState() => _UiKitSelectState();
}

class _UiKitSelectState extends State<UiKitSelect> {
  late final FSelectController<String> _ctrl;

  @override
  void initState() {
    super.initState();
    _ctrl = FSelectController();
    _ctrl.addListener(_onValueChanged);
  }

  void _onValueChanged() {
    final v = _ctrl.value;
    if (v != null) UiKitFormScope.of(context)?.set(widget.name, v);
  }

  @override
  void dispose() {
    _ctrl.removeListener(_onValueChanged);
    _ctrl.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => FSelect<String>.rich(
        control: FSelectControl.managed(controller: _ctrl),
        hint: widget.label.isEmpty ? null : widget.label,
        format: (s) => s,
        children: [
          for (final o in widget.options) FSelectItemMixin.item(title: Text(o), value: o),
        ],
      );
}
