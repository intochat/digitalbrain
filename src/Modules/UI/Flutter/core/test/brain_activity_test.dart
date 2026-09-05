import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:test/test.dart';

Map<String, dynamic> event(int sequence, String state) => {
  'id': 'activity-$sequence',
  'neuronId': 'assistant:assistant',
  'direction': 'Outgoing',
  'sequence': sequence,
  'signalType': 'AgentActivity',
  'timestamp': '2026-09-05T12:00:00Z',
  'operationId': 'request-1',
  'kind': 'delegation',
  'state': state,
  'name': 'Aspire',
  'targetId': 'aspire:principal.local',
};

void main() {
  test(
    'presentation icon keys are optional and preserved independently of type',
    () {
      final json = <String, dynamic>{
        'id': 'gmail:principal.local',
        'type': 'gmail',
        'name': 'local',
        'label': 'Gmail',
        'module': 'Google',
      };
      expect(BrainNeuron.fromJson(json).iconKey, isNull);
      expect(
        BrainNeuron.fromJson({...json, 'iconKey': 'gmail'}).iconKey,
        'gmail',
      );
      expect(
        BrainNeuron.fromJson({...json, 'iconKey': 'future-icon'}).iconKey,
        'future-icon',
      );
    },
  );

  test(
    'completed request replaces its in-flight trace in newest-first journals',
    () {
      final snapshot = BrainSnapshot.fromJson({
        'rootId': 'chat:main',
        'observedAt': '2026-09-05T12:00:01Z',
        'activity': [event(2, 'completed'), event(1, 'started')],
      });
      expect(snapshot.activeDelegations, isEmpty);
      expect(snapshot.synapses, isEmpty);
      final running = BrainSnapshot(
        rootId: snapshot.rootId,
        observedAt: snapshot.observedAt,
        activity: [BrainActivity.fromJson(event(1, 'started'))],
      );
      expect(
        running.activeDelegations.single.targetId,
        'aspire:principal.local',
      );
    },
  );

  test('generic tool evidence and legacy activities both deserialize', () {
    final tool = BrainActivity.fromJson({
      ...event(3, 'completed'),
      'kind': 'tool',
      'name': 'list_resources',
      'server': 'Aspire',
      'durationMs': 40,
      'isError': true,
      'truncated': true,
      'resultPreview': '{"content":[{"type":"text","text":"Ready"}]}',
      'failureCode': 'authentication_required',
    });
    expect(tool.server, 'Aspire');
    expect(tool.durationMs, 40.0);
    expect(tool.isError, isTrue);
    expect(tool.truncated, isTrue);
    expect(tool.resultPreview, contains('"content"'));
    expect(tool.failureCode, 'authentication_required');
    final legacy = event(1, 'started')
      ..remove('operationId')
      ..remove('kind')
      ..remove('state');
    expect(BrainActivity.fromJson(legacy).operationId, isNull);
    expect(BrainActivity.fromJson(legacy).failureCode, isNull);
  });
}
