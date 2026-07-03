import 'package:flutter/widgets.dart';

class UiKitColumn extends StatelessWidget {
  const UiKitColumn({super.key, required this.children});
  final List<Widget> children;

  @override
  Widget build(BuildContext context) => Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          for (final c in children) Padding(padding: const EdgeInsets.symmetric(vertical: 4), child: c),
        ],
      );
}
