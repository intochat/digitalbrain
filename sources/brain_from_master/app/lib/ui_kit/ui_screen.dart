import 'package:flutter/widgets.dart';

import 'ui_form_scope.dart';
import 'ui_sidebar.dart';

class UiKitScreen extends StatefulWidget {
  const UiKitScreen({super.key, required this.children});
  final List<Widget> children;

  @override
  State<UiKitScreen> createState() => _UiKitScreenState();
}

class _UiKitScreenState extends State<UiKitScreen> {
  final UiKitFormController _controller = UiKitFormController();

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    Widget? sidebar;
    final flow = <Widget>[];
    for (final child in widget.children) {
      if (child is UiKitSidebar && sidebar == null) {
        sidebar = child;
      } else {
        flow.add(child);
      }
    }

    final content = SingleChildScrollView(
      padding: const EdgeInsets.all(24),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          for (final child in flow)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 8),
              child: child,
            ),
        ],
      ),
    );

    final body = sidebar == null
        ? content
        : Row(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              SizedBox(width: 240, child: sidebar),
              Expanded(child: content),
            ],
          );

    return UiKitFormScope(controller: _controller, child: body);
  }
}
