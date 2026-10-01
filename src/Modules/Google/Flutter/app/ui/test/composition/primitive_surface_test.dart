import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets(
    'full declared primitive tree fits a narrow viewport and nested lists',
    (tester) async {
      tester.view.physicalSize = const Size(360, 600);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final nodes = <String, Map<String, dynamic>>{
        'surface': {
          'children': [
            {'kind': 'layout', 'name': 'main'},
          ],
        },
        'main': {
          'mode': 'column',
          'extents': [64, 0, 40, 32, 100, 48],
          'children': [
            {'kind': 'layout', 'name': 'toolbar'},
            {'kind': 'layout', 'name': 'content'},
            {'kind': 'layout', 'name': 'prompts'},
            {'kind': 'text', 'name': 'status'},
            {'kind': 'textfield', 'name': 'draft'},
            {'kind': 'layout', 'name': 'footer'},
          ],
        },
        'toolbar': {
          'mode': 'row',
          'extents': [0, 100, 0],
          'children': [
            {'kind': 'select', 'name': 'threads'},
            {'kind': 'button', 'name': 'new'},
            {'kind': 'select', 'name': 'models'},
          ],
        },
        'threads': {
          'label': 'Conversation',
          'options': [
            {'id': 'one', 'label': 'Current conversation', 'enabled': true},
          ],
          'selected': 'one',
        },
        'models': {
          'label': 'Model',
          'options': [
            {'id': 'auto', 'label': 'Automatic', 'enabled': true},
          ],
          'selected': 'auto',
        },
        'new': {'label': 'New', 'action': 'new'},
        'content': {
          'mode': 'list',
          'followEnd': true,
          'children': [
            for (final name in ['messages', 'results', 'receipts'])
              {'kind': 'layout', 'name': name},
          ],
        },
        'messages': {
          'mode': 'list',
          'children': [
            for (var i = 0; i < 12; i++) {'kind': 'card', 'name': 'message$i'},
          ],
        },
        'results': {
          'mode': 'list',
          'children': [
            {'kind': 'card', 'name': 'result'},
          ],
        },
        'receipts': {
          'mode': 'list',
          'children': [
            {'kind': 'card', 'name': 'receipt'},
          ],
        },
        'result': {
          'title': 'Saved work',
          'body': 'Result details',
          'children': [
            {'kind': 'button', 'name': 'open'},
          ],
        },
        'receipt': {'title': 'Usage', 'body': '2 Compute', 'children': []},
        'open': {
          'label': 'Open',
          'activation': '{"id":"result","kind":"table"}',
        },
        'prompts': {
          'mode': 'row',
          'children': [
            {'kind': 'button', 'name': 'help'},
            {'kind': 'button', 'name': 'data'},
          ],
        },
        'help': {'label': 'What can you help me with?', 'action': 'help'},
        'data': {'label': 'Show me my data', 'action': 'data'},
        'status': {'markdown': ''},
        'draft': {
          'label': 'Message',
          'kind': 'multiline',
          'value': '',
          'submitButton': 'send',
        },
        'footer': {
          'mode': 'row',
          'children': [
            {'kind': 'fileinput', 'name': 'attach'},
            {'kind': 'voiceinput', 'name': 'voice'},
            {'kind': 'button', 'name': 'send'},
            {'kind': 'button', 'name': 'stop'},
          ],
        },
        'attach': {'label': 'Attach file'},
        'voice': {'label': 'Hold to talk'},
        'send': {'label': 'Send', 'action': 'submit'},
        'stop': {'label': 'Stop', 'action': 'cancel'},
      };
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: NeuronView(
              kind: 'surface',
              name: 'surface',
              load: (_, name) async =>
                  nodes[name] ??
                  {'title': 'Message', 'body': 'Message $name', 'children': []},
              onAction: (_) async {},
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      expect(find.byType(UiVoiceInput), findsOneWidget);
      expect(find.byType(TextField), findsOneWidget);
      expect(find.text('Send'), findsOneWidget);
      final list = tester.widget<SingleChildScrollView>(
        find.byType(SingleChildScrollView).first,
      );
      expect(
        list.controller!.offset,
        list.controller!.position.maxScrollExtent,
      );
      await tester.pumpWidget(const SizedBox());
    },
  );

  testWidgets(
    'only a declared submit target binds Enter and Shift Enter remains an edit',
    (tester) async {
      final events = <Map<String, dynamic>>[];
      var value = '';
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: NeuronView(
              kind: 'textfield',
              name: 'input',
              load: (_, _) async => {
                'kind': 'multiline',
                'value': value,
                'submitButton': 'declared-button',
              },
              onAction: (event) async {
                events.add(event);
                if (event['kind'] == 'textfield') {
                  value = event['value'] as String;
                }
              },
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.enterText(find.byType(TextField), 'First line');
      await tester.sendKeyDownEvent(LogicalKeyboardKey.shiftLeft);
      await tester.sendKeyEvent(LogicalKeyboardKey.enter);
      await tester.sendKeyUpEvent(LogicalKeyboardKey.shiftLeft);
      await tester.pumpAndSettle();
      expect(events.where((event) => event['kind'] == 'button'), isEmpty);
      await tester.sendKeyEvent(LogicalKeyboardKey.enter);
      await tester.pumpAndSettle();
      expect(events.last, {'kind': 'button', 'name': 'declared-button'});
      await tester.pumpWidget(const SizedBox());
    },
  );

  testWidgets('followEnd follows new content until the reader scrolls away', (
    tester,
  ) async {
    var count = 20;
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: SizedBox(
            height: 220,
            child: NeuronView(
              kind: 'layout',
              name: 'list',
              load: (kind, name) async => kind == 'layout'
                  ? {
                      'mode': 'list',
                      'followEnd': true,
                      'children': [
                        for (var i = 0; i < count; i++)
                          {'kind': 'card', 'name': '$i'},
                      ],
                    }
                  : {
                      'title': 'Item $name',
                      'body': 'Some body text',
                      'children': [],
                    },
            ),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    final scroll = tester
        .widget<SingleChildScrollView>(find.byType(SingleChildScrollView))
        .controller!;
    expect(scroll.offset, scroll.position.maxScrollExtent);
    count++;
    await tester.pump(const Duration(seconds: 2));
    await tester.pumpAndSettle();
    expect(scroll.offset, scroll.position.maxScrollExtent);
    await tester.drag(find.byType(SingleChildScrollView), const Offset(0, 180));
    await tester.pumpAndSettle();
    final reading = scroll.offset;
    expect(reading, lessThan(scroll.position.maxScrollExtent));
    count++;
    await tester.pump(const Duration(seconds: 2));
    await tester.pumpAndSettle();
    expect(scroll.offset, reading);
    await tester.pumpWidget(const SizedBox());
  });
}
