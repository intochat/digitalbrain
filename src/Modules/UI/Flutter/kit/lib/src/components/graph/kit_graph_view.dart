import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter/scheduler.dart';

import 'graph_scene.dart';
import 'kit_graph_controller.dart';
import 'three_graph_scene.dart';

/// Interactive 3D graph. Drag to orbit, scroll to zoom, tap a node to focus it.
///
/// All navigation state lives in [controller]; this widget only renders and
/// reports picks. Pair it with `KitGraphNavigator` for visible navigation.
final class KitGraphView extends StatefulWidget {
  const KitGraphView({
    super.key,
    required this.controller,
    this.sceneFactory,
    this.semanticsLabel =
        'Interactive three-dimensional graph. Drag to orbit; tap a node to focus it.',
  });

  final KitGraphController controller;

  /// Substitutes the renderer. Tests pass a fake so no GL context is needed.
  final GraphSceneFactory? sceneFactory;

  final String semanticsLabel;

  @override
  State<KitGraphView> createState() => _KitGraphViewState();
}

class _KitGraphViewState extends State<KitGraphView>
    with SingleTickerProviderStateMixin {
  late final GraphScene _scene = (widget.sceneFactory ?? ThreeGraphScene.new)();

  /// Held as one instance: each `_scene.project` tear-off is a distinct
  /// closure, so comparing them with `identical` on dispose would never match.
  late final Offset? Function(String) _project = _scene.project;

  late final Ticker _ticker;
  Duration _last = Duration.zero;

  @override
  void initState() {
    super.initState();
    widget.controller.projector = _project;
    _scene.load(
      widget.controller.nodes,
      widget.controller.edges,
      widget.controller.layout,
    );
    _ticker = createTicker(_onFrame)..start();
  }

  void _onFrame(Duration elapsed) {
    final dt = (elapsed - _last).inMicroseconds / 1e6;
    _last = elapsed;
    if (dt <= 0) return;
    widget.controller.camera.tick(dt);
    _scene.applyCamera(widget.controller.camera.current);
  }

  @override
  void dispose() {
    _ticker.dispose();
    if (identical(widget.controller.projector, _project)) {
      widget.controller.projector = null;
    }
    _scene.dispose();
    super.dispose();
  }

  void _onTapUp(TapUpDetails details) {
    final hit = _scene.pick(details.localPosition);
    if (hit != null) {
      widget.controller.focus(hit);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Semantics(
      label: widget.semanticsLabel,
      image: true,
      child: Listener(
        onPointerSignal: (signal) {
          if (signal is PointerScrollEvent) {
            widget.controller.camera.zoomBy(
              signal.scrollDelta.dy > 0 ? 0.92 : 1.09,
            );
          }
        },
        child: GestureDetector(
          key: const Key('kit_graph_view'),
          behavior: HitTestBehavior.opaque,
          onTapUp: _onTapUp,
          onPanUpdate: (d) =>
              widget.controller.camera.orbitBy(d.delta.dx, d.delta.dy),
          child: _scene.build(context),
        ),
      ),
    );
  }
}
