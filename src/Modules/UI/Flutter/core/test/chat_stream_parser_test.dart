import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:test/test.dart';

const accepted =
    '{"sequence":4,"fromUser":true,"text":"hello","commandId":"c-1","signal":"TurnAccepted","neuronId":"uichat:desk","correlationId":"r-1","timestamp":"2026-09-10T12:00:00Z","turnId":"t-1","eventId":"e-1"}';

void main() {
  test('TurnAccepted carries the user turn without a status', () {
    final events = [
      'event: chat-turn',
      'data: $accepted',
      '',
    ].expand(SseChatTurnParser().addLine).toList();
    expect(events, hasLength(1));
    final turn = (events.single as ChatTurnObserved).turn;
    expect(turn.sequence, 4);
    expect(turn.fromUser, isTrue);
    expect(turn.text, 'hello');
    expect(turn.turnId, 't-1');
    expect(turn.commandId, 'c-1');
    expect(turn.signal, 'TurnAccepted');
    expect(turn.status, isNull);
    expect(turn.neuronId, 'uichat:desk');
    expect(turn.correlationId, 'r-1');
    expect(turn.eventId, 'e-1');
    expect(turn.timestamp, DateTime.utc(2026, 9, 10, 12));
  });

  test('TurnFailed preserves Cancelled status', () {
    final events = [
      'event: chat-turn',
      'data: {"sequence":5,"fromUser":false,"text":"cancelled by the user","commandId":"c-1","signal":"TurnFailed","neuronId":"uichat:desk","correlationId":"r-1","timestamp":"2026-09-10T12:00:02Z","turnId":"t-1","status":"Cancelled","eventId":"e-2"}',
      '',
    ].expand(SseChatTurnParser().addLine).toList();
    final turn = (events.single as ChatTurnObserved).turn;
    expect(turn.signal, 'TurnFailed');
    expect(turn.fromUser, isFalse);
    expect(turn.text, 'cancelled by the user');
    expect(turn.status, 'Cancelled');
  });

  test('ChatTurns reset replays completed and cancelled exchanges', () {
    final events = [
      'event: reset',
      'data: {"cursor":12,"state":{"turns":[{"turn":{"value":"11111111-1111-1111-1111-111111111111"},"commandId":{"value":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"},"text":"hello","status":"Completed","startedAt":"2026-09-10T12:00:00Z","settledAt":"2026-09-10T12:00:05Z","answer":"hi there","cards":[{"kind":"chart","name":"sales","caption":"Q3"}]},{"turn":{"value":"22222222-2222-2222-2222-222222222222"},"commandId":{"value":"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"},"text":"stop","status":"Cancelled","startedAt":"2026-09-10T12:01:00Z","settledAt":"2026-09-10T12:01:02Z","detail":"cancelled by the user"}]}}',
      '',
    ].expand(SseChatTurnParser().addLine).toList();
    final reset = events.single as ChatJournalReset;
    expect(reset.cursor, 12);
    expect(reset.turns, hasLength(4));
    expect(reset.turns.map((turn) => turn.sequence), [-4, -3, -2, -1]);
    expect(reset.turns.map((turn) => turn.signal), [
      'TurnAccepted',
      'Responded',
      'TurnAccepted',
      'TurnFailed',
    ]);
    final user = reset.turns[0];
    expect(user.fromUser, isTrue);
    expect(user.text, 'hello');
    expect(user.turnId, '11111111-1111-1111-1111-111111111111');
    expect(user.commandId, 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa');
    expect(user.timestamp, DateTime.utc(2026, 9, 10, 12));
    final answer = reset.turns[1];
    expect(answer.fromUser, isFalse);
    expect(answer.text, 'hi there');
    expect(answer.timestamp, DateTime.utc(2026, 9, 10, 12, 0, 5));
    expect(answer.cards, hasLength(1));
    expect(answer.cards.single.kind, 'chart');
    expect(answer.cards.single.name, 'sales');
    expect(answer.cards.single.caption, 'Q3');
    expect(reset.turns[2].fromUser, isTrue);
    expect(reset.turns[2].text, 'stop');
    final failure = reset.turns[3];
    expect(failure.fromUser, isFalse);
    expect(failure.text, 'cancelled by the user');
    expect(failure.status, 'Cancelled');
    expect(failure.timestamp, DateTime.utc(2026, 9, 10, 12, 1, 2));
  });

  test('Running snapshot contributes only the user event', () {
    final events = [
      'event: reset',
      'data: {"cursor":13,"state":{"turns":[{"turn":{"value":"t-1"},"commandId":{"value":"c-1"},"text":"hello","status":"Running","startedAt":"2026-09-10T12:00:00Z"}]}}',
      '',
    ].expand(SseChatTurnParser().addLine).toList();
    final turn = (events.single as ChatJournalReset).turns.single;
    expect(turn.sequence, -1);
    expect(turn.fromUser, isTrue);
    expect(turn.signal, 'TurnAccepted');
    expect(turn.text, 'hello');
  });

  test('keepalive does not create a chat event', () {
    expect(
      ['event: keepalive', 'data: {}', ''].expand(SseChatTurnParser().addLine),
      isEmpty,
    );
  });

  test('surface frame preserves the delivery envelope and body', () {
    final events = [
      'event: surface',
      'data: {"sequence":3,"signal":"ComponentAdded","body":{"surfaceKey":"home"}}',
      '',
    ].expand(SseSurfaceEventParser().addLine).toList();
    final event = events.single as SurfaceSignalObserved;
    expect(event.sequence, 3);
    expect(event.signal, 'ComponentAdded');
    expect(event.body, {'surfaceKey': 'home'});
  });

  test('surface reset carries only the cursor', () {
    final events = [
      'event: reset',
      'data: {"cursor":9,"state":{"scenes":[{"surfaceKey":"home"}]}}',
      '',
    ].expand(SseSurfaceEventParser().addLine).toList();
    expect((events.single as SurfaceStreamReset).cursor, 9);
  });

  test('unknown surface event names are ignored before decoding', () {
    expect(
      [
        'event: component',
        'data: not json',
        '',
      ].expand(SseSurfaceEventParser().addLine),
      isEmpty,
    );
  });

  test('shared buffer ignores comments and emits once on blank or flush', () {
    for (final parser in <SseFrameParser<Object>>[
      SseChatTurnParser(),
      SseSurfaceEventParser(),
    ]) {
      final chat = parser is SseChatTurnParser;
      final name = chat ? 'chat-turn' : 'reset';
      final data = chat ? accepted : '{"cursor":9,"state":{}}';
      expect(
        [
          'event: $name',
          ': comment',
          'data: $data',
          ': another comment',
        ].expand(parser.addLine),
        isEmpty,
      );
      expect(parser.flush(), hasLength(1));
      expect(parser.flush(), isEmpty);
      expect(
        ['event: $name', 'data: $data', ''].expand(parser.addLine),
        hasLength(1),
      );
      expect(parser.flush(), isEmpty);
      expect(['data: $data', ''].expand(parser.addLine), isEmpty);
    }
  });

  for (final payload in ['not json', '{"cursor":"wrong","state":{}}']) {
    test('malformed reset $payload is ignored only by surface parser', () {
      final lines = ['event: reset', 'data: $payload', ''];
      expect(lines.expand(SseSurfaceEventParser().addLine), isEmpty);
      expect(
        () => lines.expand(SseChatTurnParser().addLine).toList(),
        throwsA(
          payload == 'not json' ? isA<FormatException>() : isA<TypeError>(),
        ),
      );
    });
  }
}
