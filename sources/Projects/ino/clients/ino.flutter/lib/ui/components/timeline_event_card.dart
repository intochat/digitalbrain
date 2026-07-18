import 'package:flutter/material.dart';
import 'package:ino_flutter/state/timeline_bloc.dart';
import 'package:ino_flutter/ui/components/inspector_drawer.dart';

(IconData, Color) _kindVisual(String kind) {
  return switch (kind) {
    'NeuronActivated' => (Icons.flash_on, Colors.green),
    'SynapseFired' => (Icons.call_split, Colors.blue),
    'ToolInvoked' => (Icons.build, Colors.orange),
    'LlmCallStarted' => (Icons.psychology, Colors.purple),
    'LlmCallCompleted' => (Icons.psychology_alt, Colors.purpleAccent),
    'MemoryStored' => (Icons.memory, Colors.teal),
    'MemoryRecalled' => (Icons.manage_search, Colors.tealAccent),
    'ErrorOccurred' => (Icons.error_outline, Colors.red),
    _ => (Icons.circle, Colors.grey),
  };
}

({String label, Color color}) _decayBadge(int decay) {
  if (decay >= 80) return (label: 'HOT', color: Colors.red);
  if (decay >= 50) return (label: 'WARM', color: Colors.orange);
  if (decay >= 30) return (label: 'COLD', color: Colors.blue);
  return (label: 'FADED', color: Colors.grey);
}

String _formatTime(int epochMs) {
  final dt = DateTime.fromMillisecondsSinceEpoch(epochMs);
  final h = dt.hour.toString().padLeft(2, '0');
  final m = dt.minute.toString().padLeft(2, '0');
  final s = dt.second.toString().padLeft(2, '0');
  return '$h:$m:$s';
}

class TimelineEventCard extends StatelessWidget {
  const TimelineEventCard({super.key, required this.entry});

  final TimelineEntry entry;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    final (icon, iconColor) = _kindVisual(entry.kind);
    final badge = _decayBadge(entry.decay);

    return Card(
      color: colorScheme.surface,
      margin: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
      child: ListTile(
        leading: Icon(icon, color: iconColor, size: 24),
        title: Text(
          entry.kind,
          style: TextStyle(
            color: colorScheme.onSurface,
            fontSize: 14,
            fontWeight: FontWeight.w600,
          ),
        ),
        subtitle: Text(
          '${entry.source} \u2192 ${entry.target} \u00b7 ${_formatTime(entry.timestamp)}',
          style: TextStyle(
            color: colorScheme.onSurface.withAlpha(150),
            fontSize: 12,
          ),
        ),
        trailing: Container(
          padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
          decoration: BoxDecoration(
            color: badge.color.withAlpha(40),
            borderRadius: BorderRadius.circular(6),
          ),
          child: Text(
            badge.label,
            style: TextStyle(
              color: badge.color,
              fontSize: 11,
              fontWeight: FontWeight.w700,
            ),
          ),
        ),
        onTap: () => showInspectorDrawer(context, entry: entry),
      ),
    );
  }
}
