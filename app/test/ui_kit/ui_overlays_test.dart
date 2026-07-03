import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_dialog.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_sheet.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_toast.dart';

Widget _forUiHost(Widget child) => MaterialApp(
      home: FToaster(
        child: FTheme(
          data: FThemes.neutral.light.touch,
          child: FScaffold(child: child),
        ),
      ),
    );

void main() {
  testWidgets('Dialog with open:true presents its title', (tester) async {
    await tester.pumpWidget(
      _forUiHost(UiKitDialog(open: true, title: 'Confirm', children: const [Text('Sure?')])),
    );
    await tester.pumpAndSettle();
    expect(find.text('Confirm'), findsOneWidget);
    expect(find.text('Sure?'), findsOneWidget);
  });

  testWidgets('Dialog with open:false presents nothing', (tester) async {
    await tester.pumpWidget(
      _forUiHost(UiKitDialog(open: false, title: 'Confirm', children: const [Text('Sure?')])),
    );
    await tester.pumpAndSettle();
    expect(find.text('Confirm'), findsNothing);
    expect(find.text('Sure?'), findsNothing);
  });

  testWidgets('Dialog does not re-present on rebuild (present-once guard)', (tester) async {
    await tester.pumpWidget(_forUiHost(const _RebuildHarness()));
    await tester.pumpAndSettle();
    // Dialog is visible after first build.
    expect(find.text('Confirm'), findsOneWidget);

    // Force a rebuild via direct setState on the harness state — avoids any
    // tap interaction that could hit the dialog's barrier and dismiss it.
    tester.state<_RebuildHarnessState>(find.byType(_RebuildHarness)).forceRebuild();
    await tester.pumpAndSettle();

    // Still exactly one dialog — _presented flag blocked a second present().
    expect(find.text('Confirm'), findsOneWidget);
  });

  testWidgets('Toast presents its message once', (tester) async {
    await tester.pumpWidget(_forUiHost(const UiKitToast(message: 'Saved')));
    await tester.pumpAndSettle();
    expect(find.text('Saved'), findsOneWidget);
  });

  testWidgets('Sheet presents its title', (tester) async {
    await tester.pumpWidget(
      _forUiHost(UiKitSheet(open: true, title: 'Options', children: const [Text('A')])),
    );
    await tester.pumpAndSettle();
    expect(find.text('Options'), findsOneWidget);
    expect(find.text('A'), findsOneWidget);
  });
}

// A small StatefulWidget that renders UiKitDialog(open:true) alongside a
// button.  Tapping the button calls setState to force a rebuild of the dialog
// widget so we can verify the present-once guard blocks a second presentation.
class _RebuildHarness extends StatefulWidget {
  const _RebuildHarness();

  @override
  State<_RebuildHarness> createState() => _RebuildHarnessState();
}

class _RebuildHarnessState extends State<_RebuildHarness> {
  int _counter = 0;

  // Called directly by the test — no UI interaction needed, avoids hitting
  // the dialog barrier.
  void forceRebuild() => setState(() => _counter++);

  @override
  Widget build(BuildContext context) =>
      // No key change on UiKitDialog: preserves the State object (and _presented
      // flag) across rebuilds so the guard is exercised, not bypassed.
      UiKitDialog(open: true, title: 'Confirm', children: const [Text('Sure?')]);
}
