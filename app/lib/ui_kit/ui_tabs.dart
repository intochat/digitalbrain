import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';
import 'package:rfw/rfw.dart' show RemoteEventHandler;

import 'ui_form_scope.dart';
import 'ui_nav_item.dart';

class UiKitTabs extends StatelessWidget {
  const UiKitTabs({
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
    return FTabs(
      onPress: (index) {
        if (index < parsed.length) {
          fireNav(onEvent, pack, experienceId, parsed[index].eventName, scope?.values ?? const {});
        }
      },
      children: [
        for (final item in parsed)
          FTabEntry(
            label: Text(item.label),
            child: const SizedBox.shrink(),
          ),
      ],
    );
  }
}
