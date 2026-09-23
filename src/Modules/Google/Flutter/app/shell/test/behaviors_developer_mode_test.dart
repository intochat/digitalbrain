import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_app.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';

import 'workspace_remote_controller_test.dart' show MemoryPersistence;

void main() {
  Future<void> pumpShell(WidgetTester tester, bool developerMode) async {
    tester.view.physicalSize = const Size(1440, 900);
    tester.view.devicePixelRatio = 1;
    final store = WorkspaceStore(
      persistence: MemoryPersistence(),
      seedProject: false,
    )..developerMode = developerMode;
    await tester.pumpWidget(WorkspaceApp(store: store));
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('Applications'));
    await tester.pumpAndSettle();
  }

  testWidgets('the Behaviors launcher entry is hidden when the server says no', (
    tester,
  ) async {
    await pumpShell(tester, false);
    expect(find.text('Behaviors'), findsNothing);
    expect(find.text('Files'), findsOneWidget);
    await tester.pumpWidget(const SizedBox.shrink());
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });

  testWidgets('the Behaviors launcher entry appears when the server says yes', (
    tester,
  ) async {
    await pumpShell(tester, true);
    expect(find.text('Behaviors'), findsOneWidget);
    await tester.pumpWidget(const SizedBox.shrink());
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });

  test('launching Behaviors is rejected unless developer mode is on', () {
    final store = WorkspaceStore(
      persistence: MemoryPersistence(),
      seedProject: false,
    )..createProject('Personal');
    expect(() => store.launchLocalApp('behaviors'), throwsArgumentError);
    store.developerMode = true;
    expect(store.launchLocalApp('behaviors').id, 'app-behaviors');
  });
}
