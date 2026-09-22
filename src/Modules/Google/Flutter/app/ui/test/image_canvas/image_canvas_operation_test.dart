import 'dart:convert';
import 'dart:async';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';

void main() {
  testWidgets(
    'export keeps its originating callback and locks before rendering',
    (tester) async {
      final bytes = base64Decode(
        'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg==',
      );
      final rendering = Completer<Uint8List>();
      var busy = false;
      String? saved;
      Widget build(String id) => MaterialApp(
        home: Scaffold(
          body: UiImageCanvas(
            bytes: bytes,
            recipe: const ImageRecipe(),
            sourceWidth: 1,
            sourceHeight: 1,
            exportImage: (_, _) => rendering.future,
            onEdit: (_) async {},
            onSave: (_) async {
              saved = id;
            },
            onBusyChanged: (value) => busy = value,
          ),
        ),
      );
      await tester.pumpWidget(build('first'));
      await tester.runAsync(
        () => Future<void>.delayed(const Duration(milliseconds: 50)),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('Save copy'));
      expect(busy, isTrue);
      await tester.pumpWidget(build('second'));
      rendering.complete(bytes);
      await tester.runAsync(
        () => Future<void>.delayed(const Duration(milliseconds: 50)),
      );
      await tester.pumpAndSettle();
      expect(saved, 'first');
      expect(busy, isFalse);
    },
  );

  testWidgets('stroke persist waits for debounce and does not lock drawing', (
    tester,
  ) async {
    final bytes = base64Decode(
      'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg==',
    );
    var edits = 0;
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: UiImageCanvas(
            bytes: bytes,
            recipe: const ImageRecipe(),
            sourceWidth: 1,
            sourceHeight: 1,
            persistDebounce: const Duration(milliseconds: 40),
            onEdit: (_) async {
              edits++;
            },
            onSave: (_) async {},
          ),
        ),
      ),
    );
    await tester.runAsync(
      () => Future<void>.delayed(const Duration(milliseconds: 50)),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('Draw'));
    await tester.pump();
    final canvas = tester.getRect(find.bySemanticsLabel('Image canvas'));
    final gesture = await tester.startGesture(canvas.center);
    await gesture.moveBy(const Offset(0.2, 0));
    await gesture.up();
    await tester.pump();
    expect(edits, 0);
    await tester.pump(const Duration(milliseconds: 50));
    expect(edits, 1);
  });
}
