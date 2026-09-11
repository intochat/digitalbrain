/// Typed, bounded snapshots of persisted table neurons. Cells remain JSON scalars.
final class TableColumn {
  const TableColumn({
    required this.id,
    required this.label,
    required this.type,
  });
  final String id, label, type;
  factory TableColumn.fromJson(Map<String, dynamic> j) => TableColumn(
    id: j['id'] as String,
    label: j['label'] as String,
    type: j['type'] as String,
  );
  Map<String, Object?> toJson() => {'id': id, 'label': label, 'type': type};
  List<String> get operators => [
    'eq',
    'neq',
    if (type == 'text') 'contains',
    if (type == 'number' || type == 'date') ...['gt', 'gte', 'lt', 'lte'],
    'isNull',
    'isNotNull',
  ];
}

final class TableRowData {
  TableRowData({required this.id, required List<Object?> cells})
    : cells = List.unmodifiable(cells);
  final String id;
  final List<Object?> cells;
  factory TableRowData.fromJson(Map<String, dynamic> j) => TableRowData(
    id: j['id'] as String,
    cells: List<Object?>.from(j['cells'] as List),
  );
  Map<String, Object?> toJson() => {'id': id, 'cells': cells};
}

final class TableFilter {
  const TableFilter({
    required this.columnId,
    required this.operator,
    this.value,
  });
  final String columnId, operator;
  final Object? value;
  factory TableFilter.fromJson(Map<String, dynamic> j) => TableFilter(
    columnId: j['columnId'] as String,
    operator: j['operator'] as String,
    value: j['value'],
  );
  Map<String, Object?> toJson() => {
    'columnId': columnId,
    'operator': operator,
    'value': value,
  };
}

final class TableSort {
  const TableSort({required this.columnId, required this.descending});
  final String columnId;
  final bool descending;
  factory TableSort.fromJson(Map<String, dynamic> j) => TableSort(
    columnId: j['columnId'] as String,
    descending: j['descending'] as bool,
  );
  Map<String, Object?> toJson() => {
    'columnId': columnId,
    'descending': descending,
  };
}

final class TableSummary {
  const TableSummary({
    required this.id,
    required this.title,
    required this.revision,
  });
  final String id, title;
  final int revision;
  factory TableSummary.fromJson(Map<String, dynamic> j) => TableSummary(
    id: j['id'] as String,
    title: j['title'] as String,
    revision: (j['revision'] as num).toInt(),
  );
}

final class TableSnapshot {
  TableSnapshot({
    required this.id,
    required this.title,
    required this.revision,
    required List<TableColumn> columns,
    required List<TableRowData> rows,
    required List<TableFilter> filters,
    this.sort,
    required List<String> visibleColumns,
    required this.totalRows,
    required this.filteredRows,
    required this.offset,
    required this.limit,
  }) : columns = List.unmodifiable(columns),
       rows = List.unmodifiable(rows),
       filters = List.unmodifiable(filters),
       visibleColumns = List.unmodifiable(visibleColumns);
  final String id, title;
  final int revision, totalRows, filteredRows, offset, limit;
  final List<TableColumn> columns;
  final List<TableRowData> rows;
  final List<TableFilter> filters;
  final TableSort? sort;
  final List<String> visibleColumns;
  factory TableSnapshot.fromJson(Map<String, dynamic> j) => TableSnapshot(
    id: j['id'] as String,
    title: j['title'] as String,
    revision: (j['revision'] as num).toInt(),
    columns: (j['columns'] as List)
        .map((x) => TableColumn.fromJson(Map<String, dynamic>.from(x as Map)))
        .toList(),
    rows: (j['rows'] as List)
        .map((x) => TableRowData.fromJson(Map<String, dynamic>.from(x as Map)))
        .toList(),
    filters: (j['filters'] as List)
        .map((x) => TableFilter.fromJson(Map<String, dynamic>.from(x as Map)))
        .toList(),
    sort: j['sort'] == null
        ? null
        : TableSort.fromJson(Map<String, dynamic>.from(j['sort'] as Map)),
    visibleColumns: List<String>.from(j['visibleColumns'] as List),
    totalRows: (j['totalRows'] as num).toInt(),
    filteredRows: (j['filteredRows'] as num).toInt(),
    offset: (j['offset'] as num).toInt(),
    limit: (j['limit'] as num).toInt(),
  );
  Map<String, Object?> toJson() => {
    'kind': 'table',
    'id': id,
    'title': title,
    'revision': revision,
    'columns': columns.map((x) => x.toJson()).toList(),
    'rows': rows.map((x) => x.toJson()).toList(),
    'filters': filters.map((x) => x.toJson()).toList(),
    'sort': sort?.toJson(),
    'visibleColumns': visibleColumns,
    'totalRows': totalRows,
    'filteredRows': filteredRows,
    'offset': offset,
    'limit': limit,
  };
}

final class TableViewUpdate {
  const TableViewUpdate({
    required this.expectedRevision,
    required this.filters,
    required this.sort,
    required this.visibleColumns,
  });
  final int expectedRevision;
  final List<TableFilter> filters;
  final TableSort? sort;
  final List<String> visibleColumns;
  Map<String, Object?> toJson() => {
    'expectedRevision': expectedRevision,
    'filters': filters.map((x) => x.toJson()).toList(),
    'sort': sort?.toJson(),
    'visibleColumns': visibleColumns,
  };
}

final class TableRequestException implements Exception {
  const TableRequestException(this.statusCode, this.message);
  final int statusCode;
  final String message;
  @override
  String toString() => 'Table request failed ($statusCode): $message';
}

typedef ReadTable = Future<TableSnapshot> Function(
  String id, {
  int offset,
  int limit,
});
typedef UpdateTableView = Future<TableSnapshot> Function(
  String id,
  TableViewUpdate update,
);
typedef ListTables = Future<List<TableSummary>> Function();
