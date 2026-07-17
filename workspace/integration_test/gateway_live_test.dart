import 'dart:convert';
import 'package:flutter_test/flutter_test.dart';
import 'package:workspace/gateway/brain_gateway.dart';
import 'package:workspace/blocks/block_document.dart';

void main() {
  final gateway = BrainGateway(
    httpBase: 'http://localhost:5320',
    wsBase: 'ws://localhost:5320',
  );

  test('invoke chat.post advances revision', () async {
    final id = 'ws-live-${DateTime.now().microsecondsSinceEpoch}';
    final receipt = await gateway.invoke(
      'local-owner|actor/ui-dev|chat/main',
      'chat.post.v1',
      jsonEncode({'text': 'hello from the flutter gateway client'}),
      id,
    );
    expect(receipt['status'], 'accepted');
    expect(receipt['revision'], isNonZero);
  });

  test('render a window and parse its block document', () async {
    final doc = jsonEncode({
      'version': 1,
      'blocks': [
        {'kind': 'metric', 'label': 'Live', 'value': 42},
        {
          'kind': 'timeline',
          'entries': [
            {'kind': 'entry', 'title': 'proof', 'detail': 'rendered from live gateway'},
          ],
        },
      ],
    });
    final id = 'ws-win-${DateTime.now().microsecondsSinceEpoch}';
    await gateway.invoke(
      'local-owner|actor/ui-dev|window/live-proof',
      'window.render.v1',
      doc,
      id,
    );
    final snap = await gateway.read('local-owner|actor/ui-dev|window/live-proof', projection: 'document');
    final parsed = BlockDocument.parse(snap.stateJson);
    expect(parsed.blocks.length, 2);
    expect(parsed.blocks[0].kind, 'metric');
    expect(parsed.blocks[1].kind, 'timeline');
  });

  test('watch delivers a feed frame from the live feed', () async {
    final id = 'ws-watch-${DateTime.now().microsecondsSinceEpoch}';
    await gateway.invoke(
      'local-owner|actor/ui-dev|chat/main',
      'chat.post.v1',
      jsonEncode({'text': 'frame trigger'}),
      id,
    );
    final frame = await gateway
        .watch(cursor: 0, space: 'actor/ui-dev')
        .firstWhere((f) => (f.record['kind'] as String?) == 'chat')
        .timeout(const Duration(seconds: 10));
    expect(frame.record['kind'], 'chat');
  });
}
