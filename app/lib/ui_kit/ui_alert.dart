import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';

class UiKitAlert extends StatelessWidget {
  const UiKitAlert({super.key, required this.title, this.subtitle = ''});
  final String title;
  final String subtitle;

  @override
  Widget build(BuildContext context) =>
      FAlert(title: Text(title), subtitle: subtitle.isEmpty ? null : Text(subtitle));
}
