import 'dart:convert';
import 'dart:typed_data';

import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

// 1x1 transparent PNG, the smallest valid PNG payload.
final _tinyPng = base64Decode(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=',
);

// Image.memory decodes through the real platform codec, which the fake-time
// test clock never advances — pumpAndSettle would hang forever on the
// in-progress frameBuilder placeholder's spinner, so pump a bounded number
// of frames instead of settling.
Future<void> _pumpKitImage(WidgetTester tester) async {
  await tester.pump();
  await tester.pump();
}

void main() {
  testWidgets('KitImage renders the supplied bytes and caption', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: KitImage(bytes: _tinyPng, caption: 'Sunset over bay'),
      ),
    );
    await _pumpKitImage(tester);

    expect(find.byKey(const Key('kit_image_Sunset over bay')), findsOneWidget);
    expect(find.text('Sunset over bay'), findsOneWidget);
    expect(find.byType(Image), findsOneWidget);
  });

  testWidgets('KitImage shows a keyed error placeholder for bad bytes', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: KitImage(bytes: Uint8List.fromList([1, 2, 3]), caption: 'Broken'),
      ),
    );
    await _pumpKitImage(tester);

    expect(find.byKey(const Key('kit_image_error')), findsOneWidget);
  });
}
