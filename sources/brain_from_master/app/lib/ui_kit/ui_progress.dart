import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';

class UiKitProgress extends StatelessWidget {
  const UiKitProgress({super.key, required this.value});
  final double value;

  @override
  Widget build(BuildContext context) => FDeterminateProgress(value: value);
}
