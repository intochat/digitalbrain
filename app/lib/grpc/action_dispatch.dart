import 'dart:convert';

import 'package:digitalbrain_flutter/grpc/digitalbrain.pb.dart' as gw;

const _metaKeys = {
  'actionId',
  'label',
  'synapseType',
  'target',
  'targetSurfaceKind',
  'path',
};

/// Builds the unary `Send` envelope for a surface action event, or null when the
/// event carries no synapse to fire (pure navigation, hover, etc.). The browser
/// channel is gRPC-Web, which supports only unary + server-streaming — so kit/form
/// actions must travel as a unary `Send`, never the bidirectional `EngageUiSession`.
/// The unary `Send` handlers read action props at the TOP LEVEL of the payload, so
/// the flattened props become the payload directly.
gw.SynapseEnvelope? buildActionEnvelope(String name, Map<String, Object?> args) {
  if (name != 'press' && name != 'select' && name != 'action') return null;

  final rawAction = args['action'];
  final action = rawAction is Map ? rawAction.cast<String, Object?>() : args;

  final synapseType = (action['synapseType'] as String?)?.trim();
  if (synapseType == null || synapseType.isEmpty) return null;

  final rawProps = action['props'];
  final props = rawProps is Map
      ? rawProps.cast<String, Object?>()
      : {
          for (final entry in action.entries)
            if (!_metaKeys.contains(entry.key)) entry.key: entry.value,
        };

  final stringProps = <String, String>{
    for (final entry in props.entries) entry.key: entry.value?.toString() ?? '',
  };

  return gw.SynapseEnvelope()
    ..correlationId = (action['actionId'] as String?) ?? synapseType
    ..typeName = synapseType
    ..payload = utf8.encode(jsonEncode(stringProps));
}
