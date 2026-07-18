import 'package:flutter/widgets.dart';

class UiKitText extends StatelessWidget {
  final String text;

  const UiKitText({required this.text, super.key});

  @override
  Widget build(BuildContext context) => Text(text);
}
