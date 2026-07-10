import 'dart:async';

import 'package:flutter/material.dart';

import '../../rfw_host/rfw_runtime_host.dart';
import '../protocol/surface_protocol.dart';
import '../v2_runtime.dart';
import 'v2_ino_conversation_view.dart';

typedef V2SurfaceActionSubmit =
    Future<V2ActionResult> Function(
      SurfaceEnvelope surface,
      String bindingId,
      Map<String, Object?> input,
    );

const Key v2SurfaceActionProgressKey = Key('v2-surface-action-progress');
const Key v2SurfaceActionErrorKey = Key('v2-surface-action-error');

class V2SurfaceView extends StatefulWidget {
  const V2SurfaceView({
    super.key,
    required this.surface,
    required this.onSubmitAction,
    this.actionEnabled = true,
    this.reconnecting = false,
    this.connectionUnavailable = false,
    this.rfwHost,
  });

  final SurfaceEnvelope surface;
  final V2SurfaceActionSubmit onSubmitAction;
  final bool actionEnabled;
  final bool reconnecting;
  final bool connectionUnavailable;
  final RfwRuntimeHost? rfwHost;

  @override
  State<V2SurfaceView> createState() => _V2SurfaceViewState();
}

class _V2SurfaceViewState extends State<V2SurfaceView> {
  late final RfwRuntimeHost _rfwHost = widget.rfwHost ?? RfwRuntimeHost();
  bool _submitting = false;
  String? _actionError;

  @override
  Widget build(BuildContext context) {
    Widget body;
    try {
      body = switch (widget.surface.payload) {
        WidgetTreeSurfacePayload payload => UiSurfaceTreeRenderer().build(
          payload.tree,
          _onRemoteEvent,
          rfwHost: _rfwHost,
        ),
        RfwSurfacePayload payload => _buildRfw(payload),
        InoConversationSurfacePayload payload => V2InoConversationView(
          surface: widget.surface,
          payload: payload,
          onSubmitAction: widget.onSubmitAction,
          actionEnabled: widget.actionEnabled,
          reconnecting: widget.reconnecting,
          connectionUnavailable: widget.connectionUnavailable,
        ),
        NativeSurfacePayload payload => _buildNative(payload),
      };
    } catch (_) {
      body = const Center(child: Text('This view could not be displayed.'));
    }

    return Semantics(
      container: true,
      label: widget.surface.payload is InoConversationSurfacePayload
          ? 'INO conversation'
          : 'DigitalBrain workspace',
      child: Stack(
        fit: StackFit.expand,
        children: [
          body,
          if (_submitting)
            const Align(
              alignment: Alignment.topCenter,
              child: LinearProgressIndicator(key: v2SurfaceActionProgressKey),
            ),
          if (_actionError case final message?)
            Align(
              alignment: Alignment.bottomCenter,
              child: Material(
                color: Theme.of(context).colorScheme.errorContainer,
                child: Padding(
                  key: v2SurfaceActionErrorKey,
                  padding: const EdgeInsets.all(12),
                  child: Text(message),
                ),
              ),
            ),
        ],
      ),
    );
  }

  Widget _buildRfw(RfwSurfacePayload payload) {
    final source = payload.libraryText;
    if (source == null) {
      return const Center(
        child: Text('This view is not available in this app.'),
      );
    }
    final key = 'v2-${widget.surface.contentHash}-${widget.surface.revision}';
    _rfwHost.ensureLoaded(key, source);
    if (_rfwHost.parseError(key) != null) {
      return const Center(child: Text('This view could not be displayed.'));
    }
    return _rfwHost.render(
      key,
      data: payload.data,
      onEvent: _onRemoteEvent,
      rootWidget: payload.rootWidget,
      semanticsId: 'digitalbrain-v2-surface',
      semanticsLabel: 'Interactive workspace view',
    );
  }

  Widget _buildNative(NativeSurfacePayload payload) {
    final title = (payload.data['title'] ?? 'DigitalBrain').toString();
    final message =
        (payload.data['message'] ??
                payload.data['body'] ??
                payload.data['text'] ??
                '')
            .toString();
    return Center(
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 720),
        child: Card(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(title, style: Theme.of(context).textTheme.headlineSmall),
                if (message.isNotEmpty) ...[
                  const SizedBox(height: 12),
                  Text(message),
                ],
                if (widget.surface.actions.isNotEmpty) ...[
                  const SizedBox(height: 20),
                  Wrap(
                    spacing: 8,
                    runSpacing: 8,
                    children: [
                      for (
                        var index = 0;
                        index < widget.surface.actions.length;
                        index++
                      )
                        FilledButton(
                          key: ValueKey('v2-native-action-$index'),
                          onPressed: _submitting || !widget.actionEnabled
                              ? null
                              : () => _submit(
                                  widget.surface.actions[index],
                                  const <String, Object?>{},
                                ),
                          child: Text(
                            _actionLabel(
                              widget.surface.actions[index].actionType,
                            ),
                          ),
                        ),
                    ],
                  ),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }

  void _onRemoteEvent(String name, Map<String, Object?> arguments) {
    final declaredBinding =
        arguments['bindingId']?.toString() ??
        arguments['binding_id']?.toString() ??
        arguments['actionBindingId']?.toString() ??
        arguments['action_binding_id']?.toString();
    UiActionRef? action;
    if (declaredBinding != null && declaredBinding.isNotEmpty) {
      action = widget.surface.actionByBindingId(declaredBinding);
    } else {
      final actionType = arguments['actionType']?.toString() ?? name;
      action = widget.surface.actionByType(actionType);
      if (action == null && widget.surface.actions.length == 1) {
        action = widget.surface.actions.single;
      }
    }
    if (action == null) {
      setState(() => _actionError = 'That option is no longer available.');
      return;
    }

    final declaredInput = arguments['input'];
    final input = declaredInput is Map
        ? Map<String, Object?>.from(declaredInput)
        : <String, Object?>{};
    unawaited(_submit(action, input));
  }

  Future<void> _submit(UiActionRef action, Map<String, Object?> input) async {
    if (_submitting) return;
    setState(() {
      _submitting = true;
      _actionError = null;
    });
    try {
      await widget.onSubmitAction(widget.surface, action.bindingId, input);
    } catch (_) {
      if (mounted) {
        setState(
          () => _actionError =
              'That action couldn\'t be completed. Please try again.',
        );
      }
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  static String _actionLabel(String actionType) {
    return 'Continue';
  }
}
