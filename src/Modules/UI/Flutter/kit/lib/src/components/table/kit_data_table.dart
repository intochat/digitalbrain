import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import 'kit_table_controller.dart';

/// A server-filtered, server-sorted table; no source rows are modified locally.
class KitDataTable extends StatelessWidget {
  const KitDataTable({
    super.key,
    required this.controller,
    this.onActivate,
    this.active = false,
  });
  final KitTableController controller;
  final VoidCallback? onActivate;
  final bool active;

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: controller,
    builder: (context, _) {
      final table = controller.snapshot;
      final columns = table.columns
          .where((c) => table.visibleColumns.contains(c.id))
          .toList();
      final sortIndex = columns.indexWhere((c) => c.id == table.sort?.columnId);
      final canEdit = controller.update != null && !controller.busy;
      final compact = Theme.of(context).visualDensity.vertical < 0;
      void activate() => onActivate?.call();
      return Card(
        key: Key('kit_table_${table.id}'),
        margin: EdgeInsets.zero,
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              Wrap(
                crossAxisAlignment: WrapCrossAlignment.center,
                spacing: 12,
                children: [
                  Text(
                    table.title,
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  if (onActivate != null)
                    TextButton.icon(
                      onPressed: activate,
                      icon: Icon(
                        active
                            ? Icons.check_circle_outline
                            : Icons.chat_bubble_outline,
                        size: 16,
                      ),
                      label: Text(active ? 'Active table' : 'Use in chat'),
                    ),
                ],
              ),
              Text(
                '${table.filteredRows} of ${table.totalRows} rows',
                style: Theme.of(context).textTheme.bodySmall,
              ),
              Wrap(
                spacing: 8,
                crossAxisAlignment: WrapCrossAlignment.center,
                children: [
                  TextButton.icon(
                    onPressed: canEdit && table.filters.length < 64
                        ? () async {
                            activate();
                            final filter = await showDialog<TableFilter>(
                              context: context,
                              builder: (_) =>
                                  _FilterDialog(columns: table.columns),
                            );
                            if (filter != null) {
                              await controller.change(
                                filters: [
                                  ...controller.snapshot.filters,
                                  filter,
                                ],
                              );
                            }
                          }
                        : null,
                    icon: const Icon(Icons.filter_alt_outlined, size: 18),
                    label: const Text('Add filter'),
                  ),
                  TextButton(
                    onPressed: canEdit && table.filters.isNotEmpty
                        ? () {
                            activate();
                            controller.change(filters: []);
                          }
                        : null,
                    child: const Text('Clear filters'),
                  ),
                  TextButton(
                    onPressed: canEdit && table.sort != null
                        ? () {
                            activate();
                            controller.change(replaceSort: true);
                          }
                        : null,
                    child: const Text('Clear sort'),
                  ),
                  TextButton.icon(
                    onPressed: canEdit
                        ? () async {
                            activate();
                            final visible = await showDialog<List<String>>(
                              context: context,
                              builder: (_) => _ColumnsDialog(table: table),
                            );
                            if (visible != null) {
                              await controller.change(visibleColumns: visible);
                            }
                          }
                        : null,
                    icon: const Icon(Icons.view_column_outlined, size: 18),
                    label: const Text('Columns'),
                  ),
                  IconButton(
                    tooltip: 'Refresh table',
                    onPressed: controller.read != null && !controller.busy
                        ? () => controller.reload()
                        : null,
                    icon: const Icon(Icons.refresh),
                  ),
                ],
              ),
              if (table.filters.isNotEmpty)
                Padding(
                  padding: const EdgeInsets.only(bottom: 8),
                  child: Wrap(
                    spacing: 8,
                    runSpacing: 4,
                    children: [
                      for (var i = 0; i < table.filters.length; i++)
                        InputChip(
                          label: Text(_filterLabel(table, table.filters[i])),
                          onDeleted: canEdit
                              ? () {
                                  activate();
                                  controller.change(
                                    filters: [...table.filters]..removeAt(i),
                                  );
                                }
                              : null,
                        ),
                    ],
                  ),
                ),
              if (controller.busy) const LinearProgressIndicator(minHeight: 2),
              if (controller.error case final error?)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 8),
                  child: Text(
                    error,
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.error,
                    ),
                  ),
                ),
              if (columns.isEmpty)
                const Padding(
                  padding: EdgeInsets.all(16),
                  child: Text('Choose a column to display.'),
                )
              else
                LayoutBuilder(
                  builder: (context, available) => ConstrainedBox(
                    constraints: const BoxConstraints(maxHeight: 360),
                    child: SingleChildScrollView(
                      child: SingleChildScrollView(
                        scrollDirection: Axis.horizontal,
                        child: ConstrainedBox(
                          constraints: BoxConstraints(
                            minWidth: available.maxWidth.isFinite
                                ? available.maxWidth
                                : 0,
                          ),
                          child: DataTable(
                            sortColumnIndex: sortIndex < 0 ? null : sortIndex,
                            sortAscending: !(table.sort?.descending ?? false),
                            headingRowHeight: compact ? 36 : 40,
                            dataRowMinHeight: compact ? 36 : 42,
                            dataRowMaxHeight: compact ? 36 : 42,
                            horizontalMargin: 12,
                            columnSpacing: 24,
                            columns: [
                              for (final column in columns)
                                DataColumn(
                                  label: Text(column.label),
                                  numeric: column.type == 'number',
                                  onSort: canEdit
                                      ? (_, ascending) {
                                          activate();
                                          controller.change(
                                            sort: TableSort(
                                              columnId: column.id,
                                              descending: !ascending,
                                            ),
                                            replaceSort: true,
                                          );
                                        }
                                      : null,
                                ),
                            ],
                            rows: [
                              for (final row in table.rows)
                                DataRow(
                                  key: ValueKey(row.id),
                                  cells: [
                                    for (final column in columns)
                                      DataCell(
                                        ConstrainedBox(
                                          constraints: const BoxConstraints(
                                            maxWidth: 280,
                                          ),
                                          child: Text(
                                            _cell(
                                              row.cells[table.columns
                                                  .indexWhere(
                                                    (c) => c.id == column.id,
                                                  )],
                                            ),
                                            maxLines: 1,
                                            overflow: TextOverflow.ellipsis,
                                          ),
                                        ),
                                      ),
                                  ],
                                ),
                            ],
                          ),
                        ),
                      ),
                    ),
                  ),
                ),
              if (table.rows.isEmpty)
                const Padding(
                  padding: EdgeInsets.all(16),
                  child: Text('No rows match these filters.'),
                ),
              Row(
                children: [
                  Expanded(
                    child: Text(
                      table.rows.isEmpty
                          ? '0 rows on this page'
                          : '${table.offset + 1}–${table.offset + table.rows.length} of ${table.filteredRows}',
                    ),
                  ),
                  IconButton(
                    tooltip: 'Previous page',
                    onPressed:
                        controller.read != null &&
                            !controller.busy &&
                            table.offset > 0
                        ? () {
                            activate();
                            controller.reload(
                              offset: (table.offset - table.limit).clamp(
                                0,
                                table.totalRows,
                              ),
                            );
                          }
                        : null,
                    icon: const Icon(Icons.chevron_left),
                  ),
                  IconButton(
                    tooltip: 'Next page',
                    onPressed:
                        controller.read != null &&
                            !controller.busy &&
                            table.offset + table.limit < table.filteredRows
                        ? () {
                            activate();
                            controller.reload(
                              offset: table.offset + table.limit,
                            );
                          }
                        : null,
                    icon: const Icon(Icons.chevron_right),
                  ),
                ],
              ),
            ],
          ),
        ),
      );
    },
  );

  static String _cell(Object? value) => value == null ? '—' : '$value';
  static String _filterLabel(TableSnapshot table, TableFilter filter) {
    final label = table.columns
        .firstWhere((c) => c.id == filter.columnId)
        .label;
    return '$label ${_operatorLabels[filter.operator] ?? filter.operator}${filter.operator == 'isNull' || filter.operator == 'isNotNull' ? '' : ' ${_cell(filter.value)}'}';
  }
}

