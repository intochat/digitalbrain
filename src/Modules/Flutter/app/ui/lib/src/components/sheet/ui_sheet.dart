import 'package:flutter/material.dart';

import '../../models/ui_part.dart';
import '../../theme/ui_theme.dart';

/// Product spreadsheet control. Same widget for surfaces and chat bubbles.
final class UiSheet extends StatelessWidget {
  const UiSheet({super.key, required this.part, this.maxHeight = 240});

  final UiSheetPart part;
  final double maxHeight;

  @override
  Widget build(BuildContext context) {
    final columns = part.columns;
    final columnCount = columns.isEmpty ? 1 : columns.length;

    return DecoratedBox(
      key: Key('ui_sheet_${part.title}'),
      decoration: BoxDecoration(
        color: UiPalette.surfaceRaised,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: UiPalette.line),
      ),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(12, 12, 12, 8),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(part.title, style: UiType.title),
            const SizedBox(height: 2),
            Text(part.sheetName, style: UiType.meta),
            const SizedBox(height: 10),
            ConstrainedBox(
              constraints: BoxConstraints(maxHeight: maxHeight),
              child: SingleChildScrollView(
                scrollDirection: Axis.horizontal,
                child: SingleChildScrollView(
                  child: Table(
                    defaultColumnWidth: const IntrinsicColumnWidth(),
                    border: TableBorder.all(color: UiPalette.line, width: 1),
                    children: [
                      TableRow(
                        decoration: const BoxDecoration(
                          color: UiPalette.surfaceSunken,
                        ),
                        children: [
                          for (var i = 0; i < columnCount; i++)
                            _SheetCell(
                              text: i < columns.length ? columns[i] : '',
                              header: true,
                            ),
                        ],
                      ),
                      for (final row in part.rows)
                        TableRow(
                          children: [
                            for (var i = 0; i < columnCount; i++)
                              _SheetCell(text: i < row.length ? row[i] : ''),
                          ],
                        ),
                    ],
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

final class _SheetCell extends StatelessWidget {
  const _SheetCell({required this.text, this.header = false});

  final String text;
  final bool header;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
      child: Text(text, style: header ? UiType.metaStrong : UiType.meta),
    );
  }
}
