import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';
import 'package:rfw/rfw.dart' show RemoteEventHandler;

import 'ui_form_scope.dart';
import 'ui_nav_item.dart';

class UiKitBreadcrumb extends StatelessWidget {
  const UiKitBreadcrumb({
    super.key,
    required this.items,
    required this.pack,
    required this.experienceId,
    required this.onEvent,
  });

  final List items;
  final String pack;
  final String experienceId;
  final RemoteEventHandler onEvent;

  @override
  Widget build(BuildContext context) {
    final parsed = parseNavItems(items);
    final scope = UiKitFormScope.of(context);
    return FBreadcrumb(
      children: [
        for (final item in parsed)
          FBreadcrumbItem(
            onPress: () => fireNav(onEvent, pack, experienceId, item.eventName, scope?.values ?? const {}),
            child: Text(item.label),
          ),
      ],
    );
  }
}
