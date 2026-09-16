import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  for (final size in [const Size(1280, 900), const Size(360, 780)]) {
    testWidgets('homepage templates remain actionable at $size', (
      tester,
    ) async {
      await tester.binding.setSurfaceSize(size);
      addTearDown(() => tester.binding.setSurfaceSize(null));
      final selected = <String>[];
      await tester.pumpWidget(
        MaterialApp(
          theme: UiTheme.workspace(
            size.width < 600 ? Brightness.light : Brightness.dark,
          ),
          home: MediaQuery(
            data: MediaQueryData(
              textScaler: TextScaler.linear(size.width < 600 ? 2 : 1),
            ),
            child: Scaffold(
              body: UiProjectLibrary(
                projects: const [],
                onOpen: (_) {},
                onCreate: () {},
                onStartTemplate: (specialist) => selected.add(specialist.id),
              ),
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      final projectHeading = tester.getTopLeft(find.text('Your projects'));
      final exploreHeading = tester.getTopLeft(find.text('Explore'));
      if (size.width >= 960) {
        expect(exploreHeading.dx, greaterThan(projectHeading.dx));
        expect(exploreHeading.dy, projectHeading.dy);
      } else {
        expect(exploreHeading.dy, greaterThan(projectHeading.dy));
      }
      for (final title in [
        'New IntoChat project',
        'Salesforce Admin',
        'Lead Researcher',
        'Automation Builder',
      ]) {
        await tester.ensureVisible(find.text(title));
        await tester.tap(find.text(title));
        await tester.pumpAndSettle();
        expect(tester.takeException(), isNull);
      }
      expect(selected, ['intocaht', 'salesforce', 'leads', 'automation']);
    });
  }

  testWidgets('project search can recover from no results and open a project', (
    tester,
  ) async {
    String? opened;
    await tester.pumpWidget(
      MaterialApp(
        theme: UiTheme.light(),
        home: Scaffold(
          body: UiProjectLibrary(
            projects: List.generate(
              6,
              (i) => UiProjectSummary(
                id: '$i',
                title: 'Project $i',
                conversations: 1,
                savedItems: 2,
              ),
            ),
            onOpen: (id) => opened = id,
            onCreate: () {},
          ),
        ),
      ),
    );
    await tester.enterText(find.byType(TextField), 'missing');
    await tester.pumpAndSettle();
    expect(find.text('No matching projects'), findsOneWidget);
    await tester.tap(find.byTooltip('Clear search'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Project 0'));
    expect(opened, '0');
  });

  testWidgets(
    'cards support keyboard activation and large text in narrow windows',
    (tester) async {
      await tester.binding.setSurfaceSize(const Size(360, 740));
      addTearDown(() => tester.binding.setSurfaceSize(null));
      var opened = false;
      final focus = FocusNode();
      addTearDown(focus.dispose);
      await tester.pumpWidget(
        MaterialApp(
          theme: UiTheme.dark(),
          home: MediaQuery(
            data: const MediaQueryData(textScaler: TextScaler.linear(2)),
            child: Scaffold(
              body: SingleChildScrollView(
                child: UiProjectCard(
                  project: const UiProjectSummary(
                    id: 'a',
                    title: 'A long project name that must remain readable',
                    conversations: 1,
                    savedItems: 0,
                  ),
                  focusNode: focus,
                  onOpen: () => opened = true,
                ),
              ),
            ),
          ),
        ),
      );
      focus.requestFocus();
      await tester.pump();
      await tester.sendKeyEvent(LogicalKeyboardKey.enter);
      await tester.pumpAndSettle();
      expect(opened, isTrue);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets('project naming rejects whitespace without dismissing', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Builder(
          builder: (context) => TextButton(
            onPressed: () => showDialog<String>(
              context: context,
              builder: (_) => const UiProjectNameDialog(),
            ),
            child: const Text('Open'),
          ),
        ),
      ),
    );
    await tester.tap(find.text('Open'));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField), '   ');
    await tester.testTextInput.receiveAction(TextInputAction.done);
    await tester.pumpAndSettle();
    expect(find.byType(UiProjectNameDialog), findsOneWidget);
    expect(find.text('Enter a project name.'), findsOneWidget);
  });
}
