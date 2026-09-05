import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test(
    'module icon keys are allowlisted without provider substring guessing',
    () {
      BrainNeuron neuron({String? iconKey, String type = 'google-drive'}) =>
          BrainNeuron(
            id: 'observed',
            type: type,
            name: 'local',
            label: 'Observed',
            module: 'Google',
            iconKey: iconKey,
          );
      expect(brainNeuronIcon(neuron(iconKey: 'gmail')), NeuronIconKind.gmail);
      expect(
        brainNeuronIcon(neuron(iconKey: 'salesforce')),
        NeuronIconKind.salesforce,
      );
      expect(brainNeuronIcon(neuron(iconKey: 'aspire')), NeuronIconKind.aspire);
      expect(brainNeuronIcon(neuron(iconKey: 'github')), NeuronIconKind.github);
      expect(brainNeuronIcon(neuron()), NeuronIconKind.generic);
      expect(brainNeuronIcon(neuron(type: 'gmail')), NeuronIconKind.generic);
      expect(
        brainNeuronIcon(neuron(iconKey: 'https://example.com/gmail.svg')),
        NeuronIconKind.generic,
      );
      expect(
        brainNeuronIcon(neuron(iconKey: '../assets/gmail.svg')),
        NeuronIconKind.generic,
      );
      expect(
        brainNeuronIcon(neuron(iconKey: 'future', type: 'assistant')),
        NeuronIconKind.generic,
      );
      expect(
        brainNeuronIcon(neuron(type: 'assistant')),
        NeuronIconKind.assistant,
      );
    },
  );

  Widget host(Widget child, {bool reducedMotion = false}) => MaterialApp(
    theme: KitTheme.light(),
    home: KitThemeScope(
      child: MediaQuery(
        data: MediaQueryData(disableAnimations: reducedMotion),
        child: Scaffold(body: Center(child: child)),
      ),
    ),
  );

  testWidgets('Forui input preserves caller draft and dispatches submit', (
    tester,
  ) async {
    final draft = TextEditingController(text: 'Existing draft');
    String? submitted;
    await tester.pumpWidget(
      host(
        SizedBox(
          width: 360,
          child: LumenTextField(
            controller: draft,
            hint: 'Ask Ino',
            onSubmitted: (value) => submitted = value,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Existing draft'), findsOneWidget);
    await tester.enterText(find.byType(EditableText), 'Review my changes');
    await tester.testTextInput.receiveAction(TextInputAction.send);
    expect(submitted, 'Review my changes');
    expect(draft.text, 'Review my changes');
    await tester.pumpWidget(const SizedBox.shrink());
    draft.text = 'Still owned by caller';
    draft.dispose();
  });

  testWidgets('Forui product actions are labeled and honor disabled state', (
    tester,
  ) async {
    var presses = 0;
    await tester.pumpWidget(
      host(
        Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            LumenIconButton(
              icon: const Icon(Icons.send),
              label: 'Send to Ino',
              onPressed: () => presses++,
            ),
            const LumenActionButton(label: 'Unavailable'),
          ],
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('Send to Ino'));
    await tester.tap(find.text('Unavailable'));
    await tester.pumpAndSettle();
    expect(presses, 1);
    expect(tester.takeException(), isNull);
  });

  testWidgets('reduced motion settles active Ino and provider icons render', (
    tester,
  ) async {
    final semantics = tester.ensureSemantics();
    await tester.pumpWidget(
      host(
        const Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            InoPresence(state: InoPresenceState.working),
            NeuronIcon(kind: NeuronIconKind.gmail),
            NeuronIcon(kind: NeuronIconKind.salesforce),
            NeuronIcon(kind: NeuronIconKind.aspire),
            NeuronIcon(kind: NeuronIconKind.github),
          ],
        ),
        reducedMotion: true,
      ),
    );
    await tester.pumpAndSettle();
    expect(find.bySemanticsLabel('Ino, working'), findsOneWidget);
    expect(tester.binding.hasScheduledFrame, isFalse);
    expect(tester.takeException(), isNull);
    semantics.dispose();
  });
}
