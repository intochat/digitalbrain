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
      children: doc.blocks
          .map((block) => _renderBlock(context, block))
          .toList(),
    );
  }

  Widget _renderBlock(BuildContext context, Block block) {
    try {
      switch (block.kind) {
        case 'section':
          return _section(context, block);
        case 'columns':
          return _columns(context, block);
        case 'text':
          return _text(block);
        case 'metric':
          return _metric(context, block);
        case 'field':
          return _field(block);
        case 'list':
          return _list(block);
        case 'table':
          return _table(block);
        case 'timeline':
          return _timeline(context, block);
        case 'entry':
          return _entry(block);
        case 'media':
          return _media(block);
        case 'progress':
          return _progress(block);
        case 'actionRow':
          return _actionRow(block);
        default:
          return _fallback(block.kind);
      }
    } catch (_) {
      return _fallback(block.kind);
    }
  }

  Block _asBlock(dynamic entry) {
    final map = entry is Map<String, dynamic> ? entry : <String, dynamic>{};
    final kind = map['kind'];
    return Block(kind is String ? kind : '', map);
  }

  Widget _childColumn(BuildContext context, dynamic rawChildren) {
    final children = rawChildren is List ? rawChildren : const <dynamic>[];
    return Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.start,
      children: children
          .map((entry) => _renderBlock(context, _asBlock(entry)))
          .toList(),
    );
  }

  Widget _section(BuildContext context, Block block) {
    final title = block.raw['title']?.toString() ?? '';
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 8),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(title, style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 8),
          _childColumn(context, block.raw['children']),
        ],
      ),
    );
  }

  Widget _columns(BuildContext context, Block block) {
    final rawChildren = block.raw['children'];
    final children = rawChildren is List ? rawChildren : const <dynamic>[];
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: children
          .map(
            (entry) => Expanded(child: _renderBlock(context, _asBlock(entry))),
          )
          .toList(),
    );
  }

  Widget _text(Block block) {
    return Text(block.raw['value']?.toString() ?? '');
  }

  Widget _metric(BuildContext context, Block block) {
    final label = block.raw['label']?.toString() ?? '';
    final value = block.raw['value']?.toString() ?? '';
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: Theme.of(context).textTheme.labelSmall),
          Text(
            value,
            style: const TextStyle(
              fontFamily: 'monospace',
              fontFeatures: [FontFeature.tabularFigures()],
              fontSize: 20,
              fontWeight: FontWeight.w600,
            ),
          ),
        ],
      ),
    );
  }

  Widget _field(Block block) {
    final label = block.raw['label']?.toString() ?? '';
    final value = block.raw['value']?.toString() ?? '';
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 2),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('$label: ', style: const TextStyle(fontWeight: FontWeight.w600)),
          Flexible(child: Text(value)),
        ],
      ),
    );
  }

  Widget _list(Block block) {
    final rawItems = block.raw['items'];
    final items = rawItems is List ? rawItems : const <dynamic>[];
    return Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.start,
      children: items.map((item) => Text('• ${item.toString()}')).toList(),
    );
  }

  Widget _table(Block block) {
    final rawColumns = block.raw['columns'];
    final columnNames = rawColumns is List
        ? rawColumns.map((entry) => entry.toString()).toList()
        : <String>[];
    if (columnNames.isEmpty) {
      return _fallback('table');
    }
    final rawRows = block.raw['rows'];
    final rows = rawRows is List ? rawRows : const <dynamic>[];
    final dataRows = rows.map((row) {
      final cells = row is List
          ? row.map((cell) => cell.toString()).toList()
          : <String>[];
      return DataRow(
        cells: List<DataCell>.generate(
          columnNames.length,
          (index) => DataCell(Text(index < cells.length ? cells[index] : '')),
        ),
      );
    }).toList();
    return SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      child: DataTable(
        columns: columnNames
            .map((name) => DataColumn(label: Text(name)))
            .toList(),
        rows: dataRows,
      ),
    );
  }

  Widget _timeline(BuildContext context, Block block) {
    return _childColumn(context, block.raw['entries']);
  }

  Widget _entry(Block block) {
    final title = block.raw['title']?.toString() ?? '';
    final detail = block.raw['detail']?.toString() ?? '';
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 6),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Padding(
            padding: EdgeInsets.only(top: 6, right: 8),
            child: SizedBox(
              width: 8,
              height: 8,
              child: DecoratedBox(
                decoration: BoxDecoration(shape: BoxShape.circle),
              ),
            ),
          ),
          Expanded(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  title,
                  style: const TextStyle(fontWeight: FontWeight.bold),
                ),
                Text(detail),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _media(Block block) {
    final url = block.raw['url']?.toString() ?? '';
    final alt = block.raw['alt']?.toString() ?? '';
    return ConstrainedBox(
      constraints: const BoxConstraints(maxWidth: 480),
      child: Image.network(
        url,
        fit: BoxFit.contain,
        errorBuilder: (context, error, stackTrace) => Text(alt),
      ),
    );
  }

  Widget _progress(Block block) {
    final label = block.raw['label']?.toString() ?? '';
    final rawFraction = block.raw['fraction'];
    final fraction = rawFraction is num ? rawFraction.toDouble() : 0.0;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label),
          const SizedBox(height: 4),
          LinearProgressIndicator(value: fraction.clamp(0.0, 1.0)),
        ],
      ),
    );
  }

  Widget _actionRow(Block block) {
    final rawActions = block.raw['actions'];
    final actions = rawActions is List ? rawActions : const <dynamic>[];
    return Wrap(
      spacing: 8,
      runSpacing: 8,
      children: actions.map((entry) {
        final action = entry is Map<String, dynamic>
            ? BlockAction.fromJson(entry)
            : const BlockAction(label: '', contract: '', inputJson: '');
        return FilledButton(
          onPressed: () => onAction?.call(action),
          child: Text(action.label),
        );
      }).toList(),
    );
  }

  Widget _fallback(String kind) {
    return Container(
      margin: const EdgeInsets.symmetric(vertical: 4),
      padding: const EdgeInsets.all(8),
      decoration: BoxDecoration(border: Border.all(color: Colors.grey)),
      child: Text('unsupported block: $kind'),
    );
  }
}
