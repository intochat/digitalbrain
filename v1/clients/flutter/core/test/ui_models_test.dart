import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:test/test.dart';

void main() {
  test('SceneOpenedEvent reads JSON field names used by UI HTTP', () {
    final event = SceneOpenedEvent.fromJson({
      'sequence': 7,
      'sceneKey': 'home',
      'title': 'Home',
      'commandId': 'cmd',
      'shell': 'shell:dev/desk',
    });

    expect(event.sequence, 7);
    expect(event.sceneKey, 'home');
    expect(event.title, 'Home');
    expect(event.commandId, 'cmd');
    expect(event.shell, 'shell:dev/desk');
  });

  test(
    'OpenSceneRequest encodes camelCase for POST /shells/{shell}/scenes',
    () {
      expect(const OpenSceneRequest(sceneKey: 'home', title: 'Home').toJson(), {
        'sceneKey': 'home',
        'title': 'Home',
      });
    },
  );

  test('ActivateControlRequest omits null sceneKey', () {
    expect(const ActivateControlRequest(intent: 'submit').toJson(), {
      'intent': 'submit',
    });
    expect(
      const ActivateControlRequest(intent: 'submit', sceneKey: 'home').toJson(),
      {'intent': 'submit', 'sceneKey': 'home'},
    );
  });

  test('ChatTurnEvent carries durable pulse identity from UI HTTP', () {
    final event = ChatTurnEvent.fromJson({
      'sequence': 9,
      'fromUser': true,
      'text': 'hello',
      'commandId': 'command-9',
      'synapse': 'UserMessaged',
      'neuronId': 'chat:owner/main',
      'caller': 'session:owner/session',
      'correlationId': 'correlation-9',
      'timestamp': '2026-07-28T08:00:00Z',
    });

    expect(event.synapse, 'UserMessaged');
    expect(event.neuronId, 'chat:owner/main');
    expect(event.caller, 'session:owner/session');
    expect(event.correlationId, 'correlation-9');
    expect(event.timestamp, DateTime.utc(2026, 7, 28, 8));
  });

  test('AuthorizationEvent reads UI-HTTP journal projection fields', () {
    final event = AuthorizationEvent.fromJson({
      'sequence': 4,
      'kind': 'AuthorizationRequired',
      'commandId': 'cmd-4',
      'serverKey': 'google.gmail',
      'serverDisplayName': 'DigitalBrain Gmail',
      'signInUrl': 'https://ui.test/oauth?state=s1',
      'state': 's1',
      'timestamp': '2026-07-28T08:00:00Z',
    });

    expect(event.isRequired, isTrue);
    expect(event.serverDisplayName, 'DigitalBrain Gmail');
    expect(event.signInUrl, 'https://ui.test/oauth?state=s1');
    expect(event.state, 's1');
  });

  test('SignInCardProjection opens on required and resolves on completed/denied', () {
    final cards = SignInCardProjection.project([
      AuthorizationEvent(
        sequence: 1,
        kind: 'AuthorizationRequired',
        commandId: 'c1',
        serverKey: 'google.gmail',
        serverDisplayName: 'DigitalBrain Gmail',
        signInUrl: 'https://ui.test/oauth?state=a',
        state: 'a',
        timestamp: DateTime.utc(2026, 7, 28, 8),
      ),
      AuthorizationEvent(
        sequence: 2,
        kind: 'AuthorizationRequired',
        commandId: 'c2',
        serverKey: 'salesforce',
        serverDisplayName: 'DigitalBrain Salesforce',
        signInUrl: 'https://ui.test/oauth?state=b',
        state: 'b',
        timestamp: DateTime.utc(2026, 7, 28, 8, 1),
      ),
      AuthorizationEvent(
        sequence: 3,
        kind: 'AuthorizationCompleted',
        commandId: 'c1',
        serverKey: 'google.gmail',
        state: 'a',
        timestamp: DateTime.utc(2026, 7, 28, 8, 2),
      ),
      AuthorizationEvent(
        sequence: 4,
        kind: 'AuthorizationDenied',
        commandId: 'c2',
        serverKey: 'salesforce',
        state: 'b',
        timestamp: DateTime.utc(2026, 7, 28, 8, 3),
      ),
    ]);

    expect(cards, isEmpty);
  });

  test('SignInCardProjection keeps an open required card until resolved', () {
    final cards = SignInCardProjection.project([
      AuthorizationEvent(
        sequence: 1,
        kind: 'AuthorizationRequired',
        commandId: 'c1',
        serverKey: 'google.gmail',
        serverDisplayName: 'DigitalBrain Gmail',
        signInUrl: 'https://ui.test/oauth?state=a',
        state: 'a',
        timestamp: DateTime.utc(2026, 7, 28, 8),
      ),
    ]);

    expect(cards, hasLength(1));
    expect(cards.single.serverDisplayName, 'DigitalBrain Gmail');
    expect(cards.single.signInUrl.toString(), 'https://ui.test/oauth?state=a');
  });

  test('ChatDelta reads AIContent type-discriminator text frames from the stream', () {
    final delta = ChatDelta.fromJson({
      'role': 'assistant',
      'contents': [
        {r'$type': 'text', 'text': 'Hel'},
        {r'$type': 'text', 'text': 'lo'},
      ],
    });

    expect(delta.role, 'assistant');
    expect(delta.contents, hasLength(2));
    expect(delta.contents.every((part) => part.isText), isTrue);
    expect(delta.text, 'Hello');
  });

  test('ChatDeltaPart keeps unknown \$type as opaque raw fields', () {
    final delta = ChatDelta.fromJson({
      'role': 'assistant',
      'contents': [
        {r'$type': 'text', 'text': 'hi'},
        {
          r'$type': 'data',
          'mediaType': 'image/png',
          'uri': 'data:image/png;base64,abc',
        },
      ],
    });

    expect(delta.contents, hasLength(2));
    expect(delta.contents[0].isText, isTrue);
    expect(delta.contents[1].type, 'data');
    expect(delta.contents[1].isText, isFalse);
    expect(delta.contents[1].raw['mediaType'], 'image/png');
    expect(delta.text, 'hi');
  });

  test('BrainTopologySnapshot carries configured modules and live neurons', () {
    final topology = BrainTopologySnapshot.fromJson({
      'modules': [
        {'id': 'DigitalBrain.Chat.ChatModule'},
      ],
      'neurons': [
        {
          'id': 'chat:owner/main',
          'grainType': 'chat',
          'identity': 'owner/main',
          'placement': 'cluster-1',
        },
      ],
      'observedAt': '2026-07-28T08:00:00Z',
    });

    expect(topology.modules.single.id, 'DigitalBrain.Chat.ChatModule');
    expect(topology.neurons.single.id, 'chat:owner/main');
    expect(topology.neurons.single.grainType, 'chat');
    expect(topology.neurons.single.placement, 'cluster-1');
    expect(topology.observedAt, DateTime.utc(2026, 7, 28, 8));
  });
}
