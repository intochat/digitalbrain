import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:workspace/theme/brain_theme.dart';

void main() {
  testWidgets('filledButtonTheme background resolves to a non-indigo surface', (
    tester,
  ) async {
    final resolved = BrainTheme.dark.filledButtonTheme.style?.backgroundColor
        ?.resolve({});

    expect(resolved, isNotNull);
    expect(resolved, isNot(equals(BrainColors.indigo)));
  });

  testWidgets('outlinedButtonTheme foreground resolves to non-indigo ink', (
    tester,
  ) async {
    final resolved = BrainTheme.dark.outlinedButtonTheme.style?.foregroundColor
        ?.resolve({});

    expect(resolved, isNotNull);
    expect(resolved, isNot(equals(BrainColors.indigo)));
  });

  testWidgets('navigationRailTheme indicator is not indigo', (tester) async {
    expect(
      BrainTheme.dark.navigationRailTheme.indicatorColor,
      isNot(equals(BrainColors.indigo)),
    );
  });

  testWidgets('navigationBarTheme indicator is not indigo', (tester) async {
    expect(
      BrainTheme.dark.navigationBarTheme.indicatorColor,
      isNot(equals(BrainColors.indigo)),
    );
  });

  testWidgets('surface action button renders a non-indigo background', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: BrainTheme.dark,
        home: Scaffold(
          body: FilledButton(onPressed: () {}, child: const Text('Approve')),
        ),
      ),
    );

    final material = tester.widget<Material>(
      find
          .ancestor(of: find.text('Approve'), matching: find.byType(Material))
          .first,
    );

    expect(material.color, isNotNull);
    expect(material.color, isNot(equals(BrainColors.indigo)));
    expect(material.color, equals(BrainColors.surfaceRaised));
  });
}
