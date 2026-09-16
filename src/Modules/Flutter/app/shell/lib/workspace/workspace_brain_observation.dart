import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';

/// A dated observation for attached live views, not an editable artifact ID.
Map<String, dynamic> workspaceBrainObservation(BrainSnapshot snapshot) => {
  'rootId': snapshot.rootId,
  'scope': snapshot.scope,
  'observedAt': snapshot.observedAt.toUtc().toIso8601String(),
  'truncated': snapshot.truncated,
  'nodes': [
    for (final node in snapshot.nodes)
      {
        'id': node.id,
        'type': node.type,
        'name': node.name,
        'label': node.label,
        'module': node.module,
        'role': node.role,
        'status': node.status,
        'iconKey': node.iconKey,
        'handledSignals': node.handledSignals,
        'incomingSequence': node.incomingSequence,
        'outgoingSequence': node.outgoingSequence,
        'lastActivityAt': node.lastActivityAt?.toUtc().toIso8601String(),
        'isInfrastructure': node.isInfrastructure,
      },
  ],
  'synapses': [
    for (final edge in snapshot.synapses)
      {
        'id': edge.id,
        'sourceId': edge.sourceId,
        'targetId': edge.targetId,
        'signalType': edge.signalType,
        'kind': edge.kind,
        'weight': edge.weight,
        'fireCount': edge.fireCount,
        'lastFiredAt': edge.lastFiredAt?.toUtc().toIso8601String(),
        'isBlocking': edge.isBlocking,
        'canUnsubscribe': edge.canUnsubscribe,
      },
  ],
  'activity': [
    for (final event in snapshot.activity)
      {
        'id': event.id,
        'neuronId': event.neuronId,
        'direction': event.direction,
        'sequence': event.sequence,
        'signalType': event.signalType,
        'timestamp': event.timestamp?.toUtc().toIso8601String(),
        'summary': event.summary,
        'callerId': event.callerId,
        'correlationId': event.correlationId,
        'operationId': event.operationId,
        'kind': event.kind,
        'state': event.state,
        'name': event.name,
        'targetId': event.targetId,
        'server': event.server,
        'durationMs': event.durationMs,
        'resultPreview': event.resultPreview,
        'failureCode': event.failureCode,
        'isError': event.isError,
        'truncated': event.truncated,
      },
  ],
};
