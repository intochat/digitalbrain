import 'package:flutter/widgets.dart';

class UiKitRow extends StatelessWidget {
  const UiKitRow({super.key, required this.children});
  final List<Widget> children;

  @override
  Widget build(BuildContext context) => Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          for (final c in children) Padding(padding: const EdgeInsets.symmetric(horizontal: 4), child: c),
        ],
      );
}
