import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';

class UiKitIcon extends StatelessWidget {
  const UiKitIcon({super.key, required this.name});
  final String name;

  // Confirmed const members of FIcons in forui_assets 0.21.0.
  // Unknown names fall back to circle — never throws.
  static const Map<String, IconData> _icons = {
    'star': FIcons.star,
    'check': FIcons.check,
    'list': FIcons.list,
    'search': FIcons.search,
    'user': FIcons.userRound,
    'settings': FIcons.settings,
    'info': FIcons.info,
    'house': FIcons.house,
  };

  @override
  Widget build(BuildContext context) =>
      Icon(_icons[name] ?? FIcons.circle);
}
