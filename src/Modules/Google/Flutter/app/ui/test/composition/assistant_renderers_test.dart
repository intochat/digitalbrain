import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  Map<String, dynamic> chatState(
    int revision,
    List<Map<String, Object>> messages,
  ) => {
    'name': 'ws/applications/assistant/chat',
    'revision': revision,
    'label': 'Message the assistant',
    'draft': '',
    'messages': messages,
  };

  Future<Map<String, dynamic>> Function(String, String) loader(
    Map<String, dynamic> Function() chat,
  ) =>
      (kind, name) async => switch (kind) {
        'surface' => {
          'definition': {
            'children': [
              {'kind': 'layout', 'name': 'ws/applications/assistant/layout'},
            ],
          },
        },
        'layout' => {
          'definition': {
            'mode': 'column',
            'children': [
              {'kind': 'uichat', 'name': 'ws/applications/assistant/chat'},
              {'kind': 'voiceinput', 'name': 'ws/applications/assistant/voice'},
            ],
          },
        },
        'uichat' => chat(),
        _ => {
          'name': name,
          'revision': 1,
          'label': 'Hold to talk',
          'captures': 0,
        },
      };

  testWidgets('uichat shows the neuron messages and submits what is sent', (
    tester,
  ) async {
    final events = <Map<String, dynamic>>[];
    var chat = chatState(1, [
      {'id': 'a', 'role': 'User', 'text': 'hi'},
      {'id': 'b', 'role': 'Assistant', 'text': 'Hello there'},
    ]);
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: NeuronView(
            kind: 'surface',
            name: 'ws/applications/assistant/surface',
            load: loader(() => chat),
            onAction: (event) async {
              events.add(event);
              chat = chatState(2, [
                ...(chat['messages'] as List).cast(),
                {'id': 'c', 'role': 'User', 'text': 'how are you'},
                {'id': 'd', 'role': 'Assistant', 'text': 'Fine, thanks'},
              ]);
            },
            voiceBuilder: (name, state) => Text('voice ${state['label']}'),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('hi'), findsOneWidget);
    expect(find.text('Hello there'), findsOneWidget);

    await tester.enterText(find.byType(TextField), 'how are you');
    await tester.testTextInput.receiveAction(TextInputAction.send);
    await tester.pumpAndSettle();

    expect(events, [
      {
        'kind': 'uichat',
        'name': 'ws/applications/assistant/chat',
        'value': 'how are you',
      },
    ]);
    expect(find.text('Fine, thanks'), findsOneWidget);
    await tester.pumpWidget(const SizedBox());
    await tester.pump(const Duration(seconds: 1));
  });

  testWidgets('voiceinput renders through the host voice builder', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: NeuronView(
            kind: 'surface',
            name: 'ws/applications/assistant/surface',
            load: loader(() => chatState(1, [])),
            voiceBuilder: (name, state) => Text('$name ${state['label']}'),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(
      find.text('ws/applications/assistant/voice Hold to talk'),
      findsOneWidget,
    );
    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('voiceinput without a voice builder names its kind', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: NeuronView(
            kind: 'voiceinput',
            name: 'ws/applications/assistant/voice',
            load: loader(() => chatState(1, [])),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.textContaining('voiceinput'), findsOneWidget);
  });
}
