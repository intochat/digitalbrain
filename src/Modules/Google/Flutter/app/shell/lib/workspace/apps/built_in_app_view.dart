import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';

class BuiltInAppView extends StatefulWidget {
  const BuiltInAppView({
    super.key,
    required this.client,
    required this.workspaceId,
    required this.appId,
    required this.onClose,
    this.onPreferences,
    this.onMoreSettings,
  });
  final DigitalBrainUiClient client;
  final String workspaceId, appId;
  final VoidCallback onClose;
  final ValueChanged<Map<String, dynamic>>? onPreferences;
  final VoidCallback? onMoreSettings;
  @override
  State<BuiltInAppView> createState() => _BuiltInAppViewState();
}

class _BuiltInAppViewState extends State<BuiltInAppView> {
  Map<String, dynamic>? _state;
  String? _error;
  bool _busy = false;
  int _generation = 0, _renderRevision = 0;

  @override
  void initState() {
    super.initState();
    _open();
  }

  @override
  void didUpdateWidget(BuiltInAppView oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.workspaceId != widget.workspaceId ||
        oldWidget.appId != widget.appId ||
        oldWidget.client != widget.client) {
      _state = null;
      _open();
    }
  }

  Future<void> _open({Map<String, dynamic>? event}) async {
    final generation = ++_generation;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      if (event != null) {
        await widget.client.appRequest(
          widget.workspaceId,
          'event',
          body: event,
        );
        if (!mounted || generation != _generation) return;
      }
      final result = await widget.client.jsonRequest(
        'POST',
        '/workspaces/${Uri.encodeComponent(widget.workspaceId)}/built-in/${Uri.encodeComponent(widget.appId)}/open',
        <String, dynamic>{},
      );
      if (!mounted || generation != _generation) return;
      final state = Map<String, dynamic>.from(result as Map);
      if (state['surface'] is! Map) {
        throw const FormatException('This app has no surface.');
      }
      setState(() {
        _state = state;
        _renderRevision++;
      });
      if (widget.appId == 'settings' && state['preferences'] is Map) {
        widget.onPreferences?.call(
          Map<String, dynamic>.from(state['preferences'] as Map),
        );
      }
    } catch (error) {
      if (mounted && generation == _generation) {
        setState(() => _error = '$error');
      }
    } finally {
      if (mounted && generation == _generation) setState(() => _busy = false);
    }
  }

  Future<Map<String, dynamic>> _loadNode(String kind, String name) =>
      widget.client.appRequest(
        widget.workspaceId,
        'node?${Uri(queryParameters: {'kind': kind, 'name': name}).query}',
      );

  @override
  Widget build(BuildContext context) {
    final surface = _state?['surface'] as Map?;
    return Scaffold(
      appBar: AppBar(
        title: Text(widget.appId == 'settings' ? 'Settings' : 'Assistant'),
        leading: IconButton(
          tooltip: 'Close app',
          onPressed: widget.onClose,
          icon: const Icon(Icons.arrow_back),
        ),
        actions: [
          if (widget.onMoreSettings != null)
            TextButton(
              onPressed: widget.onMoreSettings,
              child: const Text('More settings'),
            ),
        ],
      ),
      body: Column(
        children: [
          if (_busy) const LinearProgressIndicator(),
          if (_error != null)
            Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                children: [
                  Text(_error!),
                  TextButton(
                    onPressed: _busy ? null : _open,
                    child: const Text('Retry'),
                  ),
                ],
              ),
            ),
          if (surface != null)
            Expanded(
              child: AbsorbPointer(
                absorbing: _busy,
                child: NeuronView(
                  kind: surface['kind'] as String,
                  name: surface['name'] as String,
                  load: _loadNode,
                  revision: _renderRevision,
                  enabled: !_busy,
                  onAction: (event) => _open(event: event),
                ),
              ),
            ),
        ],
      ),
    );
  }
}
