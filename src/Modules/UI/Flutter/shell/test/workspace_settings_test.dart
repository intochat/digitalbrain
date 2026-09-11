import 'package:digitalbrain_flutter_shell/workspace/workspace_settings.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

class _Storage implements WorkspacePersistence {
  String? value;
  @override
  Future<String?> read() async => value;
  @override
  Future<void> write(String value) async {
    this.value = value;
  }
}

void main() {
  testWidgets('profile save persists edited preferences', (tester) async {
    final disk = _Storage();
    final store = WorkspaceStore(persistence: disk);
    await store.load();
    await tester.pumpWidget(
      MaterialApp(
        home: WorkspaceSettings(store: store, onClose: () {}),
      ),
    );
    await tester.enterText(find.byType(TextField).first, 'Alex');
    await tester.enterText(find.byType(TextField).last, 'Operations');
    await tester.tap(find.text('Save profile'));
    await tester.pumpAndSettle();
    final restored = WorkspaceStore(persistence: disk);
    await restored.load();
    expect(restored.settings.displayName, 'Alex');
    expect(restored.settings.role, 'Operations');
  });

  testWidgets('developer tools opens actual gallery route', (tester) async {
    final store = WorkspaceStore(persistence: _Storage());
    await tester.pumpWidget(
      MaterialApp(
        home: WorkspaceSettings(
          store: store,
          initialSection: 'developer',
          onClose: () {},
        ),
      ),
    );
    await tester.tap(find.text('UI components gallery'));
    await tester.pumpAndSettle();
    expect(find.byType(UiGalleryScreen), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('mobile settings fits viewport', (tester) async {
    tester.view.physicalSize = const Size(390, 844);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final store = WorkspaceStore(persistence: _Storage());
    await tester.pumpWidget(
      MaterialApp(
        home: WorkspaceSettings(
          store: store,
          initialSection: 'agents',
          onClose: () {},
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Salesforce Admin'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
