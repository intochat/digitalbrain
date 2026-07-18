import 'package:flutter/material.dart';

import 'gateway/brain_gateway.dart';
import 'shell/app_shell.dart';
import 'surface/file_feed_cursor_store.dart';
import 'theme/brain_theme.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  final cursorStore = FileFeedCursorStore(FileFeedCursorStore.defaultPath());
  runApp(WorkspaceApp(cursorStore: cursorStore));
}

class WorkspaceApp extends StatelessWidget {
  const WorkspaceApp({required this.cursorStore, super.key});

  final FileFeedCursorStore cursorStore;

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
        cursorStore: cursorStore,
      ),
    );
  }
}
