import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';

import 'rive_design_registry.dart';
import 'rive_handles.dart';

class RiveArtboard extends StatefulWidget {
  const RiveArtboard({
    super.key,
    required this.registry,
    required this.domain,
    required this.artboard,
    required this.bindings,
    required this.triggers,
    this.animSpecs = const <String, AnimSpec?>{},
  });

  final RiveDesignRegistry registry;
  final String domain;
  final String artboard;
  final Map<String, Object?> bindings;
  final Map<String, VoidCallback?> triggers;
  final Map<String, AnimSpec?> animSpecs;

  @override
  State<RiveArtboard> createState() => _RiveArtboardState();
}

class _RiveArtboardState extends State<RiveArtboard> {
  RiveResolution? _resolution;

  @override
  void initState() {
    super.initState();
    _resolve();
  }

  @override
  void didUpdateWidget(covariant RiveArtboard oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!mapEquals(oldWidget.bindings, widget.bindings) ||
        !mapEquals(oldWidget.animSpecs, widget.animSpecs)) {
      _applyBindings();
    }
  }

  Future<void> _resolve() async {
    final res = await widget.registry.resolveController(
      domain: widget.domain,
      artboard: widget.artboard,
    );
    if (!mounted) {
      res.dispose();
      return;
    }
    setState(() => _resolution = res);
    _applyBindings();
    _wireTriggers();
  }

  void _wireTriggers() {
    final vm = _resolution?.viewModel;
    if (vm == null) return;
    widget.triggers.forEach((name, cb) {
      if (cb == null) return;
      vm.onTrigger(name, cb);
    });
  }

  void _applyBindings() {
    final vm = _resolution?.viewModel;
    if (vm == null) return;
    widget.bindings.forEach((name, value) {
      if (value == null) return;
      final anim = widget.animSpecs[name];
      switch (value) {
        case String s:
          vm.writeString(name, s);
        case num n:
          vm.writeNumber(name, n.toDouble(), anim: anim);
        case Color c:
          vm.writeColor(name, c, anim: anim);
      }
    });
  }

  @override
  void dispose() {
    _resolution?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) =>
      _resolution?.buildWidget() ?? const SizedBox.shrink();
}
