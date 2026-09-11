import 'dart:convert';

import 'package:archive/archive.dart';
import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:xml/xml.dart';

typedef CreateWorkspaceTable = Future<TableSnapshot> Function({
  required String title,
  required List<TableColumn> columns,
  required List<TableRowData> rows,
});

class WorkspaceTableImport {
  const WorkspaceTableImport(this.columns, this.rows);
  final List<TableColumn> columns;
  final List<TableRowData> rows;
}

/// Imports the first worksheet's stored values. Formulas must have cached
/// results; dates and custom number formats are retained as stored cell text.
WorkspaceTableImport parseWorkspaceXlsx(List<int> bytes) {
  final zip = ZipDecoder().decodeBytes(bytes);
  if (zip.files.fold<int>(0, (total, file) => total + file.size) >
      50 * 1024 * 1024) {
    throw const FormatException('The expanded workbook exceeds 50 MB.');
  }
  XmlDocument read(String path) {
    final file = zip.find(path);
    if (file == null) throw FormatException('Workbook part is missing: $path');
    return XmlDocument.parse(utf8.decode(file.content));
  }

  final workbook = read('xl/workbook.xml');
  final sheet = workbook.findAllElements('sheet').firstOrNull;
  if (sheet == null) {
    throw const FormatException('The workbook has no worksheets.');
  }
  final relationId = sheet.getAttribute(
    'id',
    namespace:
        'http://schemas.openxmlformats.org/officeDocument/2006/relationships',
  );
  final relations = read('xl/_rels/workbook.xml.rels');
  final relation = relations
      .findAllElements('Relationship')
      .where((element) => element.getAttribute('Id') == relationId)
      .firstOrNull;
  final target = relation?.getAttribute('Target');
  if (target == null) {
    throw const FormatException('The first worksheet is missing.');
  }
  final path = Uri.parse('xl/workbook.xml').resolve(target).path;
  final worksheet = read(path.startsWith('/') ? path.substring(1) : path);
  final strings = zip.find('xl/sharedStrings.xml') == null
      ? <String>[]
      : read('xl/sharedStrings.xml')
            .findAllElements('si')
            .map(
              (entry) => entry
                  .findAllElements('t')
                  .map((text) => text.innerText)
                  .join(),
            )
            .toList();
  final records = <List<String>>[];
  var width = 0;
  for (final row in worksheet.findAllElements('row')) {
    final rowIndex =
        int.tryParse(row.getAttribute('r') ?? '') ?? records.length + 1;
    if (rowIndex > 10001) {
      throw const FormatException('Import supports up to 10,000 rows.');
    }
    while (records.length < rowIndex) {
      records.add([]);
    }
    final values = records[rowIndex - 1];
    for (final cell in row.findElements('c')) {
      final reference = cell.getAttribute('r') ?? '';
      final letters = RegExp(r'^[A-Z]+').stringMatch(reference);
      var column = 0;
      if (letters != null) {
        for (final unit in letters.codeUnits) {
          column = column * 26 + unit - 64;
        }
      } else {
        column = values.length + 1;
      }
      if (column > 100) {
        throw const FormatException('Import supports up to 100 columns.');
      }
      while (values.length < column) {
        values.add('');
      }
      final type = cell.getAttribute('t');
      final value = cell.findElements('v').firstOrNull?.innerText;
      if (cell.findElements('f').isNotEmpty && value == null) {
        throw FormatException(
          'Formula $reference has no stored result. Recalculate and save the workbook first.',
        );
      }
      if (type == 'e') {
        throw FormatException('Cell $reference contains a spreadsheet error.');
      }
      if (type == 's') {
        final index = int.tryParse(value ?? '');
        if (index == null || index < 0 || index >= strings.length) {
          throw FormatException('Cell $reference references invalid text.');
        }
        values[column - 1] = strings[index];
      } else if (type == 'inlineStr') {
        values[column - 1] = cell
            .findAllElements('t')
            .map((text) => text.innerText)
            .join();
      } else {
        values[column - 1] = value ?? '';
      }
      if (column > width) width = column;
    }
  }
  for (final row in records) {
    while (row.length < width) {
      row.add('');
    }
  }
  String quote(String value) => '"${value.replaceAll('"', '""')}"';
  return parseWorkspaceTable(
    records.map((row) => row.map(quote).join(',')).join('\n'),
  );
}

