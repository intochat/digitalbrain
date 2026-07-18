import 'package:flutter/material.dart';

import '../surface/feed_cursor_store.dart';
import '../surface/ui_surface_client.dart';
import '../surface/ui_surface_controller.dart';
import '../surface/ui_surface_renderer.dart';

class AppShell extends StatefulWidget {
  const AppShell(
    this.client, {
    super.key,
    this.cursorStore,
    this.controller,
  });

  final UiSurfaceClient client;
  final FeedCursorStore? cursorStore;
  final UiSurfaceController? controller;

  @override
  State<AppShell> createState() => _AppShellState();
}

class _AppShellState extends State<AppShell> {
  late final UiSurfaceController _controller;
  late final bool _ownsController;

  @override
  void initState() {
    super.initState();
    final provided = widget.controller;
    if (provided != null) {
      _controller = provided;
      _ownsController = false;
    } else {
      _controller = UiSurfaceController(
        client: widget.client,
        cursorStore: widget.cursorStore ?? MemoryFeedCursorStore(),
      );
      _ownsController = true;
    }
    _controller.start();
  }

  @override
  void dispose() {
    if (_ownsController) {
      _controller.dispose();
    }
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Surfaces')),
      body: UiSurfaceRenderer(controller: _controller),
    );
  }
}
