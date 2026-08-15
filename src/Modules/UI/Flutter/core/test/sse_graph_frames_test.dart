import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:test/test.dart';

void main() {
  test('SseGraphChangeParser yields graph-change events and skips others', () {
    final parser = SseGraphChangeParser();
    final events = <GraphChangeEvent>[
      ...parser.addLine('event: graph-change'),
      ...parser.addLine(
        'data: {"sequence":3,"kind":"connected",'
        '"connectionId":"11111111-2222-3333-4444-555555555555",'
        '"source":"chat:dev/main","synapseAlias":"chat.responded",'
        '"target":"chart:dev/dashboard","timestamp":"2026-08-10T08:00:00Z"}',
      ),
      ...parser.addLine(''),
      ...parser.addLine('event: chat-turn'),
      ...parser.addLine('data: {"sequence":4}'),
      ...parser.addLine(''),
      ...parser.flush(),
    ];

    final change = events.single;
    expect(change.kind, 'connected');
    expect(change.sequence, 3);
    expect(change.target, 'chart:dev/dashboard');
  });
}
