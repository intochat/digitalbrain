import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:digitalbrain_ui_kit/src/gallery/kit_gallery_preview.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets(
    'data table gallery uses real controller states and refresh recovery',
    (tester) async {
      final entry = galleryEntries.singleWhere(
        (entry) => entry.id == 'data-table',
      );
      await tester.pumpWidget(
        MaterialApp(
          theme: KitTheme.light(),
          home: Scaffold(
            body: SingleChildScrollView(child: GalleryPreview(entry: entry)),
          ),
        ),
      );
      expect(find.byType(KitDataTable), findsOneWidget);
      await tester.tap(find.byKey(const Key('gallery_state_Loading')));
      await tester.pump();
      expect(find.byType(LinearProgressIndicator), findsOneWidget);
      await tester.tap(find.byKey(const Key('gallery_state_Error')));
      await tester.pumpAndSettle();
      expect(find.textContaining('Example: table could not'), findsOneWidget);
      await tester.tap(find.byTooltip('Refresh table'));
      await tester.pumpAndSettle();
      expect(find.textContaining('Example: table could not'), findsNothing);
      await tester.tap(find.byKey(const Key('gallery_state_Empty')));
      await tester.pumpAndSettle();
      expect(find.text('0 of 0 rows'), findsOneWidget);
      expect(tester.takeException(), isNull);
    },
  );
}
