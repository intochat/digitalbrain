import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('KitSheet renders title and cells', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: KitSheet(
          part: KitSheetPart(
            title: 'Yesterday',
            sheetName: 'Sheet1',
            columns: ['Item', 'Qty'],
            rows: [
              ['Shoes', '2'],
            ],
          ),
        ),
      ),
    );

    expect(find.byKey(const Key('kit_sheet_Yesterday')), findsOneWidget);
    expect(find.text('Shoes'), findsOneWidget);
    expect(find.text('2'), findsOneWidget);
  });
}
