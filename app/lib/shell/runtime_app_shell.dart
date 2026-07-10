import 'package:flutter/widgets.dart';

import '../v2/v2_config.dart';
import '../v2/widgets/v2_runtime_shell.dart';
import 'forui_app_shell.dart';

class RuntimeAppShell extends StatelessWidget {
  const RuntimeAppShell({super.key, required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) =>
      isV2Runtime() ? const V2RuntimeShell() : ForuiAppShell(child: child);
}
