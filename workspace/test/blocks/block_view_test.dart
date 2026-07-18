import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:workspace/blocks/block_document.dart';
import 'package:workspace/blocks/block_view.dart';

void main() {
  testWidgets('canonical v1 fixture renders every supported composite', (
    tester,
  ) async {
    final fixture = File(
      'test/fixtures/ui_document_v1/basic.json',
    ).readAsStringSync();
    final document = BlockDocument.parse(fixture);

    await tester.pumpWidget(
      MaterialApp(home: Scaffold(body: BlockView(document))),
    );

    expect(find.text('Inbox summary'), findsOneWidget);
    expect(find.text('Two messages need attention.'), findsOneWidget);
    expect(find.text('• Approve the draft'), findsOneWidget);
    expect(find.text('• Review the sender'), findsOneWidget);
    expect(find.text('Connection: ready'), findsOneWidget);
    expect(find.text('Approve'), findsOneWidget);
    expect(find.byType(Card), findsOneWidget);
  });

  testWidgets('text block renders its text', (tester) async {
    final document = BlockDocument.parse(
      '{"version":1,"blocks":[{"kind":"text","text":"Hello world"}]}',
    );

    await tester.pumpWidget(
      MaterialApp(home: Scaffold(body: BlockView(document))),
    );

    expect(find.text('Hello world'), findsOneWidget);
  });
}
