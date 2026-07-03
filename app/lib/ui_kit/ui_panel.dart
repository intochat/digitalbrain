import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';

class UiKitPanel extends StatelessWidget {
  const UiKitPanel({super.key, required this.children});
  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    return FCard(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: children,
      ),
    );
  }
}
