import 'dart:convert';

import 'package:digitalbrain_flutter/grpc/digitalbrain.pb.dart' as gw;
import 'package:digitalbrain_flutter/grpc/endpoint.dart';

const _metaKeys = {
  'actionId',
  'label',
  'synapseType',
  'type',
  'target',
  'targetSurfaceKind',
  'path',
  'props',
};

/// Builds the unary `Send` envelope for a surface action event, or null when the
/// event carries no synapse to fire (pure navigation, hover, etc.). The browser
/// channel is gRPC-Web, which supports only unary + server-streaming — so kit/form
/// actions must travel as a unary `Send`, never the bidirectional `EngageUiSession`.
/// The unary `Send` handlers read action props at the TOP LEVEL of the payload, so
/// the flattened props become the payload directly.
gw.SynapseEnvelope? buildActionEnvelope(
  String name,
  Map<String, Object?> args, {
  String? defaultClientId,
}) {
  if (name != 'press' && name != 'select' && name != 'action') return null;

  final rawAction = args['action'];
  final action = rawAction is Map ? rawAction.cast<String, Object?>() : args;

  final synapseType = (action['synapseType'] as String?)?.trim();
  if (synapseType == null || synapseType.isEmpty) return null;

  final rawProps = action['props'];
  final topLevelProps = {
    for (final entry in action.entries)
      if (!_metaKeys.contains(entry.key)) entry.key: entry.value,
  };
  final props = rawProps is Map
      ? {...topLevelProps, ...rawProps.cast<String, Object?>()}
      : topLevelProps;

  final resolvedProps = Map<String, Object?>.of(props);
  final fallbackClientId = defaultClientId?.trim();
  if (fallbackClientId != null && fallbackClientId.isNotEmpty) {
    final currentClientId = resolvedProps['clientId']?.toString().trim();
    if (currentClientId == null || currentClientId.isEmpty) {
      resolvedProps['clientId'] = fallbackClientId;
    }
  }

  final callbackPath = resolvedProps['callbackPath']?.toString().trim();
  final redirectUri = resolvedProps['redirect_uri']?.toString().trim();
  if (callbackPath != null &&
      callbackPath.isNotEmpty &&
      (redirectUri == null || redirectUri.isEmpty)) {
    resolvedProps['redirect_uri'] = resolveKernelCallbackUrl(callbackPath);
  }

  final stringProps = <String, String>{
    for (final entry in resolvedProps.entries)
      entry.key: entry.value?.toString() ?? '',
  };

  return gw.SynapseEnvelope()
    ..correlationId = (action['actionId'] as String?) ?? synapseType
    ..typeName = synapseType
    ..payload = utf8.encode(jsonEncode(stringProps));
}

/// Builds the unary `Send` envelope for a floating-panel event.
///
/// Panel bodies can emit the same action-shaped events as the shell/chat UI, but
/// some older panel surfaces still use a plain `type` property. Prefer the
/// shared action dispatcher so nested `props` are flattened consistently, then
/// fall back to the legacy typed payload shape for those older surfaces.
gw.SynapseEnvelope? buildPanelEventEnvelope(
  String panelId,
  String name,
  Map<String, Object?> args, {
  String? defaultClientId,
}) {
  final actionEnvelope = buildActionEnvelope(
    name,
    args,
    defaultClientId: defaultClientId,
  );
  if (actionEnvelope != null) return actionEnvelope;

  var type = args['type']?.toString();
  if (type == null || type.isEmpty) return null;

  if (type == 'cancelTask') type = 'CancelTask';

  final payload = Map<String, Object?>.of(args)..remove('type');
  final fallbackClientId = defaultClientId?.trim();
  if (fallbackClientId != null && fallbackClientId.isNotEmpty) {
    final currentClientId = payload['clientId']?.toString().trim();
    if (currentClientId == null || currentClientId.isEmpty) {
      payload['clientId'] = fallbackClientId;
    }
  }

  return gw.SynapseEnvelope()
    ..correlationId = panelId
    ..typeName = type
    ..payload = utf8.encode(jsonEncode(payload));
}
