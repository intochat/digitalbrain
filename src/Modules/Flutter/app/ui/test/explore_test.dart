import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('Explore and creation remain usable at 360px and large text', (
    tester,
  ) async {
    await tester.binding.setSurfaceSize(const Size(360, 780));
    addTearDown(() => tester.binding.setSurfaceSize(null));
    await tester.pumpWidget(
      MaterialApp(
        theme: UiTheme.workspace(Brightness.light),
        builder: (context, child) => MediaQuery(
          data: MediaQuery.of(context)
              .copyWith(textScaler: const TextScaler.linear(1.5)),
          child: child!,
        ),
        home: Scaffold(
          body: Builder(
            builder: (context) => UiExplore(
              firstVisit: true,
              onStart: (s) => showDialog<UiProjectStart>(
                context: context,
                builder: (_) => UiProjectStartDialog(specialist: s),
              ),
            ),
          ),
        ),
      ),
    );
    await tester.ensureVisible(find.text('Automation Builder'));
    await tester.tap(find.text('Automation Builder'));
    await tester.pumpAndSettle();
    expect(find.text('Start project'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
  testWidgets(
    'dock distinguishes same-type artifacts and restores selected item',
    (tester) async {
      String? restored;
      await tester.pumpWidget(
        MaterialApp(
          theme: UiTheme.workspace(Brightness.dark),
          home: Scaffold(
            body: UiArtifactDock(
              items: const [
                UiDockItem(id: 'one', title: 'Prospects', kind: 'table'),
                UiDockItem(id: 'two', title: 'Sources', kind: 'table'),
              ],
              onRestore: (id) => restored = id,
            ),
          ),
        ),
      );
      await tester.tap(find.byKey(const ValueKey('restore-two')));
      expect(restored, 'two');
      expect(find.byTooltip('Restore Prospects'), findsOneWidget);
      expect(find.byIcon(Icons.table_chart_outlined), findsNWidgets(2));
    },
  );
}
