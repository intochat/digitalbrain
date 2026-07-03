import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';

class UiKitTooltip extends StatelessWidget {
  const UiKitTooltip({super.key, required this.tip, required this.child});
  final String tip;
  final Widget child;

  @override
  Widget build(BuildContext context) =>
      FTooltip(tipBuilder: (context, _) => Text(tip), child: child);
}
