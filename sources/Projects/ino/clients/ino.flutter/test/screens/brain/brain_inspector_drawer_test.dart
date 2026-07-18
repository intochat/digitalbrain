import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/screens/brain/brain_inspector_drawer.dart';
import 'package:ino_flutter/state/brain_inspector_bloc.dart';

Widget _wrap(BrainInspectorBloc bloc) => MaterialApp(
      home: Scaffold(
        body: BlocProvider.value(
          value: bloc,
          child: const BrainInspectorDrawer(),
        ),
      ),
    );

void main() {
  group('BrainInspectorDrawer', () {
    testWidgets('renders nothing when no node is selected', (tester) async {
      final bloc = BrainInspectorBloc();
      await tester.pumpWidget(_wrap(bloc));
      expect(find.byKey(const Key('brain-inspector-drawer-panel')), findsNothing);
    });

    testWidgets('neuron selection renders title + role + traffic list', (tester) async {
      final bloc = BrainInspectorBloc();
      bloc.add(IngestFire(FireEvent(
        id: '1',
        traceParent: 't',
        synapseType: 'ChatIntent',
        fromId: 'kernel.cortex',
        toId: 'travel.find_flights',
        payloadJson: '{"text":"hi"}',
        timestampUnixMs: DateTime.now().millisecondsSinceEpoch,
      )));
      bloc.add(SelectNeuron(nodeId: 'kernel.cortex'));

      await tester.pumpWidget(_wrap(bloc));
      await tester.pump(Duration.zero);

      expect(find.text('Cortex'), findsOneWidget);
      expect(find.textContaining('Routes user prompts'), findsOneWidget);
      expect(find.textContaining('ChatIntent'), findsOneWidget);
    });

    testWidgets('synapse type selection renders title + consumers section', (tester) async {
      final bloc = BrainInspectorBloc();
      bloc.add(SelectSynapseType(nodeId: 'syn.chat_intent'));

      await tester.pumpWidget(_wrap(bloc));
      await tester.pump(Duration.zero);
      expect(find.text('ChatIntent'), findsOneWidget);
      expect(find.text('Consumers'), findsOneWidget);
    });

    testWidgets('pulse selection renders traceparent + payload', (tester) async {
      final pulse = FireEvent(
        id: 'p1',
        traceParent: '00-abc-def-01',
        synapseType: 'ChatIntent',
        fromId: 'kernel.cortex',
        toId: 'travel.plan',
        payloadJson: '{"text":"hi"}',
        timestampUnixMs: 0,
      );
      final bloc = BrainInspectorBloc();
      bloc.add(PausePulse(pulse: pulse));

      await tester.pumpWidget(_wrap(bloc));
      await tester.pump(Duration.zero);
      expect(find.textContaining('00-abc-def-01'), findsOneWidget);
      expect(find.textContaining('"text"'), findsOneWidget);
    });
  });
}
