import 'dart:ui' show ImageFilter;

import 'package:flutter/material.dart';

import 'shell_theme.dart';

/// A single row inside a ShellCard. The four sealed variants mirror the
/// prototype's CARDS data shape (docs/ino-design/src/data.js lines 77-127).
sealed class ShellCardRow {
  const ShellCardRow({this.dim = false, this.highlight = false});
  final bool dim;
  final bool highlight;
}

class FlightRow extends ShellCardRow {
  const FlightRow({
    required this.code,
    required this.route,
    required this.duration,
    required this.price,
    this.tag,
    super.dim,
    super.highlight,
  });
  final String code;
  final String route;
  final String duration;
  final String price;
  final String? tag;
}

class HotelRow extends ShellCardRow {
  const HotelRow({
    required this.name,
    required this.area,
    required this.note,
    required this.price,
    this.tag,
    super.dim,
    super.highlight,
  });
  final String name;
  final String area;
  final String note;
  final String price;
  final String? tag;
}

class DayRow extends ShellCardRow {
  const DayRow({
    required this.day,
    required this.weather,
    required this.plan,
    super.highlight,
  });
  final String day;
  final String weather;
  final String plan;
}

class ReminderRow extends ShellCardRow {
  const ReminderRow({
    required this.name,
    required this.when,
    this.tag,
  });
  final String name;
  final String when;
  final String? tag;
}

class ShellCardModel {
  const ShellCardModel({
    required this.id,
    required this.title,
    required this.subtitle,
    required this.cluster,
    required this.rows,
  });

  final String id;
  final String title;
  final String subtitle;
  final String cluster;
  final List<ShellCardRow> rows;
}

class ShellCard extends StatelessWidget {
  const ShellCard({
    required this.model,
    this.onReplayTrace,
    this.highlight = false,
    super.key,
  });

  final ShellCardModel model;
  final VoidCallback? onReplayTrace;

  /// True briefly during a morph (T6.3) — paints a gold border flash.
  final bool highlight;

  @override
  Widget build(BuildContext context) {
    return ClipRRect(
      borderRadius: BorderRadius.circular(16),
      child: BackdropFilter(
        filter: ImageFilter.blur(
          sigmaX: InoShellTheme.glassBlurSigmaStrong,
          sigmaY: InoShellTheme.glassBlurSigmaStrong,
        ),
        child: AnimatedContainer(
          duration: const Duration(milliseconds: 240),
          curve: InoShellTheme.easeOut,
          padding: const EdgeInsets.fromLTRB(16, 14, 16, 14),
          decoration: BoxDecoration(
            color: InoShellTheme.glassFillStrong,
            border: Border.all(
              color: highlight ? InoShellTheme.gold : InoShellTheme.lineStrong,
              width: highlight ? 2 : 1,
            ),
            borderRadius: BorderRadius.circular(16),
            boxShadow: highlight
                ? [
                    BoxShadow(
                      color: InoShellTheme.gold.withValues(alpha: 0.18),
                      blurRadius: 14,
                      spreadRadius: 6,
                    )
                  ]
                : null,
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              _CardHeader(model: model, onReplayTrace: onReplayTrace),
              const SizedBox(height: 10),
              for (final row in model.rows) ...[
                _CardRow(row: row),
                const SizedBox(height: 6),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class _CardHeader extends StatelessWidget {
  const _CardHeader({required this.model, required this.onReplayTrace});
  final ShellCardModel model;
  final VoidCallback? onReplayTrace;

  @override
  Widget build(BuildContext context) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                model.cluster.toUpperCase(),
                style: const TextStyle(
                  fontFamily: 'JetBrains Mono',
                  fontSize: 10,
                  letterSpacing: 1.6,
                  color: InoShellTheme.muted2,
                ),
              ),
              const SizedBox(height: 4),
              Text(
                model.title,
                style: const TextStyle(
                  fontSize: 16,
                  fontWeight: FontWeight.w600,
                  letterSpacing: -0.2,
                  color: InoShellTheme.text,
                ),
              ),
              const SizedBox(height: 2),
              Text(
                model.subtitle,
                style: const TextStyle(
                  fontSize: 12,
                  color: InoShellTheme.muted,
                ),
              ),
            ],
          ),
        ),
        if (onReplayTrace != null)
          IconButton(
            tooltip: 'Replay trace',
            iconSize: 16,
            visualDensity: VisualDensity.compact,
            color: InoShellTheme.muted,
            onPressed: onReplayTrace,
            icon: const Icon(Icons.chevron_right),
          ),
      ],
    );
  }
}

class _CardRow extends StatelessWidget {
  const _CardRow({required this.row});
  final ShellCardRow row;

