import 'dart:convert';

class Block {
  Block(this.kind, this.raw);

  final String kind;
  final Map<String, dynamic> raw;
}

class BlockDocument {
  BlockDocument(this.blocks);

  final List<Block> blocks;

  static BlockDocument parse(String json) {
    final decoded = jsonDecode(json);
    if (decoded is! Map<String, dynamic>) {
      throw const FormatException('block document must be a JSON object');
    }
    if (decoded['version'] != 1) {
      throw FormatException(
        'unsupported block document version ${decoded['version']}',
      );
    }
    final rawBlocks = decoded['blocks'];
    if (rawBlocks is! List) {
      throw const FormatException('block document missing blocks array');
    }
    final blocks = rawBlocks.map((entry) {
      if (entry is! Map<String, dynamic>) {
        throw const FormatException('block entry must be a JSON object');
      }
      final kind = entry['kind'];
      if (kind is! String) {
        throw const FormatException('block entry missing kind');
      }
      return Block(kind, entry);
    }).toList();
    return BlockDocument(blocks);
  }
}
