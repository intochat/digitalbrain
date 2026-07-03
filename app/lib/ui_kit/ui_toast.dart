import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'ui_overlay_host.dart';

class UiKitToast extends StatefulWidget {
  const UiKitToast({super.key, required this.message});
  final String message;

  @override
  State<UiKitToast> createState() => _UiKitToastState();
}

class _UiKitToastState extends State<UiKitToast> with PresentOnce {
  @override
  Widget build(BuildContext context) {
    presentOnce(true, () => showFToast(context: context, title: Text(widget.message)));
    return const SizedBox.shrink();
  }
}
