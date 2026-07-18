import 'package:flutter/material.dart';
import 'package:flutter/services.dart' show rootBundle;

import 'package:digitalbrain_flutter/theme/digitalbrain_theme.dart';
import 'event_table.dart';
import 'layout_contract.dart';
import 'rfw_runtime_host.dart';

/// Renders one RFW document (loaded from a bundled asset for Slice 1A) inside
/// the layout contract, routing its events through [EventTable].
class RfwSurface extends StatefulWidget {
  const RfwSurface({
    super.key,
    required this.host,
    required this.docKey,
    required this.assetPath,
    required this.data,
    required this.events,
    this.rootWidget = 'root',
    this.width = 420,
  });

  final RfwRuntimeHost host;
  final String docKey;
  final String assetPath;
  final Map<String, Object?> data;
  final EventTable events;
  final String rootWidget;
  final double width;

  @override
  State<RfwSurface> createState() => _RfwSurfaceState();
}

class _RfwSurfaceState extends State<RfwSurface> {
  String? _source;

  @override
  void initState() {
    super.initState();
    rootBundle.loadString(widget.assetPath).then((s) {
      widget.host.ensureLoaded(widget.docKey, s);
      if (mounted) setState(() => _source = s);
    });
  }

  @override
  void didUpdateWidget(RfwSurface oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.assetPath != widget.assetPath || oldWidget.docKey != widget.docKey) {
      setState(() => _source = null);
      rootBundle.loadString(widget.assetPath).then((s) {
        widget.host.ensureLoaded(widget.docKey, s);
        if (mounted) setState(() => _source = s);
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_source == null) {
      return const SizedBox(
        width: 420,
        height: 80,
        child: Center(child: CircularProgressIndicator()),
      );
    }
    final err = widget.host.parseError(widget.docKey);
    if (err != null) {
      return _error('RFW parse error', err);
    }
    return rfwBoundedFrame(
      width: widget.width,
      child: widget.host.render(
        widget.docKey,
        data: widget.data,
        onEvent: (name, args) => widget.events.dispatch(name, args),
        rootWidget: widget.rootWidget,
      ),
    );
  }

  Widget _error(String title, String msg) => Container(
    width: widget.width,
    padding: const EdgeInsets.all(16),
    decoration: BoxDecoration(
      color: DigitalBrainColors.rose.withValues(alpha: 0.06),
      borderRadius: BorderRadius.circular(14),
      border: Border.all(color: DigitalBrainColors.rose.withValues(alpha: 0.4)),
    ),
    child: Text(
      '$title\n$msg',
      style: const TextStyle(
        color: DigitalBrainColors.inkMid,
        fontFamily: 'monospace',
        fontSize: 12,
      ),
    ),
  );
}