  @override
  Widget build(BuildContext context) {
    final opacity = row.dim ? 0.45 : 1.0;
    return Opacity(
      opacity: opacity,
      child: switch (row) {
        FlightRow r => _FlightRowBody(row: r),
        HotelRow r => _HotelRowBody(row: r),
        DayRow r => _DayRowBody(row: r),
        ReminderRow r => _ReminderRowBody(row: r),
      },
    );
  }
}

class _FlightRowBody extends StatelessWidget {
  const _FlightRowBody({required this.row});
  final FlightRow row;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        SizedBox(
          width: 70,
          child: Text(
            row.code,
            style: const TextStyle(
              fontFamily: 'JetBrains Mono',
              fontSize: 12,
              color: InoShellTheme.text,
            ),
          ),
        ),
        Expanded(
          child: Text(
            row.route,
            style: const TextStyle(fontSize: 12, color: InoShellTheme.text),
          ),
        ),
        const SizedBox(width: 12),
        Text(
          row.duration,
          style: const TextStyle(
            fontFamily: 'JetBrains Mono',
            fontSize: 12,
            color: InoShellTheme.muted,
          ),
        ),
        const SizedBox(width: 12),
        SizedBox(
          width: 60,
          child: Text(
            row.price,
            style: const TextStyle(
              fontFamily: 'JetBrains Mono',
              fontSize: 12,
              color: InoShellTheme.text,
            ),
          ),
        ),
        if (row.tag != null) _TagPill(label: row.tag!, highlight: row.highlight),
      ],
    );
  }
}

class _HotelRowBody extends StatelessWidget {
  const _HotelRowBody({required this.row});
  final HotelRow row;

  @override
  Widget build(BuildContext context) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                row.name,
                style: TextStyle(
                  fontSize: 13,
                  color: InoShellTheme.text,
                  fontWeight:
                      row.highlight ? FontWeight.w600 : FontWeight.w400,
                ),
              ),
              Text(
                '${row.area} · ${row.note}',
                style: const TextStyle(
                  fontSize: 11,
                  color: InoShellTheme.muted,
                ),
              ),
            ],
          ),
        ),
        const SizedBox(width: 8),
        SizedBox(
          width: 80,
          child: Text(
            row.price,
            textAlign: TextAlign.right,
            style: const TextStyle(
              fontFamily: 'JetBrains Mono',
              fontSize: 12,
              color: InoShellTheme.text,
            ),
          ),
        ),
        if (row.tag != null) _TagPill(label: row.tag!, highlight: row.highlight),
      ],
    );
  }
}

class _DayRowBody extends StatelessWidget {
  const _DayRowBody({required this.row});
  final DayRow row;

  @override
  Widget build(BuildContext context) {
    final color = row.highlight ? InoShellTheme.gold : InoShellTheme.text;
    return Row(
      children: [
        SizedBox(
          width: 56,
          child: Text(
            row.day,
            style: TextStyle(
              fontFamily: 'JetBrains Mono',
              fontSize: 12,
              color: color,
              fontWeight: row.highlight ? FontWeight.w600 : FontWeight.w400,
            ),
          ),
        ),
        SizedBox(
          width: 48,
          child: Text(
            row.weather,
            style: const TextStyle(
              fontFamily: 'JetBrains Mono',
              fontSize: 11,
              color: InoShellTheme.muted,
            ),
          ),
        ),
        Expanded(
          child: Text(
            row.plan,
            style: TextStyle(fontSize: 12, color: color),
          ),
        ),
      ],
    );
  }
}

class _ReminderRowBody extends StatelessWidget {
  const _ReminderRowBody({required this.row});
  final ReminderRow row;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Expanded(
          child: Text(
            row.name,
            style: const TextStyle(fontSize: 13, color: InoShellTheme.text),
          ),
        ),
        const SizedBox(width: 8),
        Text(
          row.when,
          style: const TextStyle(
            fontFamily: 'JetBrains Mono',
            fontSize: 12,
            color: InoShellTheme.muted,
          ),
        ),
        if (row.tag != null) ...[
          const SizedBox(width: 8),
          _TagPill(label: row.tag!),
        ],
      ],
    );
  }
}

class _TagPill extends StatelessWidget {
  const _TagPill({required this.label, this.highlight = false});
  final String label;
  final bool highlight;

  @override
  Widget build(BuildContext context) {
    final tone = highlight ? InoShellTheme.gold : InoShellTheme.indigo;
    return Container(
      margin: const EdgeInsets.only(left: 8),
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
      decoration: BoxDecoration(
        color: tone.withValues(alpha: 0.10),
        border: Border.all(color: tone.withValues(alpha: 0.45)),
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        label,
        style: TextStyle(
          fontFamily: 'JetBrains Mono',
          fontSize: 10,
          letterSpacing: 0.4,
          color: tone,
        ),
      ),
    );
  }
}
