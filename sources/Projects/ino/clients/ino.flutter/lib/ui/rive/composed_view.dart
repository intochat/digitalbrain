import 'package:flutter/material.dart';
import 'package:rfw/formats.dart' show parseLibraryFile;
import 'package:rfw/rfw.dart';

import '../ino_runtime.dart';
import 'rive_design_registry.dart';

class ComposedView extends StatefulWidget {
  const ComposedView({
    super.key,
    required this.registry,
    required this.rfwSource,
    required this.data,
  });

  factory ComposedView.sample({required RiveDesignRegistry registry}) {
    return ComposedView(
      registry: registry,
      rfwSource: '''
import ino.rive;
import core.widgets;
widget root = Hero(domain: "kernel", title: data.title);
''',
      data: const {'title': 'Tokyo'},
    );
  }

  final RiveDesignRegistry registry;
  final String rfwSource;
  final Map<String, Object> data;

  @override
  State<ComposedView> createState() => _ComposedViewState();
}

class _ComposedViewState extends State<ComposedView> {
  late final InoRuntime _ino;
  late final DynamicContent _data;
  static const LibraryName _composedLib = LibraryName(<String>['ino', 'composed']);

  @override
  void initState() {
    super.initState();
    _ino = createInoRuntime(riveRegistry: widget.registry);
    _data = DynamicContent(widget.data);
    _ino.runtime.update(_composedLib, parseLibraryFile(widget.rfwSource));
  }

  @override
  Widget build(BuildContext context) {
    return RemoteWidget(
      runtime: _ino.runtime,
      data: _data,
      widget: const FullyQualifiedWidgetName(_composedLib, 'root'),
    );
  }
}