const _operatorLabels = {
  'eq': 'equals',
  'neq': 'does not equal',
  'contains': 'contains',
  'gt': '>',
  'gte': '≥',
  'lt': '<',
  'lte': '≤',
  'isNull': 'is empty',
  'isNotNull': 'is not empty',
};

class _FilterDialog extends StatefulWidget {
  const _FilterDialog({required this.columns});
  final List<TableColumn> columns;
  @override
  State<_FilterDialog> createState() => _FilterDialogState();
}

class _FilterDialogState extends State<_FilterDialog> {
  late TableColumn _column = widget.columns.first;
  String _operator = 'eq';
  bool _boolean = true;
  String? _error;
  final _value = TextEditingController();
  @override
  void dispose() {
    _value.dispose();
    super.dispose();
  }

  bool get _nullOperator => _operator == 'isNull' || _operator == 'isNotNull';
  void _apply() {
    Object? value;
    if (!_nullOperator) {
      value = _value.text;
      if (_column.type == 'number') {
        final number = _parseFilterNumber(_value.text.trim());
        if (number == null) {
          setState(
            () => _error = 'Use at most 15 significant digits, magnitude ≤ 9007199254740991, and at most 28 decimal places.',
          );
          return;
        }
        value = number;
      } else if (_column.type == 'boolean') {
        value = _boolean;
      } else if (_column.type == 'date') {
        final text = _value.text.trim();
        final date = DateTime.tryParse(text);
        if (!RegExp(r'^\d{4}-\d{2}-\d{2}$').hasMatch(text) ||
            date == null ||
            date.toIso8601String().substring(0, 10) != text) {
          setState(() => _error = 'Enter a valid date as YYYY-MM-DD.');
          return;
        }
        value = text;
      }
    }
    Navigator.pop(
      context,
      TableFilter(columnId: _column.id, operator: _operator, value: value),
    );
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Add filter'),
    content: SizedBox(
      width: 360,
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            DropdownButtonFormField<String>(
              isExpanded: true,
              key: const Key('table_filter_column'),
              initialValue: _column.id,
              decoration: const InputDecoration(labelText: 'Column'),
              items: [
                for (final column in widget.columns)
                  DropdownMenuItem(
                    value: column.id,
                    child: Text(
                      column.label,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                  ),
              ],
              onChanged: (id) => setState(() {
                _column = widget.columns.firstWhere((c) => c.id == id);
                _operator = 'eq';
                _error = null;
                _value.clear();
              }),
            ),
            const SizedBox(height: 16),
            DropdownButtonFormField<String>(
              isExpanded: true,
              key: ValueKey('operator_${_column.id}'),
              initialValue: _operator,
              decoration: const InputDecoration(labelText: 'Operator'),
              items: [
                for (final op in _column.operators)
                  DropdownMenuItem(
                    value: op,
                    child: Text(
                      _operatorLabels[op] ?? op,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                  ),
              ],
              onChanged: (op) => setState(() {
                _operator = op!;
                _error = null;
              }),
            ),
            const SizedBox(height: 16),
            if (!_nullOperator && _column.type == 'boolean')
              DropdownButtonFormField<bool>(
                initialValue: _boolean,
                decoration: const InputDecoration(labelText: 'Value'),
                items: const [
                  DropdownMenuItem(value: true, child: Text('true')),
                  DropdownMenuItem(value: false, child: Text('false')),
                ],
                onChanged: (value) => setState(() => _boolean = value!),
              )
            else if (!_nullOperator)
              TextField(
                key: const Key('table_filter_value'),
                controller: _value,
                keyboardType: _column.type == 'number'
                    ? const TextInputType.numberWithOptions(
                        decimal: true,
                        signed: true,
                      )
                    : _column.type == 'date'
                    ? TextInputType.datetime
                    : TextInputType.text,
                decoration: InputDecoration(
                  labelText: _column.type == 'date'
                      ? 'Value (YYYY-MM-DD)'
                      : 'Value',
                  errorText: _error,
                  errorMaxLines: 4,
                ),
                onSubmitted: (_) => _apply(),
              ),
          ],
        ),
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(onPressed: _apply, child: const Text('Apply')),
    ],
  );
}

