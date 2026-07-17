import 'dart:convert';
import 'package:flutter_test/flutter_test.dart';
import 'package:workspace/gateway/brain_gateway.dart';
import 'package:workspace/blocks/block_document.dart';

void main() {
  final gateway = BrainGateway(
    httpBase: 'http://localhost:5320',
    wsBase: 'ws://localhost:5320',
  );

  test('invoke window.render advances revision', () async {
    final id = 'ws-live-${DateTime.now().microsecondsSinceEpoch}';
    final receipt = await gateway.invoke(
      'local-owner|actor/ui-dev|window/live-invoke',
      'window.render.v1',
      jsonEncode({
        'version': 1,
        'blocks': [
          {'kind': 'text', 'text': 'hello from the flutter gateway client'},
        ],
      }),
      id,
    );
    expect(receipt['status'], 'accepted');
    expect(receipt['revision'], isNonZero);
  });

  test('render a window and parse its block document', () async {
    final doc = jsonEncode({
      'version': 1,
      'blocks': [
        {'kind': 'heading', 'text': 'Live'},
        {'kind': 'text', 'text': 'rendered from live gateway'},
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
    expect(parsed.blocks[0].kind, 'heading');
    expect(parsed.blocks[1].kind, 'text');
  });

  test('watch delivers a feed frame from the live feed', () async {
    final id = 'ws-watch-${DateTime.now().microsecondsSinceEpoch}';
    await gateway.invoke(
      'local-owner|actor/ui-dev|feed/main',
      'feed.append.v1',
      jsonEncode({
        'sourceKey': 'local-owner|actor/ui-dev|window/live-proof',
        'revision': 1,
        'kind': 'window',
      }),
      id,
    );
    final frame = await gateway
        .watch(cursor: 0)
        .firstWhere((f) => (f.record['kind'] as String?) == 'window')
        .timeout(const Duration(seconds: 10));
    expect(frame.record['kind'], 'window');
  });
}
