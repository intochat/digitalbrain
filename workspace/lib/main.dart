import 'package:flutter/material.dart';

import 'gateway/brain_gateway.dart';
import 'shell/app_shell.dart';
import 'theme/brain_theme.dart';

void main() {
  runApp(const WorkspaceApp());
}

class WorkspaceApp extends StatelessWidget {
  const WorkspaceApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      theme: BrainTheme.dark,
      debugShowCheckedModeBanner: false,
      home: AppShell(
        BrainGateway(
          httpBase: 'http://localhost:5320',
          wsBase: 'ws://localhost:5320',
        ),
      ),
    );
  }
}
