import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';

class UiKitHeading extends StatelessWidget {
  const UiKitHeading({super.key, required this.text});
  final String text;

  @override
  Widget build(BuildContext context) =>
      Text(text, style: context.theme.typography.xl2.copyWith(fontWeight: FontWeight.bold));
}
