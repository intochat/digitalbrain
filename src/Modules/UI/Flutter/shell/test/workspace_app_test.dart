import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_app.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_chat.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:digitalbrain_flutter_shell/workspace/artifact_editors.dart';

class MemoryWorkspace implements WorkspacePersistence {
  String? value;
  @override
  Future<String?> read() async => value;
  @override
  Future<void> write(String data) async {
    value = data;
  }
}

void main() {
  testWidgets('focused tabs cap long labels and reveal the active editor', (
    tester,
  ) async {
    await tester.binding.setSurfaceSize(const Size(1268, 900));
    addTearDown(() => tester.binding.setSurfaceSize(null));
    final store = WorkspaceStore(persistence: MemoryWorkspace());
    await store.load();
    for (var i = 0; i < 6; i++) {
      store.addArtifact(
        WorkspaceArtifact(
          id: 'tab$i',
          title: 'A very long artifact title for project document number $i',
          kind: 'document',
        ),
      );
      store.openArtifact('tab$i');
    }
    await tester.pumpWidget(WorkspaceApp(store: store));
    await tester.pumpAndSettle();
    await tester.tap(find.text('My project'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('New conversation').first);
    await tester.pumpAndSettle();
    final last = find.byKey(const ValueKey('artifact-tab-tab5'));
    expect(tester.getRect(last).right, lessThanOrEqualTo(1268));
    expect(tester.getSize(last).width, lessThanOrEqualTo(240));
    store.openArtifact('tab0');
    await tester.pumpAndSettle();
    final first = find.byKey(const ValueKey('artifact-tab-tab0'));
    expect(tester.getRect(first).left, greaterThanOrEqualTo(428));
    expect(tester.getRect(first).right, lessThanOrEqualTo(1268));
    expect(tester.takeException(), isNull);
  });
  testWidgets(
    'live graph selections remain local and never await artifact sync',
    (tester) async {
      final store = WorkspaceStore(persistence: MemoryWorkspace());
      await store.load();
      store.addArtifact(
        WorkspaceArtifact(
          id: 'live',
          title: 'Live brain',
          kind: 'brain',
          data: {'_live': true, 'live': true},
        ),
      );
      store.openArtifact('live');
      var reads = 0, saves = 0;
      await tester.pumpWidget(
        WorkspaceApp(
          store: store,
          onReadBrain: () async {
            reads++;
            return BrainSnapshot(rootId: 'root', observedAt: DateTime.now());
          },
          onUpdateArtifact:
              (
                id, {
                required expectedRevision,
                required title,
                required content,
              }) async {
                saves++;
                return {};
              },
        ),
      );
      await tester.pumpAndSettle();
      expect(reads, 1);
      final observation = BrainSnapshot.fromJson(
        store.currentProject.artifacts.single.data,
      );
      expect(observation.rootId, 'root');
      expect(
        store.currentProject.artifacts.single.data['_observationStale'],
        false,
      );
      await tester.tap(find.text('My project'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Live brain'));
      await tester.pumpAndSettle();
      final editor = tester.widget<WorkspaceArtifactEditor>(
        find.byType(WorkspaceArtifactEditor),
      );
      editor.onChanged(
        editor.artifact.copyWith(editorState: {'selectedId': 'node'}),
      );
      await tester.pump(const Duration(seconds: 1));
      expect(
        store.currentProject.artifacts.single.editorState['selectedId'],
        'node',
      );
      expect(store.currentProject.artifacts.single.data['_dirty'], isNot(true));
      expect(saves, 0);
      expect(find.textContaining('awaiting sync'), findsNothing);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets('floating title drag and resize update durable window bounds', (
    tester,
  ) async {
    await tester.binding.setSurfaceSize(const Size(1600, 1000));
    addTearDown(() => tester.binding.setSurfaceSize(null));
    final store = WorkspaceStore(persistence: MemoryWorkspace());
    await store.load();
    store.addArtifact(
      WorkspaceArtifact(
        id: 'floating',
        title: 'Floating note',
        kind: 'document',
      ),
    );
    store.openArtifact('floating');
    store.setLayout('windows');
    store.setWindowBounds('floating', [20, 20, 650, 500]);
    await tester.pumpWidget(WorkspaceApp(store: store));
    await tester.pumpAndSettle();
    await tester.tap(find.text('My project'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Floating note'));
    await tester.pumpAndSettle();
    final title = find.byKey(const ValueKey('window-drag-floating'));
    await tester.drag(title, const Offset(80, 50));
    await tester.pumpAndSettle();
    expect(
      store.currentProject.presentation.windowBounds['floating']![0],
      greaterThan(70),
    );
    expect(
      store.currentProject.presentation.windowBounds['floating']![1],
      greaterThan(40),
    );
    final before = List<double>.from(
      store.currentProject.presentation.windowBounds['floating']!,
    );
    await tester.drag(
      find.byKey(const ValueKey('window-resize-floating')),
      const Offset(60, 40),
    );
    await tester.pumpAndSettle();
    expect(
      store.currentProject.presentation.windowBounds['floating']![2],
      greaterThan(before[2] + 30),
    );
    expect(
      store.currentProject.presentation.windowBounds['floating']![3],
      greaterThan(before[3] + 10),
    );
    expect(tester.takeException(), isNull);
  });
  testWidgets(
    'restored editors hydrate and old receipts cannot revert the saved table',
    (tester) async {
      final store = WorkspaceStore(persistence: MemoryWorkspace());
      await store.load();
      TableSnapshot snapshot(int revision, String name) => TableSnapshot(
        id: 'table',
        title: 'People',
        revision: revision,
        columns: const [TableColumn(id: 'name', label: 'Name', type: 'text')],
        rows: [
          TableRowData(id: 'row', cells: [name]),
        ],
        filters: [],
        visibleColumns: ['name'],
        totalRows: 1,
        filteredRows: 1,
        offset: 0,
        limit: 50,
      );
      store.addArtifact(
        WorkspaceArtifact(
          id: 'table',
          title: 'People',
          kind: 'table',
          data: snapshot(1, 'Old').toJson(),
        ),
      );
      store.openArtifact('table');
      var reads = 0;
      await tester.pumpWidget(
        WorkspaceApp(
          store: store,
          onReadTable: (id, {offset = 0, limit = 50}) async {
            reads++;
            return snapshot(2, 'Current');
          },
        ),
      );
      await tester.pumpAndSettle();
      expect(reads, 1);
      await tester.tap(find.text('My project'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('People'));
      await tester.pumpAndSettle();
      expect(find.byType(UiDataTable), findsOneWidget);
      final chat = tester.widget<WorkspaceChat>(find.byType(WorkspaceChat));
      chat.onArtifact(snapshot(1, 'Old').toJson());
      await tester.pumpAndSettle();
      expect(store.currentProject.artifacts.single.data['revision'], 2);
      expect(
        store.currentProject.artifacts.single.data['rows'],
        snapshot(2, 'Current').toJson()['rows'],
      );
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets('phone home and workspace fit without horizontal overflow', (
    tester,
  ) async {
    await tester.binding.setSurfaceSize(const Size(390, 844));
    addTearDown(() => tester.binding.setSurfaceSize(null));
    final store = WorkspaceStore(persistence: MemoryWorkspace());
    await tester.pumpWidget(WorkspaceApp(store: store));
    await tester.pumpAndSettle();
    await tester.tap(find.text('My project'));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    await tester.tap(find.widgetWithText(TextButton, 'New conversation'));
    await tester.pumpAndSettle();
    expect(find.text('Conversation'), findsOneWidget);
    expect(tester.takeException(), isNull);
    await tester.tap(find.text('Workspace').first);
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
  });
  testWidgets('navigation keeps an agent stream alive until saved EOF', (
    tester,
  ) async {
    final store = WorkspaceStore(persistence: MemoryWorkspace());
    final events = StreamController<AgentEvent>();
    await tester.pumpWidget(
      WorkspaceApp(
        store: store,
        onRun: ({
          required threadId,
          required runId,
          parentRunId,
          required text,
        }) => events.stream,
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('My project'));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(TextButton, 'New conversation'));
    await tester.pumpAndSettle();
    final conversation = store.currentConversation;
    await tester.enterText(find.byType(TextField), 'Keep running');
    await tester.tap(find.byTooltip('Send message'));
    events.add(
      AgentEvent({'type': 'RUN_STARTED', 'threadId': 't', 'runId': 'saved'}),
    );
    await tester.pump();
    await tester.tap(find.byTooltip('Go to homepage'));
    await tester.pump();
    events.add(AgentEvent({'type': 'RUN_FINISHED'}));
    await events.close();
    await tester.pumpAndSettle();
    await tester.pump(const Duration(seconds: 1));
    expect(conversation.parentRunId, 'saved');
    expect(tester.takeException(), isNull);
  });
  testWidgets('project work opens without attaching and settings is a page', (
    tester,
  ) async {
    await tester.binding.setSurfaceSize(const Size(1300, 900));
    addTearDown(() => tester.binding.setSurfaceSize(null));
    final store = WorkspaceStore(persistence: MemoryWorkspace());
    await store.load();
    store.addArtifact(
      WorkspaceArtifact(id: 'one', title: 'Notes', kind: 'document'),
    );
    await tester.pumpWidget(WorkspaceApp(store: store));
    await tester.pumpAndSettle();
    await tester.tap(find.text('My project'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Notes'));
    await tester.pumpAndSettle();
    expect(store.currentProject.presentation.openArtifactIds, contains('one'));
    expect(store.currentConversation.attachedArtifactIds, isEmpty);
    await tester.tap(find.byTooltip('Attach project work'));
    await tester.pumpAndSettle();
    await tester.tap(find.byType(CheckboxListTile));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Done'));
    await tester.pumpAndSettle();
    expect(store.currentConversation.attachedArtifactIds, contains('one'));
    await tester.tap(find.byTooltip('Settings'));
    await tester.pumpAndSettle();
    expect(find.text('Profile'), findsWidgets);
    await tester.pump(const Duration(seconds: 1));
    expect(tester.takeException(), isNull);
  });
  testWidgets('UiChat continuation waits for EOF and retains transcript', (
    tester,
  ) async {
    final store = WorkspaceStore(persistence: MemoryWorkspace());
    await store.load();
    var events = StreamController<AgentEvent>();
    final parents = <String?>[];
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: WorkspaceChat(
            conversation: store.currentConversation,
            store: store,
            onArtifact: (_) {},
            onAttach: () {},
            onRun:
                ({
                  required threadId,
                  required runId,
                  parentRunId,
                  required text,
                }) {
                  parents.add(parentRunId);
                  return events.stream;
                },
          ),
        ),
      ),
    );
    await tester.enterText(find.byType(TextField), 'Hello');
    await tester.tap(find.byTooltip('Send message'));
    events.add(
      AgentEvent({
        'type': 'RUN_STARTED',
        'threadId': 'thread',
        'runId': 'first',
      }),
    );
    events.add(
      AgentEvent({
        'type': 'TEXT_MESSAGE_CONTENT',
        'messageId': 'message',
        'delta': 'Answer',
      }),
    );
    events.add(AgentEvent({'type': 'RUN_FINISHED'}));
    await tester.pump();
    expect(find.byTooltip('Stop response'), findsOneWidget);
    await events.close();
    await tester.pumpAndSettle();
    expect(store.currentConversation.parentRunId, 'first');
    expect(store.currentConversation.messages.last['text'], 'Answer');
    events = StreamController<AgentEvent>();
    await tester.enterText(find.byType(TextField), 'Continue');
    await tester.tap(find.byTooltip('Send message'));
    expect(parents, [null, 'first']);
    await events.close();
    await tester.pumpAndSettle();
    await tester.pump(const Duration(seconds: 1));
    expect(tester.takeException(), isNull);
  });
}
