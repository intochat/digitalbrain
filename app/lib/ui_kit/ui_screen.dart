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
    // A ui:Sidebar is a full-height nav rail — pull it out of the vertical flow so it isn't given
    // unbounded height (which throws and blanks the screen) and lay it beside the scrollable content.
    Widget? sidebar;
    final flow = <Widget>[];
    for (final child in widget.children) {
      if (child is UiKitSidebar && sidebar == null) {
        sidebar = child;
      } else {
        flow.add(child);
      }
    }

    // Scrollable so a hop with many components (e.g. the gallery) doesn't overflow the viewport.
    final content = SingleChildScrollView(
      padding: const EdgeInsets.all(24),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          for (final child in flow)
            Padding(padding: const EdgeInsets.symmetric(vertical: 8), child: child),
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
