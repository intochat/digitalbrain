import 'package:flutter/widgets.dart';

class UiKitGap extends StatelessWidget {
  const UiKitGap({super.key, this.size = 16});
  final double size;

  @override
  Widget build(BuildContext context) => SizedBox(height: size, width: size);
}
