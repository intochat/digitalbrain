import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';

class UiKitTable extends StatelessWidget {
  const UiKitTable({super.key, required this.columns, required this.rows});

  final List<String> columns;
  final List<List<String>> rows;

  @override
  Widget build(BuildContext context) {
    final t = FTheme.of(context);

    Widget cell(String text, {bool header = false}) => Expanded(
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 6),
        child: Text(
          text,
          overflow: TextOverflow.ellipsis,
          style: header
              ? t.typography.sm.copyWith(fontWeight: FontWeight.w600)
              : t.typography.sm,
        ),
      ),
    );

    return FCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        mainAxisSize: MainAxisSize.min,
        children: [
          Row(children: columns.map((c) => cell(c, header: true)).toList()),
          const FDivider(),
          for (final row in rows)
            Row(children: [for (final value in row) cell(value)]),
        ],
      ),
    );
  }
}
