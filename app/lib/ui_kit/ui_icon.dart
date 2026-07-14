import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';

class UiKitIcon extends StatelessWidget {
  const UiKitIcon({super.key, required this.name});
  final String name;

  static final Map<String, IconData> _icons = {
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
  Widget build(BuildContext context) => Icon(_icons[name] ?? FIcons.circle);
}
