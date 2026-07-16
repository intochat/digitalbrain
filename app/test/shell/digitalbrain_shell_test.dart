import 'dart:ui' show Tristate;

import 'package:digitalbrain_flutter/digital_brain_ui/digital_brain_ui.dart';
import 'package:digitalbrain_flutter/shell/digitalbrain_shell.dart';
import 'package:digitalbrain_flutter/shell/main_destination.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('compact width uses a drawer with Chat and Activity', (
    tester,
  ) async {
    final semantics = tester.ensureSemantics();
    await _pumpShell(tester, width: 599);

    expect(find.byType(NavigationRail), findsNothing);
    expect(find.byKey(digitalBrainOpenNavigationKey), findsOneWidget);
    await tester.tap(find.byKey(digitalBrainOpenNavigationKey));
    await tester.pumpAndSettle();
    expect(find.byType(NavigationDrawer), findsOneWidget);
    _expectProductDestinations();
    final chat = tester.getSemantics(
      find.descendant(
        of: find.byType(NavigationDrawer),
        matching: find.text('Chat'),
      ),
    );
    expect(chat.flagsCollection.isSelected, Tristate.isTrue);
    semantics.dispose();
  });

  testWidgets('medium widths use a collapsed accessible rail', (tester) async {
    for (final width in <double>[600, 1199]) {
      await _pumpShell(tester, width: width);

      final rail = tester.widget<NavigationRail>(find.byType(NavigationRail));
      expect(rail.extended, isFalse);
      expect(rail.selectedIndex, 0);
      expect(find.byType(NavigationDrawer), findsNothing);
      _expectProductDestinations();
    }
  });

  testWidgets('desktop width uses an extended labeled rail', (tester) async {
    final semantics = tester.ensureSemantics();
    await _pumpShell(tester, width: 1200);

    final rail = tester.widget<NavigationRail>(find.byType(NavigationRail));
    expect(rail.extended, isTrue);
    expect(rail.selectedIndex, 0);
    _expectProductDestinations();
    expect(
      tester.getSemantics(find.byTooltip('Chat')).flagsCollection.isSelected,
      Tristate.isTrue,
    );
    semantics.dispose();
  });

  testWidgets('Studio context is unselected and has one global sign out', (
    tester,
  ) async {
    var signOutCalls = 0;
    await _pumpShell(
      tester,
      width: 1200,
      location: Uri.parse(
        '/features/proposals/proposal-0123456789abcdef0123456789abcdef',
      ),
      onSignOut: () => signOutCalls++,
    );

    expect(
      find.descendant(
        of: find.byKey(digitalBrainCurrentContextKey),
        matching: find.text('Feature Studio'),
      ),
      findsOneWidget,
    );
    expect(
      tester.widget<NavigationRail>(find.byType(NavigationRail)).selectedIndex,
      isNull,
    );
    expect(find.byKey(digitalBrainSignOutButtonKey), findsOneWidget);
    expect(find.text('Home'), findsNothing);
    expect(find.text('Features'), findsWidgets);
    expect(find.text('Connections'), findsNothing);
    expect(find.text('Activity'), findsWidgets);
    expect(find.text('Memory'), findsNothing);

    await tester.tap(find.byKey(digitalBrainSignOutButtonKey));
    expect(signOutCalls, 1);
  });

  testWidgets('selected Chat and global sign out are accessible by keyboard', (
    tester,
  ) async {
    final semantics = tester.ensureSemantics();
    MainDestination? selected;
    await _pumpShell(
      tester,
      width: 736,
      location: Uri.parse('/chat'),
      onDestinationSelected: (destination) => selected = destination,
    );

    final context = tester.getSemantics(
      find.byKey(digitalBrainCurrentContextKey),
    );
    expect(context.label, 'Chat');
    expect(context.flagsCollection.isHeader, isTrue);
    expect(context.flagsCollection.isLiveRegion, isTrue);
    expect(find.byTooltip('Chat'), findsOneWidget);
    final chat = tester.getSemantics(find.byTooltip('Chat'));
    expect(chat.flagsCollection.isSelected, Tristate.isTrue);
    final signOutFinder = find.bySemanticsLabel('Sign out');
    expect(signOutFinder, findsOneWidget);
    final signOut = tester.getSemantics(signOutFinder);
    expect(signOut.label, 'Sign out');
    expect(signOut.flagsCollection.isButton, isTrue);

    await tester.sendKeyEvent(LogicalKeyboardKey.tab);
    await tester.pump();
    await tester.sendKeyEvent(LogicalKeyboardKey.enter);
    await tester.pump();

    expect(selected, MainDestination.chat);
    semantics.dispose();
  });

  testWidgets('Run detail keeps Activity selected and announces its context', (
    tester,
  ) async {
    await _pumpShell(
      tester,
      width: 1200,
      location: Uri.parse('/activity/run-a'),
    );

    expect(
      tester.widget<NavigationRail>(find.byType(NavigationRail)).selectedIndex,
      MainDestination.values.indexOf(MainDestination.activity),
    );
    expect(
      find.descendant(
        of: find.byKey(digitalBrainCurrentContextKey),
        matching: find.text('Run details'),
      ),
      findsOneWidget,
    );
  });

  testWidgets('global sign out is keyboard usable in every shell mode', (
    tester,
  ) async {
    for (final width in <double>[599, 736, 1200]) {
      var signOutCalls = 0;
      await _pumpShell(tester, width: width, onSignOut: () => signOutCalls++);

      for (var attempt = 0; attempt < 10; attempt++) {
        await tester.sendKeyEvent(LogicalKeyboardKey.tab);
        await tester.pump();
        if (_signOutHasPrimaryFocus(tester)) break;
      }
      expect(_signOutHasPrimaryFocus(tester), isTrue, reason: 'width $width');

      await tester.sendKeyEvent(LogicalKeyboardKey.enter);
      await tester.pump();
      expect(signOutCalls, 1, reason: 'width $width');

      await tester.pumpWidget(const SizedBox.shrink());
      await tester.pump();
    }
  });
}

