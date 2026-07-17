import 'package:flutter/material.dart';

import 'block_action.dart';
import 'block_document.dart';

class BlockView extends StatelessWidget {
  const BlockView(this.doc, {super.key, this.onAction});

  final BlockDocument doc;
  final void Function(BlockAction)? onAction;

  @override
  Widget build(BuildContext context) {
    return Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.start,
      children: doc.blocks.map((block) => _render(context, block)).toList(),
    );
  }

  Widget _render(BuildContext context, Block block) {
    switch (block.kind) {
      case 'text':
        return Text(block.raw['text'] as String? ?? '');
      case 'heading':
        return _heading(context, block);
      case 'list':
        return _list(context, block);
      case 'card':
        return _card(context, block);
      case 'button':
        return _button(block);
      case 'status':
        return _status(block);
      default:
        throw StateError('validated block kind was not renderable');
    }
  }

  Widget _heading(BuildContext context, Block block) {
    return Text(
      block.raw['text'] as String? ?? '',
      style: Theme.of(context).textTheme.titleLarge,
    );
  }

  Widget _list(BuildContext context, Block block) {
    return Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.start,
      children: block.children
          .map(
            (child) => child.kind == 'text'
                ? Text('• ${child.raw['text'] as String? ?? ''}')
                : _render(context, child),
          )
          .toList(),
    );
  }

  Widget _card(BuildContext context, Block block) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: block.children
              .map((child) => _render(context, child))
              .toList(),
        ),
      ),
    );
  }

  Widget _button(Block block) {
    final action = block.raw['action'] as Map<String, dynamic>;
    final label = block.raw['label'] as String? ?? '';
    return FilledButton(
      onPressed: () => onAction?.call(
        BlockAction(
          label: label,
          contract: action['contract'] as String,
          target: action['target'] as String,
          inputJson: action['inputJson'] as String,
        ),
      ),
      child: Text(label),
    );
  }

  Widget _status(Block block) {
    final label = block.raw['label'] as String? ?? '';
    final value = block.raw['value'] as String? ?? '';
    return Text('$label: $value');
  }
}
