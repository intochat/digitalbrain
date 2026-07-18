import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/screens/shell/shell_screen.dart';
import 'package:ino_flutter/screens/shell/shell_theme.dart';
import 'package:ino_flutter/screens/shell/shell_topbar.dart';
import 'package:ino_flutter/state/brain_inspector_bloc.dart';

// ShellBrainCanvas uses three_js which requires a native GL context
// (libEGL.dll via flutter_angle FFI) unavailable in flutter_test on desktop.
// We inject a stub canvas to isolate ShellScreen's scaffold structure tests
// from the rendering pipeline — the same pattern used for /brain (no screen
// widget tests there).
Widget _wrap(Widget child) => BlocProvider(
      create: (_) => BrainInspectorBloc(),
      child: MaterialApp(home: child),
    );

// PersonaWidget requires PersonaBloc + TimelineBloc which are not in scope
// for ShellScreen's structural tests. We stub both via builder injection
// to keep tests focused on scaffold/stack shape.
Widget _stubPersona(BuildContext _) => const SizedBox.shrink();
Widget _stubTimeline(BuildContext _) => const SizedBox.shrink();

void main() {
  testWidgets('ShellScreen renders an ink-0 background', (tester) async {
    await tester.pumpWidget(
      _wrap(ShellScreen(
        canvas: const SizedBox.shrink(),
        topbarPersonaBuilder: _stubPersona,
        timelineBuilder: _stubTimeline,
        runnerEnabled: false,
      )),
    );
    final scaffold = tester.widget<Scaffold>(find.byType(Scaffold));
    expect(scaffold.backgroundColor, InoShellTheme.ink0);
  });

  testWidgets('ShellScreen has a Stack as its top-level body', (tester) async {
    await tester.pumpWidget(
      _wrap(ShellScreen(
        canvas: const SizedBox.shrink(),
        topbarPersonaBuilder: _stubPersona,
        timelineBuilder: _stubTimeline,
        runnerEnabled: false,
      )),
    );
    expect(find.byType(Stack), findsAtLeastNWidgets(1));
  });

  testWidgets('ShellTopbar is present in the screen Stack', (tester) async {
    await tester.pumpWidget(
      _wrap(ShellScreen(
        canvas: const SizedBox.shrink(),
        topbarPersonaBuilder: _stubPersona,
        timelineBuilder: _stubTimeline,
        runnerEnabled: false,
      )),
    );
    expect(find.byType(ShellTopbar), findsOneWidget);
  });
}