Future<void> _pumpShell(
  WidgetTester tester, {
  required double width,
  Uri? location,
  VoidCallback? onSignOut,
  ValueChanged<MainDestination>? onDestinationSelected,
}) async {
  tester.view.devicePixelRatio = 1;
  tester.view.physicalSize = Size(width, 900);
  addTearDown(tester.view.resetDevicePixelRatio);
  addTearDown(tester.view.resetPhysicalSize);
  await tester.pumpWidget(
    MaterialApp(
      theme: ThemeData.dark(useMaterial3: true),
      home: WindowSizeScope(
        child: DigitalBrainShell(
          location: location ?? Uri.parse('/chat'),
          onDestinationSelected: onDestinationSelected ?? (_) {},
          onSignOut: onSignOut ?? () {},
          child: const ColoredBox(
            color: Color(0xff101114),
            child: Center(child: Text('Trusted canvas')),
          ),
        ),
      ),
    ),
  );
  await tester.pump();
}

void _expectProductDestinations() {
  expect(find.text('Chat'), findsWidgets);
  expect(find.text('Home'), findsNothing);
  expect(find.text('Features'), findsWidgets);
  expect(find.text('Connections'), findsNothing);
  expect(find.text('Activity'), findsWidgets);
  expect(find.text('Memory'), findsNothing);
}

bool _signOutHasPrimaryFocus(WidgetTester tester) {
  final focusContext = FocusManager.instance.primaryFocus?.context;
  if (focusContext == null) return false;
  final signOut = tester.element(find.byKey(digitalBrainSignOutButtonKey));
  if (identical(focusContext, signOut)) return true;
  var found = false;
  focusContext.visitAncestorElements((ancestor) {
    if (!identical(ancestor, signOut)) return true;
    found = true;
    return false;
  });
  return found;
}