// Validate the original decimal spelling before num parsing can round it.
num? _parseFilterNumber(String text) {
  if (!RegExp(r'^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$')
      .hasMatch(text)) {
    return null;
  }
  final parts = text.toLowerCase().split('e');
  final mantissa = parts.first.replaceFirst(RegExp(r'^[+-]'), '');
  final exponent = parts.length == 1 ? 0 : int.tryParse(parts.last);
  if (exponent == null) return null;
  final digits = mantissa.replaceAll('.', '');
  final significant = digits
      .replaceFirst(RegExp(r'^0+'), '')
      .replaceFirst(RegExp(r'0+$'), '');
  if (significant.length > 15) return null;
  final fractionDigits = mantissa.contains('.')
      ? mantissa.length - mantissa.indexOf('.') - 1
      : 0;
  final trailingZeros =
      digits.length - digits.replaceFirst(RegExp(r'0+$'), '').length;
  if (significant.isNotEmpty &&
      fractionDigits - exponent - trailingZeros > 28) {
    return null;
  }
  final number = num.tryParse(text);
  if (number == null ||
      !number.isFinite ||
      number.abs() > 9007199254740991 ||
      (number == 0 && significant.isNotEmpty)) {
    return null;
  }
  return number;
}

class _ColumnsDialog extends StatefulWidget {
  const _ColumnsDialog({required this.table});
  final TableSnapshot table;
  @override
  State<_ColumnsDialog> createState() => _ColumnsDialogState();
}

class _ColumnsDialogState extends State<_ColumnsDialog> {
  late final _visible = widget.table.visibleColumns.toSet();
  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Visible columns'),
    content: SizedBox(
      width: 360,
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            for (final column in widget.table.columns)
              CheckboxListTile(
                title: Text(column.label),
                value: _visible.contains(column.id),
                onChanged: (selected) => setState(() {
                  if (selected!) {
                    _visible.add(column.id);
                  } else {
                    _visible.remove(column.id);
                  }
                }),
              ),
          ],
        ),
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        onPressed: _visible.isEmpty
            ? null
            : () => Navigator.pop(context, [
                for (final c in widget.table.columns)
                  if (_visible.contains(c.id)) c.id,
              ]),
        child: const Text('Apply'),
      ),
    ],
  );
}
