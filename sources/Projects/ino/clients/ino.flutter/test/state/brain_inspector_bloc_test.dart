import 'package:bloc_test/bloc_test.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/state/brain_inspector_bloc.dart';

FireEvent _fire({
  String fromId = 'a',
  String toId = 'b',
  String type = 'ChatIntent',
  String payload = '{"x":1}',
  int tsMs = 0,
}) =>
    FireEvent(
      id: '$fromId>$toId@$tsMs',
      traceParent: '00-trace-span-01',
      synapseType: type,
      fromId: fromId,
      toId: toId,
      payloadJson: payload,
      timestampUnixMs: tsMs,
    );

void main() {
  group('BrainInspectorBloc', () {
    test('IngestFire pushes onto sender and receiver buffers, newest first', () async {
      final bloc = BrainInspectorBloc();
      bloc.add(IngestFire(_fire(fromId: 'cortex', toId: 'flightSearch', tsMs: 1)));
      bloc.add(IngestFire(_fire(fromId: 'cortex', toId: 'hotelSearch',  tsMs: 2)));
      await Future<void>.delayed(Duration.zero);

      expect(bloc.state.recentByNodeId['cortex']!.map((f) => f.toId),
          equals(['hotelSearch', 'flightSearch']));
      expect(bloc.state.recentByNodeId['flightSearch']!.single.fromId, equals('cortex'));
    });

    test('ring buffer caps at 25 per id, evicting oldest', () async {
      final bloc = BrainInspectorBloc();
      for (var i = 0; i < 30; i++) {
        bloc.add(IngestFire(_fire(fromId: 'cortex', toId: 't$i', tsMs: i)));
      }
      await Future<void>.delayed(Duration.zero);

      expect(bloc.state.recentByNodeId['cortex'], hasLength(25));
      expect(bloc.state.recentByNodeId['cortex']!.first.toId, equals('t29'));
      expect(bloc.state.recentByNodeId['cortex']!.last.toId, equals('t5'));
    });

    blocTest<BrainInspectorBloc, BrainInspectorState>(
      'SelectNode then Deselect emits selected then null',
      build: BrainInspectorBloc.new,
      act: (b) {
        b.add(SelectNeuron(nodeId: 'cortex'));
        b.add(Deselect());
      },
      expect: () => [
        isA<BrainInspectorState>().having((s) => s.selected, 'selected', isA<NeuronSelection>()),
        isA<BrainInspectorState>().having((s) => s.selected, 'selected', isNull),
      ],
    );

    blocTest<BrainInspectorBloc, BrainInspectorState>(
      'PausePulse sets pausedPulse; Deselect clears it',
      build: BrainInspectorBloc.new,
      act: (b) {
        final p = _fire(tsMs: 7);
        b.add(PausePulse(pulse: p));
        b.add(Deselect());
      },
      expect: () => [
        isA<BrainInspectorState>().having((s) => s.pausedPulse?.timestampUnixMs, 'paused', 7),
        isA<BrainInspectorState>().having((s) => s.pausedPulse, 'paused', isNull),
      ],
    );

    test('FireEvent.fromBrainPulse maps proto fields correctly', () {
      final fire = FireEvent.fromBrainPulse(
        fromGrain: 'cortex',
        toGrain: 'travel.flight_search',
        methodName: 'HandleAsync',
        payloadJson: '{"text":"hi"}',
        traceParent: '00-trace-span-01',
        timestampUnixMs: 12345,
      );
      expect(fire.fromId, equals('cortex'));
      expect(fire.toId, equals('travel.flight_search'));
      expect(fire.synapseType, equals('HandleAsync'));
      expect(fire.payloadJson, equals('{"text":"hi"}'));
    });
  });
}
