import 'dart:convert';

const _allowedKinds = {'text', 'heading', 'list', 'card', 'button', 'status'};
const _maximumDepth = 8;
const _maximumTextLength = 16384;
const _maximumDocumentBytes = 262144;
const _maximumActionInputBytes = 32768;

class Block {
  Block(this.kind, this.raw, this.children);

  final String kind;
  final Map<String, dynamic> raw;
  final List<Block> children;
}

class BlockDocument {
  BlockDocument(this.blocks);

  final List<Block> blocks;

  static BlockDocument parse(String json) {
    if (utf8.encode(json).length > _maximumDocumentBytes) {
      throw const FormatException('UI document is too large');
    }

    final decoded = jsonDecode(json);
    if (decoded is! Map<String, dynamic>) {
      throw const FormatException('UI document must be a JSON object');
    }
    if (decoded['version'] != 1) {
      throw FormatException(
        'unsupported UI document version ${decoded['version']}',
      );
    }
    final rawBlocks = decoded['blocks'];
    if (rawBlocks is! List) {
      throw const FormatException('UI document missing blocks array');
    }

    return BlockDocument(
      rawBlocks.map((entry) => _parseBlock(entry, 1)).toList(),
    );
  }

  static Block _parseBlock(dynamic entry, int depth) {
    if (entry is! Map<String, dynamic>) {
      throw const FormatException('block entry must be a JSON object');
    }
    if (depth > _maximumDepth) {
      throw const FormatException('UI document nesting is too deep');
    }

    final kind = entry['kind'];
    if (kind is! String || !_allowedKinds.contains(kind)) {
      throw FormatException('unsupported UI block kind $kind');
    }

    for (final field in const ['text', 'label', 'value']) {
      final value = entry[field];
      if (value != null &&
          (value is! String || value.length > _maximumTextLength)) {
        throw FormatException('$field must be a bounded string');
      }
    }

    final rawChildren = entry['children'];
    if (rawChildren != null && kind != 'list' && kind != 'card') {
      throw const FormatException('children are allowed only on list and card');
    }
    if (rawChildren != null && rawChildren is! List) {
      throw const FormatException('children must be an array');
    }
    final children = rawChildren is List
        ? rawChildren.map((child) => _parseBlock(child, depth + 1)).toList()
        : <Block>[];

    final action = entry['action'];
    if (kind == 'button' && action is! Map<String, dynamic>) {
      throw const FormatException('button requires an action');
    }
    if (kind != 'button' && action != null) {
      throw const FormatException('actions are allowed only on button blocks');
    }
    if (action is Map<String, dynamic>) {
      _validateAction(action);
    }

    return Block(kind, entry, children);
  }

  static void _validateAction(Map<String, dynamic> action) {
    final contract = action['contract'];
    final target = action['target'];
    final inputJson = action['inputJson'];
    if (contract is! String || contract.isEmpty || contract.length > 256) {
      throw const FormatException('action contract is required and bounded');
    }
    if (target is! String || target.isEmpty || target.length > 512) {
      throw const FormatException('action target is required and bounded');
    }
    if (inputJson is! String ||
        inputJson.isEmpty ||
        utf8.encode(inputJson).length > _maximumActionInputBytes) {
      throw const FormatException('action inputJson is required and bounded');
    }
    final decoded = jsonDecode(inputJson);
    if (decoded is! Map<String, dynamic>) {
      throw const FormatException('action inputJson must contain an object');
    }
  }
}
