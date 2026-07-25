import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

final class ShellSurfaceApp extends StatelessWidget {
  const ShellSurfaceApp({
    super.key,
    required this.controller,
    this.events,
    this.shellName = 'desk',
    this.statusMessage,
  });

  final ShellSurfaceController controller;
  final Stream<SceneOpenedEvent>? events;
  final String shellName;
  final String? statusMessage;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'DigitalBrain',
      home: ShellSurfaceHome(
        controller: controller,
        events: events,
        shellName: shellName,
        statusMessage: statusMessage,
      ),
    );
  }
}

final class ShellSurfaceHome extends StatefulWidget {
  const ShellSurfaceHome({
    super.key,
    required this.controller,
    this.events,
    this.shellName = 'desk',
    this.statusMessage,
  });

  final ShellSurfaceController controller;
  final Stream<SceneOpenedEvent>? events;
  final String shellName;
  final String? statusMessage;

  @override
  State<ShellSurfaceHome> createState() => _ShellSurfaceHomeState();
}

final class _ShellSurfaceHomeState extends State<ShellSurfaceHome> {
  StreamSubscription<SceneOpenedEvent>? _subscription;

  @override
  void initState() {
    super.initState();
    _listen(widget.events);
  }

  @override
  void didUpdateWidget(covariant ShellSurfaceHome oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!identical(oldWidget.events, widget.events)) {
      _subscription?.cancel();
      _listen(widget.events);
    }
  }

  void _listen(Stream<SceneOpenedEvent>? events) {
    _subscription = events?.listen((event) {
      if (!mounted) {
        return;
      }
      setState(() {
        widget.controller.apply(event);
      });
    });
  }

  @override
  void dispose() {
    _subscription?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final scenes = widget.controller.scenes;
    final status = widget.statusMessage;

    return Scaffold(
      appBar: AppBar(
        title: Text('shell:${widget.shellName}'),
      ),
      body: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (status != null && status.isNotEmpty)
            MaterialBanner(
              content: Text(status),
              actions: const [SizedBox.shrink()],
            ),
          Expanded(
            child: scenes.isEmpty
                ? const Center(child: Text('No scenes open'))
                : ListView.builder(
                    key: const Key('shell_scene_list'),
                    itemCount: scenes.length,
                    itemBuilder: (context, index) {
                      final scene = scenes[index];
                      return ListTile(
                        key: Key('scene_${scene.sceneKey}'),
                        title: Text(scene.sceneKey),
                        subtitle: Text(scene.title),
                        trailing: Text('seq ${scene.sequence}'),
                      );
                    },
                  ),
          ),
        ],
      ),
    );
  }
}
