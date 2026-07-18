import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';

class UiKitIcon extends StatelessWidget {
  const UiKitIcon({super.key, required this.name});
  final String name;

  static final Map<String, IconData> _icons = {
    'star': FLucideIcons.star,
    'check': FLucideIcons.check,
    'list': FLucideIcons.list,
    'search': FLucideIcons.search,
    'user': FLucideIcons.userRound,
    'settings': FLucideIcons.settings,
    'info': FLucideIcons.info,
    'house': FLucideIcons.house,
  };

  @override
  Widget build(BuildContext context) =>
      Icon(_icons[name] ?? FLucideIcons.circle);
}
