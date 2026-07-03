import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';

class UiKitBadge extends StatelessWidget {
  const UiKitBadge({super.key, required this.text});
  final String text;

  @override
  Widget build(BuildContext context) => FBadge(child: Text(text));
}
