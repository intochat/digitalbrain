import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:workspace/blocks/block_document.dart';

void main() {
  group('BlockDocument.parse', () {
    test('parses the canonical v1 fixture', () async {
      final fixture = await File(
        'test/fixtures/ui_document_v1/basic.json',
      ).readAsString();

      final document = BlockDocument.parse(fixture);

      expect(document.blocks, hasLength(4));
      expect(document.blocks.first.kind, 'heading');
      expect(document.blocks[1].children, hasLength(2));
    });

    test('rejects an unsupported kind', () {
      expect(
        () => BlockDocument.parse(
          '{"version":1,"blocks":[{"kind":"unknown","text":"x"}]}',
        ),
        throwsFormatException,
      );
    });

    test('rejects an unsupported version', () {
      expect(
        () => BlockDocument.parse('{"version":2,"blocks":[]}'),
        throwsFormatException,
      );
    });

    test('rejects excessive nesting', () {
      dynamic block = {'kind': 'text', 'text': 'leaf'};
      for (var depth = 0; depth < 9; depth++) {
        block = {
          'kind': 'card',
          'children': [block],
        };
      }

      expect(
        () => BlockDocument.parse(
          jsonEncode({
            'version': 1,
            'blocks': [block],
          }),
        ),
        throwsFormatException,
      );
    });

    test('rejects oversized text', () {
      expect(
        () => BlockDocument.parse(
          jsonEncode({
            'version': 1,
            'blocks': [
              {'kind': 'text', 'text': 'x' * 16385},
            ],
          }),
        ),
        throwsFormatException,
      );
    });

    test('rejects malformed action input JSON', () {
      expect(
        () => BlockDocument.parse(
          jsonEncode({
            'version': 1,
            'blocks': [
              {
                'kind': 'button',
                'label': 'Approve',
                'action': {
                  'contract': 'effect.approve.v1',
                  'target': 'owner|actor/ui|effect/1',
                  'inputJson': '{',
                },
              },
            ],
          }),
        ),
        throwsFormatException,
      );
    });
  });
}
