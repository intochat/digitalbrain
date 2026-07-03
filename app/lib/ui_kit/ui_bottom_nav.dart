import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'package:rfw/rfw.dart' show RemoteEventHandler;

import 'ui_form_scope.dart';
import 'ui_nav_item.dart';

class UiKitBottomNav extends StatelessWidget {
  const UiKitBottomNav({
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
    return FBottomNavigationBar(
      onChange: (index) {
        if (index < parsed.length) {
          fireNav(onEvent, pack, experienceId, parsed[index].eventName, scope?.values ?? const {});
        }
      },
      children: [
        for (final item in parsed)
          FBottomNavigationBarItem(
            icon: const Icon(Icons.circle_outlined),
            label: Text(item.label),
          ),
      ],
    );
  }
}
