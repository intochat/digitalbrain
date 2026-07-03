import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'ui_overlay_host.dart';

class UiKitDialog extends StatefulWidget {
  const UiKitDialog({super.key, required this.open, required this.title, required this.children});
  final bool open;
  final String title;
  final List<Widget> children;

  @override
  State<UiKitDialog> createState() => _UiKitDialogState();
}

class _UiKitDialogState extends State<UiKitDialog> with PresentOnce {
  @override
  Widget build(BuildContext context) {
    presentOnce(widget.open, () {
      showFDialog<void>(
        context: context,
        builder: (context, style, animation) => FDialog(
          title: Text(widget.title),
          body: Column(mainAxisSize: MainAxisSize.min, children: widget.children),
          actions: const [],
        ),
      );
    });
    return const SizedBox.shrink();
  }
}
