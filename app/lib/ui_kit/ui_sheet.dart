import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'ui_overlay_host.dart';

class UiKitSheet extends StatefulWidget {
  const UiKitSheet({super.key, required this.open, required this.title, required this.children});
  final bool open;
  final String title;
  final List<Widget> children;

  @override
  State<UiKitSheet> createState() => _UiKitSheetState();
}

class _UiKitSheetState extends State<UiKitSheet> with PresentOnce {
  @override
  Widget build(BuildContext context) {
    presentOnce(widget.open, () {
      showFSheet<void>(
        context: context,
        side: FLayout.btt,
        builder: (context) => Padding(
          padding: const EdgeInsets.all(16),
          child: Column(mainAxisSize: MainAxisSize.min, children: [Text(widget.title), ...widget.children]),
        ),
      );
    });
    return const SizedBox.shrink();
  }
}
