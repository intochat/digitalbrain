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
}
