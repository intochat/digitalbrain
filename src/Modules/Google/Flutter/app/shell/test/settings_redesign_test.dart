import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/model_settings.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_app.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_settings.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

import 'workspace_data_window_test.dart' show WorkspaceClient;
import 'workspace_remote_controller_test.dart' show MemoryPersistence;

const catalog = ChatModelCatalog(
  automatic: ChatModelOption(label: 'Automatic (workspace default)'),
  models: [
    ChatModelOption(
      id: 'profile:fast',
      label: 'Fast',
      provider: 'Provider',
      model: 'model',
      capabilities: ['Tools'],
    ),
  ],
);

void main() {
  test('unavailable model response tells user how to recover', () async {
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://test'),
      httpClient: MockClient(
        (_) async => http.Response('{"code":"MODEL_UNAVAILABLE"}', 400),
      ),
    );
    await expectLater(
      client
          .runAgent(workspaceId: 'w', threadId: 't', runId: 'r', text: 'Hi')
          .toList(),
      throwsA(
        predicate(
          (error) => error.toString().contains(
            'Choose another model beside the composer',
          ),
        ),
      ),
    );
  });

  test(
    'model default applies to new conversations and survives reload',
    () async {
      final persistence = MemoryPersistence();
      final store = WorkspaceStore(persistence: persistence);
      final old = store.currentConversation;
      store.settings.defaultModelProfile = 'profile:fast';
      final next = store.createConversation();
      expect(next.modelProfile, 'profile:fast');
      expect(old.modelProfile, isNull);
      next.modelProfile = null;
      await store.save();
      final restored = WorkspaceStore(persistence: persistence);
      await restored.load();
      expect(restored.settings.defaultModelProfile, 'profile:fast');
      expect(restored.currentConversation.modelProfile, isNull);
      store.dispose();
      restored.dispose();
    },
  );

  test('agent transport includes chosen catalog ID', () async {
    String? sent;
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://test'),
      httpClient: MockClient((request) async {
        sent = (jsonDecode(request.body) as Map)['modelProfile'] as String?;
        return http.Response('data: {"type":"RUN_FINISHED"}\n\n', 200);
      }),
    );
    await client
        .runAgent(
          workspaceId: 'w',
          threadId: 't',
          runId: 'r',
          text: 'Hi',
          modelProfile: 'profile:fast',
        )
        .toList();
    expect(sent, 'profile:fast');
  });

  testWidgets('connected gear opens unified settings and visible UI Kit', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1440, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final store = WorkspaceStore(persistence: MemoryPersistence());
    final api = WorkspaceClient();
    await tester.pumpWidget(
      WorkspaceApp(
        store: store,
        programmingClient: DigitalBrainUiClient(
          baseUri: Uri.parse('http://test'),
          httpClient: api,
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('Settings'));
    await tester.pumpAndSettle();
    expect(find.text('General'), findsWidgets);
    expect(find.text('More settings'), findsNothing);
    expect(find.text('Role or team'), findsNothing);
    await tester.enterText(
      find.widgetWithText(TextField, 'Display name'),
      'Ada',
    );
    await tester.tap(find.text('Save name'));
    await tester.pumpAndSettle();
    expect(store.settings.displayName, 'Ada');
    await tester.tap(find.text('UI Kit'));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('ui_gallery_screen')), findsOneWidget);
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox());
    await api.events.close();
    store.dispose();
  });

  testWidgets('narrow settings switches Models and UI Kit without overflow', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(390, 780);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final store = WorkspaceStore(persistence: MemoryPersistence());
    await tester.pumpWidget(
      MaterialApp(
        home: WorkspaceSettings(
          store: store,
          onClose: () {},
          loadModels: () async => catalog,
        ),
      ),
    );
    await tester.tap(find.byType(DropdownButtonFormField<String>).first);
    await tester.pumpAndSettle();
    await tester.tap(find.text('Models').last);
    await tester.pumpAndSettle();
    await tester.tap(find.text('Fast'));
    await tester.pumpAndSettle();
    expect(store.settings.defaultModelProfile, 'profile:fast');
    await tester.tap(find.byType(DropdownButtonFormField<String>).first);
    await tester.pumpAndSettle();
    await tester.tap(find.text('UI Kit').last);
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('ui_gallery_screen')), findsOneWidget);
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox());
    store.dispose();
  });

  testWidgets('model load retries and preserves unavailable saved choice', (
    tester,
  ) async {
    var attempts = 0;
    String? selected = 'profile:removed';
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: SingleChildScrollView(
            child: ModelSettings(
              selectedId: selected,
              onSelected: (id) => selected = id,
              load: () async {
                if (++attempts == 1) throw StateError('offline');
                return catalog;
              },
            ),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Models could not be loaded.'), findsOneWidget);
    await tester.tap(find.text('Retry'));
    await tester.pumpAndSettle();
    expect(find.textContaining('no longer available'), findsOneWidget);
    expect(selected, 'profile:removed');
    await tester.tap(find.text('Automatic (workspace default)'));
    expect(selected, isNull);
  });

  testWidgets('declared select options send selection to their neuron', (
    tester,
  ) async {
    final events = <Map<String, dynamic>>[];
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: NeuronView(
            kind: 'select',
            name: 'model',
            load: (_, _) async => {
              'label': 'Model',
              'selected': null,
              'options': [
                {'id': 'profile:fast', 'label': 'Fast', 'enabled': true},
              ],
            },
            onAction: (event) async => events.add(event),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('Model'));
    await tester.pumpAndSettle();
    await tester.tap(find.byType(CheckedPopupMenuItem<String>));
    await tester.pumpAndSettle();
    expect(events.single, {
      'kind': 'select',
      'name': 'model',
      'value': 'profile:fast',
    });
    await tester.pumpWidget(const SizedBox());
  });
}