/// RFC-style quoted fields, including embedded newlines and doubled quotes.
/// Values are kept as text so account codes and leading zeroes are not lost.
WorkspaceTableImport parseWorkspaceTable(
  String source, {
  String delimiter = ',',
}) {
  if (source.startsWith('\ufeff')) source = source.substring(1);
  final records = <List<String>>[];
  var row = <String>[];
  var field = StringBuffer();
  var quoted = false;
  var closedQuote = false;
  for (var i = 0; i < source.length; i++) {
    final char = source[i];
    if (quoted) {
      if (char == '"') {
        if (i + 1 < source.length && source[i + 1] == '"') {
          field.write('"');
          i++;
        } else {
          quoted = false;
          closedQuote = true;
        }
      } else {
        field.write(char);
      }
      continue;
    }
    if (char == '"' && field.isEmpty && !closedQuote) {
      quoted = true;
    } else if (char == delimiter) {
      row.add(field.toString());
      field = StringBuffer();
      closedQuote = false;
    } else if (char == '\r' || char == '\n') {
      row.add(field.toString());
      records.add(row);
      row = [];
      field = StringBuffer();
      closedQuote = false;
      if (char == '\r' && i + 1 < source.length && source[i + 1] == '\n') i++;
    } else if (closedQuote) {
      throw const FormatException('Unexpected text after a quoted field.');
    } else {
      field.write(char);
    }
  }
  if (quoted) throw const FormatException('A quoted field is not closed.');
  if (field.isNotEmpty || row.isNotEmpty || closedQuote) {
    row.add(field.toString());
    records.add(row);
  }
  if (records.isEmpty || records.first.every((value) => value.trim().isEmpty)) {
    throw const FormatException('The file must start with a header row.');
  }
  final headers = records.first;
  if (headers.length > 100) {
    throw const FormatException('Import supports up to 100 columns.');
  }
  if (records.length > 10001) {
    throw const FormatException('Import supports up to 10,000 rows.');
  }
  final columns = <TableColumn>[
    for (var i = 0; i < headers.length; i++)
      TableColumn(
        id: 'column_${i + 1}',
        label: headers[i].trim().isEmpty
            ? 'Column ${i + 1}'
            : headers[i].trim(),
        type: 'text',
      ),
  ];
  final rows = <TableRowData>[];
  for (var i = 1; i < records.length; i++) {
    final values = records[i];
    if (values.length != headers.length) {
      throw FormatException(
        'Row ${i + 1} has ${values.length} fields; expected ${headers.length}.',
      );
    }
    rows.add(TableRowData(id: 'row_$i', cells: values));
  }
  return WorkspaceTableImport(columns, rows);
}

Future<TableSnapshot?> importWorkspaceTable(
  BuildContext context,
  CreateWorkspaceTable create,
) async {
  try {
    final selection = await FilePicker.platform.pickFiles(
      type: FileType.custom,
      allowedExtensions: ['csv', 'tsv', 'xlsx'],
      withData: true,
    );
    if (selection == null) return null;
    final file = selection.files.single;
    final bytes = file.bytes;
    if (bytes == null || bytes.isEmpty) {
      throw const FormatException('The selected file is empty.');
    }
    if (bytes.length > 10 * 1024 * 1024) {
      throw const FormatException('Select a file smaller than 10 MB.');
    }
    final extension = file.extension?.toLowerCase();
    final parsed = extension == 'xlsx'
        ? parseWorkspaceXlsx(bytes)
        : parseWorkspaceTable(
            utf8.decode(bytes),
            delimiter: extension == 'tsv' ? '\t' : ',',
          );
    return await create(
      title: file.name.replaceFirst(
        RegExp(r'\.(csv|tsv|xlsx)$', caseSensitive: false),
        '',
      ),
      columns: parsed.columns,
      rows: parsed.rows,
    );
  } catch (error) {
    if (context.mounted) {
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text('Table import failed: $error')));
    }
    return null;
  }
}
