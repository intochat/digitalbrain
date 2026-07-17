import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:workspace/blocks/block_document.dart';

void main() {
  group('BlockDocument.parse', () {
    test('parses a metric and timeline block', () {
      final document = BlockDocument.parse(
        jsonEncode({
          'version': 1,
          'blocks': [
            {'kind': 'metric', 'label': 'A', 'value': 1},
            {'kind': 'timeline', 'entries': <dynamic>[]},
          ],
        }),
      );

      expect(document.blocks, hasLength(2));
      expect(document.blocks[0].kind, 'metric');
      expect(document.blocks[0].raw['label'], 'A');
      expect(document.blocks[1].kind, 'timeline');
    });

    test('throws FormatException for an unsupported version', () {
      final json = jsonEncode({'version': 2, 'blocks': <dynamic>[]});

      expect(() => BlockDocument.parse(json), throwsFormatException);
    });

    test('throws FormatException for garbage input', () {
      expect(() => BlockDocument.parse('not json'), throwsFormatException);
    });

    test('throws FormatException for a non-object top level shape', () {
      expect(
        () => BlockDocument.parse(jsonEncode([1, 2, 3])),
        throwsFormatException,
      );
    });

    test('throws FormatException when blocks is missing', () {
      final json = jsonEncode({'version': 1});

      expect(() => BlockDocument.parse(json), throwsFormatException);
    });

    test('throws FormatException for a non-map block entry', () {
      final json = jsonEncode({
        'version': 1,
        'blocks': ['not a block'],
      });

      expect(() => BlockDocument.parse(json), throwsFormatException);
    });

    test('throws FormatException for a block missing kind', () {
      final json = jsonEncode({
        'version': 1,
        'blocks': [
          {'label': 'no kind here'},
        ],
      });

      expect(() => BlockDocument.parse(json), throwsFormatException);
    });
  });
}
