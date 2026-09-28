import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:digitalbrain_ui/src/gallery/neuron_gallery.dart';
import 'package:digitalbrain_ui/src/gallery/ui_gallery_preview.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('catalog covers registry exactly and labels fallback honestly', () {
    final neurons = galleryEntries.where(
      (entry) => entry.id.startsWith('neuron:'),
    );
    expect(
      neurons.map((entry) => entry.id.substring(7)).toSet(),
      RendererRegistry.uiKinds.toSet(),
    );
    expect(neurons.length, RendererRegistry.uiKinds.length);
    for (final entry in neurons) {
      final kind = entry.id.substring(7);
      expect(
        entry.category == 'Composable neurons',
        !RendererRegistry.uiEntry(kind).isFallback,
      );
      if (!RendererRegistry.uiEntry(kind).isFallback) {
        expect(neuronExample(kind), isNotEmpty);
        expect(neuronDescriptions[kind], isNotNull);
      }
    }
  });

  testWidgets('search, select, and return on narrow settings panel', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(390, 800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      const MaterialApp(home: Scaffold(body: UiGalleryScreen())),
    );
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const Key('gallery_search')),
      'textfield',
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('gallery_entry_neuron:textfield')));
    await tester.pumpAndSettle();
    expect(find.text('Composable neuron · dedicated renderer'), findsOneWidget);
    expect(find.widgetWithText(TextField, 'Book title'), findsOneWidget);
    expect(tester.takeException(), isNull);
    await tester.tap(find.byKey(const Key('gallery_back')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('gallery_search')), findsOneWidget);
    await tester.enterText(
      find.byKey(const Key('gallery_search')),
      'no-such-component',
    );
    await tester.pumpAndSettle();
    expect(find.text('No components found'), findsOneWidget);
  });

  testWidgets('category filters presentation widgets from neurons', (
    tester,
  ) async {
    await tester.pumpWidget(
      const MaterialApp(home: Scaffold(body: UiGalleryScreen())),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('gallery_category')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Foundations').last);
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('gallery_entry_lumen')), findsOneWidget);
    expect(find.byKey(const Key('gallery_entry_neuron:surface')), findsNothing);
  });

  for (final kind in RendererRegistry.uiKinds) {
    testWidgets('$kind preview renders bounded at narrow width', (
      tester,
    ) async {
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: SizedBox(width: 280, child: NeuronGalleryPreview(kind: kind)),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      if (RendererRegistry.uiEntry(kind).isFallback) {
        expect(find.text('Not supported yet: $kind'), findsOneWidget);
      }
    });
  }

  testWidgets('composed tabs switch their actual child view locally', (
    tester,
  ) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(body: NeuronGalleryPreview(kind: 'tabs')),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('A Field Guide'), findsOneWidget);
    await tester.tap(find.text('Summary'));
    await tester.pumpAndSettle();
    expect(find.text('Currently reading'), findsOneWidget);
    expect(find.text('A Field Guide'), findsNothing);
  });
}
