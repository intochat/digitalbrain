import 'dart:convert';

import 'ui_surface_models.dart';

class UiSurfacePatcher {
  UiSurfacePatcher._();

  static final RegExp _blockTextPath = RegExp(r'^/blocks/(\d+)/text$');
  static final RegExp _blockKindPath = RegExp(r'^/blocks/(\d+)/kind$');
  static final RegExp _blockIndexPath = RegExp(r'^/blocks/(\d+)$');

  static UiSurface? apply(UiSurface surface, UiSurfacePatch patch) {
    if (patch.surfaceId != surface.surfaceId) {
      return null;
    }
    if (patch.fromRevision != surface.revision) {
      return null;
    }
    if (patch.toRevision <= surface.revision) {
      return null;
    }

    var blocks = List<UiBlock>.from(surface.blocks);
    for (final operation in patch.operations) {
      final next = _applyOperation(blocks, operation);
      if (next == null) {
        return null;
      }
      blocks = next;
    }
    return surface.copyWith(revision: patch.toRevision, blocks: blocks);
  }

  static List<UiBlock>? _applyOperation(
    List<UiBlock> blocks,
    UiPatchOperation operation,
  ) {
    switch (operation.op) {
      case 'replace':
        return _replace(blocks, operation.path, operation.value);
      case 'add':
        return _add(blocks, operation.path, operation.value);
      case 'remove':
        return _remove(blocks, operation.path);
      default:
        return null;
    }
  }

  static List<UiBlock>? _replace(
    List<UiBlock> blocks,
    String path,
    String value,
  ) {
    final textMatch = _blockTextPath.firstMatch(path);
    if (textMatch != null) {
      final index = int.parse(textMatch.group(1)!);
      if (index < 0 || index >= blocks.length) {
        return null;
      }
      final next = List<UiBlock>.from(blocks);
      next[index] = next[index].copyWith(text: value);
      return next;
    }

    final kindMatch = _blockKindPath.firstMatch(path);
    if (kindMatch != null) {
      final index = int.parse(kindMatch.group(1)!);
      if (index < 0 || index >= blocks.length) {
        return null;
      }
      final next = List<UiBlock>.from(blocks);
      next[index] = next[index].copyWith(kind: value);
      return next;
    }
    return null;
  }

  static List<UiBlock>? _add(List<UiBlock> blocks, String path, String value) {
    if (path != '/blocks/-' && !_blockIndexPath.hasMatch(path)) {
      return null;
    }
    try {
      final decoded = jsonDecode(value);
      if (decoded is! Map<String, dynamic>) {
        return null;
      }
      final block = UiBlock.fromJson(decoded);
      final next = List<UiBlock>.from(blocks);
      if (path == '/blocks/-') {
        next.add(block);
        return next;
      }
      final index = int.parse(_blockIndexPath.firstMatch(path)!.group(1)!);
      if (index < 0 || index > next.length) {
        return null;
      }
      next.insert(index, block);
      return next;
    } on FormatException {
      return null;
    }
  }

  static List<UiBlock>? _remove(List<UiBlock> blocks, String path) {
    final match = _blockIndexPath.firstMatch(path);
    if (match == null) {
      return null;
    }
    final index = int.parse(match.group(1)!);
    if (index < 0 || index >= blocks.length) {
      return null;
    }
    final next = List<UiBlock>.from(blocks);
    next.removeAt(index);
    return next;
  }
}
