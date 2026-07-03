import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';

class UiKitHeader extends StatelessWidget {
  const UiKitHeader({super.key, required this.title});
  final String title;

  @override
  Widget build(BuildContext context) => FHeader(title: Text(title));
}
