import 'dart:async';
import 'dart:convert';
import 'dart:math';

import 'package:flutter/material.dart';

import '../../rfw_host/rfw_runtime_host.dart';
import '../protocol/surface_protocol.dart';
import '../runtime.dart';
import 'ino_conversation_view.dart';

typedef SurfaceActionSubmit =
    Future<ActionResult> Function(
      SurfaceEnvelope surface,
      String bindingId,
      Map<String, Object?> input,
    );

const Key surfaceActionProgressKey = Key('v2-surface-action-progress');
const Key surfaceActionErrorKey = Key('v2-surface-action-error');
const Key featureApprovalApproveKey = Key('v2-feature-approval-approve');
const Key featureApprovalRejectKey = Key('v2-feature-approval-reject');

class SurfaceView extends StatefulWidget {
  const SurfaceView({
    super.key,
    required this.surface,
    required this.onSubmitAction,
    this.actionEnabled = true,
    this.reconnecting = false,
    this.connectionUnavailable = false,
    this.rfwHost,
  });

  final SurfaceEnvelope surface;
  final SurfaceActionSubmit onSubmitAction;
  final bool actionEnabled;
  final bool reconnecting;
  final bool connectionUnavailable;
  final RfwRuntimeHost? rfwHost;

  @override
  State<SurfaceView> createState() => _SurfaceViewState();
}

class _SurfaceViewState extends State<SurfaceView> {
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
        InoConversationSurfacePayload payload => InoConversationView(
          surface: widget.surface,
          payload: payload,
          onSubmitAction: widget.onSubmitAction,
          actionEnabled: widget.actionEnabled,
          reconnecting: widget.reconnecting,
          connectionUnavailable: widget.connectionUnavailable,
        ),
        FeatureApprovalSurfacePayload payload => _buildFeatureApproval(payload),
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
              child: LinearProgressIndicator(key: surfaceActionProgressKey),
            ),
          if (_actionError case final message?)
            Align(
              alignment: Alignment.bottomCenter,
              child: Material(
                color: Theme.of(context).colorScheme.errorContainer,
                child: Padding(
                  key: surfaceActionErrorKey,
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

  Widget _buildFeatureApproval(FeatureApprovalSurfacePayload payload) {
    final theme = Theme.of(context);
    final action = widget.surface.actionByType('feature.release.decision.v1');
    return SingleChildScrollView(
      padding: const EdgeInsets.all(24),
      child: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 760),
          child: Card(
            child: Padding(
              padding: const EdgeInsets.all(24),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(payload.title, style: theme.textTheme.headlineSmall),
                  const SizedBox(height: 16),
                  _approvalField('Installation', payload.installationId),
                  _approvalField('Release digest', payload.releaseDigest),
                  _approvalField(
                    'Source',
                    '${payload.sourceKind} · ${payload.sourceReference}',
                  ),
                  _approvalField('Revision', payload.revision.toString()),
                  _approvalList(
                    'Requested capabilities',
                    payload.requestedCapabilities,
                  ),
                  _approvalList('Added', payload.addedCapabilities),
                  _approvalList('Removed', payload.removedCapabilities),
                  const SizedBox(height: 8),
                  Text(
                    'Capability bindings',
                    style: theme.textTheme.titleMedium,
                  ),
                  const SizedBox(height: 8),
                  for (final binding in payload.capabilityBindings)
                    Padding(
                      padding: const EdgeInsets.only(bottom: 12),
                      child: DecoratedBox(
                        decoration: BoxDecoration(
                          border: Border.all(
                            color: theme.colorScheme.outlineVariant,
                          ),
                          borderRadius: BorderRadius.circular(8),
                        ),
                        child: Padding(
                          padding: const EdgeInsets.all(12),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                '${binding.capabilityId} v${binding.capabilityVersion}',
                              ),
                              if (binding.provider != null)
                                Text('Provider: ${binding.provider}'),
                              if (binding.providerConnectionId != null)
                                Text(
                                  'Connection: ${binding.providerConnectionId}',
                                ),
                              Text(
                                'Constraints: ${jsonEncode(binding.constraints)}',
                              ),
                            ],
                          ),
                        ),
                      ),
                    ),
                  const SizedBox(height: 12),
                  Row(
                    mainAxisAlignment: MainAxisAlignment.end,
                    children: [
                      OutlinedButton(
                        key: featureApprovalRejectKey,
                        onPressed:
                            action == null ||
                                _submitting ||
                                !widget.actionEnabled
                            ? null
                            : () => _submitFeatureDecision(
                                action,
                                payload,
                                false,
                              ),
                        child: const Text('Reject'),
                      ),
                      const SizedBox(width: 12),
                      FilledButton(
                        key: featureApprovalApproveKey,
                        onPressed:
                            action == null ||
                                _submitting ||
                                !widget.actionEnabled
                            ? null
                            : () =>
                                  _submitFeatureDecision(action, payload, true),
                        child: const Text('Approve'),
                      ),
                    ],
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  Widget _approvalField(String label, String value) => Padding(
    padding: const EdgeInsets.only(bottom: 10),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(label, style: Theme.of(context).textTheme.labelLarge),
        const SizedBox(height: 2),
        SelectableText(value),
      ],
    ),
  );

  Widget _approvalList(String label, List<String> values) =>
      _approvalField(label, values.isEmpty ? 'None' : values.join(', '));

  Future<void> _submitFeatureDecision(
    UiActionRef action,
    FeatureApprovalSurfacePayload payload,
    bool approved,
  ) => _submit(action, {
    'approvalId': payload.approvalId,
    'releaseDigest': payload.releaseDigest,
    'expectedRevision': payload.revision,
    'decision': approved ? 'approve' : 'reject',
    'clientDecisionId': _clientDecisionId(),
  });

  static String _clientDecisionId() {
    final random = Random.secure();
    return List.generate(
      16,
      (_) => random.nextInt(256).toRadixString(16).padLeft(2, '0'),
    ).join();
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
