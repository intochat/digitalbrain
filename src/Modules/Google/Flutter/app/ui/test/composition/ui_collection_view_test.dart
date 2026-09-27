import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  Future<void> show(WidgetTester tester, UiCollectionView view) =>
      tester.pumpWidget(MaterialApp(home: Scaffold(body: view)));

  testWidgets('files retain size, selection, and unsupported semantics', (
    tester,
  ) async {
    final semantics = tester.ensureSemantics();

    String? selected;
    await show(
      tester,
      UiCollectionView(
        items: const [
          {
            'id': 'a',
            'label': 'Notes',
            'kind': 'file',
            'sizeBytes': 2048,
            'canActivate': false,
          },
        ],
        onSelect: (id) => selected = id,
      ),
    );
    expect(find.text('2.0 KB'), findsOneWidget);
    expect(find.text('Preview unavailable'), findsOneWidget);
    expect(find.bySemanticsLabel('Notes, unsupported file'), findsOneWidget);
    await tester.tap(find.byTooltip('Select Notes'));
    expect(selected, 'a');
    semantics.dispose();
  });

  testWidgets('neutral activity activates without file or selection UI', (
    tester,
  ) async {
    final semantics = tester.ensureSemantics();

    Map<String, dynamic>? activated;
    await show(
      tester,
      UiCollectionView(
        fileMode: false,
        selectionEnabled: false,
        items: const [
          {
            'id': 'a',
            'label': 'Research',
            'kind': 'model',
            'secondaryText': 'Completed',
            'trailingText': 'Preview 2',
            'sizeBytes': 2048,
          },
        ],
        onActivate: (item) => activated = item,
      ),
    );
    expect(find.text('Completed'), findsOneWidget);
    expect(find.text('Preview 2'), findsOneWidget);
    expect(find.text('2.0 KB'), findsNothing);
    expect(find.text('Preview unavailable'), findsNothing);
    expect(find.byType(IconButton), findsNothing);
    expect(find.bySemanticsLabel('Open Research'), findsOneWidget);
    await tester.tap(find.text('Research'));
    expect(activated?['id'], 'a');
    semantics.dispose();
  });

  testWidgets('empty and error states support retry', (tester) async {
    var retries = 0;
    await show(
      tester,
      UiCollectionView(
        items: const [],
        fileMode: false,
        emptyLabel: 'No usage',
        error: 'Could not refresh',
        onRetry: () => retries++,
      ),
    );
    expect(find.text('No usage'), findsOneWidget);
    expect(find.text('Could not refresh'), findsOneWidget);
    await tester.tap(find.text('Retry'));
    expect(retries, 1);
  });

  testWidgets(
    'paging retains rows and prevents duplicate requests while loading',
    (tester) async {
      var requests = 0;
      const items = [
        {'id': 'a', 'label': 'Activity'},
      ];
      await show(
        tester,
        UiCollectionView(
          items: items,
          fileMode: false,
          onLoadMore: () => requests++,
        ),
      );
      await tester.tap(find.text('Load more'));
      expect(requests, 1);
      await show(
        tester,
        UiCollectionView(
          items: items,
          fileMode: false,
          onLoadMore: () => requests++,
          loadingMore: true,
          error: 'Refresh failed',
        ),
      );
      expect(find.text('Activity'), findsOneWidget);
      expect(find.text('Refresh failed'), findsOneWidget);
      expect(find.text('Load more'), findsNothing);
      expect(find.byType(CircularProgressIndicator), findsOneWidget);
      expect(requests, 1);
    },
  );
  testWidgets('neutral rows fit narrow panels inside a wide screen', (
    tester,
  ) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: Align(
            child: SizedBox(
              width: 280,
              height: 300,
              child: UiCollectionView(
                fileMode: false,
                selectionEnabled: false,
                items: [
                  {
                    'id': 'a',
                    'label': 'A long activity label that should truncate',
                    'secondaryText': 'A long secondary detail that should wrap',
                    'trailingText': 'Preview 123456.123 Compute',
                  },
                ],
              ),
            ),
          ),
        ),
      ),
    );
    expect(tester.takeException(), isNull);
  });
}
